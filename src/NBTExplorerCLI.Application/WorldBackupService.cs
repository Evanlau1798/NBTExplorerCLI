using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;

namespace NBTExplorerCLI.Application;

public sealed class WorldBackupService
{
    private const int MaximumBytes = 67_108_864;
    private readonly IWorldInventory inventory;
    private readonly IWorldBackupStore store;
    public WorldBackupService(IWorldInventory inventory, IWorldBackupStore store)
        => (this.inventory, this.store) = (inventory ?? throw new ArgumentNullException(nameof(inventory)), store ?? throw new ArgumentNullException(nameof(store)));
    public VerifiedBackup Create(IEnumerable<ArtifactPrecondition> sources)
    {
        ArgumentNullException.ThrowIfNull(sources); var inputs = sources.Take(100_001).ToArray();
        if (inputs.Any(input => input is null || input.Artifact is null || input.Image is null)) throw new ArgumentException("Invalid backup source", nameof(sources));
        var manifest = new BackupManifest(inventory.WorldId, inputs.Select(input => new BackupArtifact(input.Artifact, input.Image)));
        var saved = new HashSet<Hash256>();
        foreach (var entry in manifest.Artifacts)
        {
            var bytes = ReadSource(entry);
            if (bytes is not null && saved.Add(entry.Image.Hash!)) store.SaveBlob(entry.Image.Hash!, bytes);
        }
        var json = new UTF8Encoding(false, true).GetBytes(BackupManifestJson.Write(manifest)); var id = Hash256.Compute(json);
        var proof = new VerifiedBackup(manifest, id, store);
        var verified = new HashSet<Hash256>();
        foreach (var entry in manifest.Artifacts)
        {
            if (entry.Image.Hash is { } hash && verified.Add(hash)) _ = proof.ReadArtifact(entry.Artifact);
            _ = ReadSource(entry);
        }
        // Publishing the immutable manifest is the completion boundary after every blob has read back.
        store.SaveManifest(id, json); return Load(id);
    }
    public VerifiedBackup Load(Hash256 id)
    {
        ArgumentNullException.ThrowIfNull(id); var bytes = store.ReadManifest(id) ?? throw new FileNotFoundException("No completed backup manifest");
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Backup manifest exceeds object budget");
        if (Hash256.Compute(bytes) != id) throw new InvalidDataException("Backup manifest content identity differs");
        var manifest = BackupManifestJson.Read(new UTF8Encoding(false, true).GetString(bytes));
        if (manifest.WorldId != inventory.WorldId) throw new ArtifactConflictException("Backup belongs to another world");
        var proof = new VerifiedBackup(manifest, id, store);
        var verified = new HashSet<Hash256>();
        foreach (var entry in manifest.Artifacts) if (entry.Image.Hash is { } hash && verified.Add(hash)) _ = proof.ReadArtifact(entry.Artifact);
        return proof;
    }
    private byte[]? ReadSource(BackupArtifact entry)
    {
        byte[]? bytes; try { bytes = inventory.Read(entry.Artifact); } catch (FileNotFoundException) { bytes = null; }
        if (bytes is { Length: > MaximumBytes }) throw new IOException("Backup source exceeds artifact budget");
        var image = bytes is null ? ArtifactImage.Absent : ArtifactImage.Present(Hash256.Compute(bytes));
        if (image != entry.Image) throw new ArtifactConflictException("Backup source differs from its declared raw preimage"); return bytes;
    }
}

public sealed class VerifiedBackup
{
    public BackupManifest Manifest { get; }
    public Hash256 ManifestHash { get; }
    private readonly IWorldBackupStore store;
    private readonly Dictionary<ArtifactAddress, BackupArtifact> artifacts;
    internal VerifiedBackup(BackupManifest manifest, Hash256 id, IWorldBackupStore store)
        => (Manifest, ManifestHash, this.store, artifacts) = (manifest, id, store, manifest.Artifacts.ToDictionary(entry => entry.Artifact));
    public byte[]? ReadArtifact(ArtifactAddress artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!artifacts.TryGetValue(artifact, out var entry)) throw new ArgumentException("Artifact is not authorized by this backup", nameof(artifact));
        if (entry.Image.ExpectedAbsent) return null;
        var bytes = store.ReadBlob(entry.Image.Hash!) ?? throw new InvalidDataException("Backup blob is missing");
        if (bytes.Length > 67_108_864) throw new InvalidDataException("Backup blob exceeds artifact budget");
        if (Hash256.Compute(bytes) != entry.Image.Hash) throw new InvalidDataException("Backup blob differs from its verified image"); return bytes;
    }
}

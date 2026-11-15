using System.Collections.Immutable;
using Next.Domain;
using Next.Minecraft;
using Next.Formats;

namespace Next.Application;

public sealed record ScanRequest(SnapshotScope Scope, DecodeLimits? Limits = null, int MaximumSources = 100_000);
public sealed record DiscoveredSource(WorldSourceLocation Location, ChunkPosition? Chunk, SourceOutcome Outcome,
    string? DiagnosticCode, Hash256? ContentHash, OperationSupport Capability, PackMetadata? Pack = null, Hash256? PackMetadataHash = null);
public sealed record WorldDiscoveryResult(WorldMetadata? Metadata, SnapshotScope Scope, WorldInventorySnapshot Inventory, ImmutableArray<DiscoveredSource> Sources, string? DiagnosticCode)
{
    // Catalog completeness covers enumeration and container headers; payload extraction is a separate stage.
    public bool Complete => DiagnosticCode is null && Sources.All(source => source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant);
}

public static class WorldDiscovery
{
    public static WorldDiscoveryResult Discover(IWorldInventory inventory, ScanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Scope);
        var limits = request.Limits ?? DecodeLimits.Default; limits.Validate();
        if (request.MaximumSources is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var physical = inventory.Enumerate(); var sources = new List<DiscoveredSource>();
        cancellationToken.ThrowIfCancellationRequested();
        var metadataHashes = new Dictionary<string, Hash256>(StringComparer.Ordinal);
        var changedPacks = new HashSet<string>(StringComparer.Ordinal);
        WorldMetadata? metadata = null; string? diagnostic = physical.DiagnosticCode;
        WorldDiscoveryResult Finish() => new(metadata, request.Scope, physical, sources.Select(source => source.Location.PackRoot is { } root && changedPacks.Contains(root) && source.Outcome == SourceOutcome.Success
            ? source with { Outcome = SourceOutcome.Failed, DiagnosticCode = "PackMetadataChanged", Capability = new() } : source).ToImmutableArray(), diagnostic);
        bool Add(DiscoveredSource source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sources.Count == request.MaximumSources) { diagnostic = "SourceLimitExceeded"; return false; }
            sources.Add(source); return true;
        }
        var activeMetadata = "level.dat"; var validatingMetadata = false;
        NbtDocument ReadMetadata(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            activeMetadata = path;
            var bytes = inventory.Read(new(inventory.WorldId, path, ArtifactKind.Nbt)); metadataHashes[path] = Hash256.Compute(bytes);
            return CompressedNbt.ReadDocument(bytes, NbtCompression.GZip, limits).Document;
        }
        try
        {
            var level = ReadMetadata("level.dat");
            cancellationToken.ThrowIfCancellationRequested();
            // The registry's own version is checked separately; directory names never select a profile.
            var worldGen = physical.Entries.Any(entry => entry.RelativePath == "data/minecraft/world_gen_settings.dat" && entry.Outcome == SourceOutcome.Success)
                ? ReadMetadata("data/minecraft/world_gen_settings.dat") : null;
            validatingMetadata = true; metadata = WorldMetadata.Read(level, worldGen);
        }
        catch (Exception error) when (DiscoveryFailure.IsExpected(error))
        {
            var failure = DiscoveryFailure.Describe(error); diagnostic ??= failure.Code;
            foreach (var entry in physical.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var artifact = new ArtifactAddress(inventory.WorldId, entry.RelativePath, ArtifactKind.Text);
                    if (entry.RelativePath == ".nbtcli-writer.lock" && entry.Outcome == SourceOutcome.Success && !entry.IsDirectory)
                    {
                        if (!Add(new(new(artifact, WorldSourceRole.Irrelevant), null, SourceOutcome.Irrelevant, "InternalWriterLease", null, new()))) break;
                        continue;
                    }
                    var target = entry.RelativePath == activeMetadata && !validatingMetadata;
                    var outcome = target ? failure.Outcome : SourceOutcome.Unsupported;
                    var code = target ? failure.Code : "MetadataUnavailable";
                    var hash = metadataHashes.GetValueOrDefault(entry.RelativePath);
                    if (entry.Outcome != SourceOutcome.Success) { outcome = entry.Outcome; code = entry.DiagnosticCode; }
                    else if (hash is null)
                    {
                        try { hash = Hash256.Compute(inventory.Read(artifact)); }
                        catch (Exception readError) when (DiscoveryFailure.IsExpected(readError)) { (outcome, code) = DiscoveryFailure.Describe(readError); }
                    }
                    if (!Add(new(new(artifact, WorldSourceRole.Unknown), null, outcome, code, hash, new()))) break;
                }
                catch (ArgumentException) { diagnostic = "InvalidArtifactPath"; }
            }
            return Finish();
        }
        var packs = DirectoryPackDiscovery.FindRoots(physical);
        var packStates = new Dictionary<string, DirectoryPackDiscovery.State>(StringComparer.Ordinal);
        foreach (var (root, kind) in packs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Scope.ArtifactPaths.IsEmpty && !request.Scope.ArtifactPaths.Any(path => path == root || path.StartsWith(root + "/", StringComparison.Ordinal))) continue;
            var state = DirectoryPackDiscovery.Read(inventory, root, kind, limits); packStates.Add(root, state);
            if (state.Outcome != SourceOutcome.Success && (request.Scope.ArtifactPaths.IsEmpty || request.Scope.ArtifactPaths.Contains(root, StringComparer.Ordinal)))
            {
                try
                {
                    if (!Add(new(new(new(inventory.WorldId, root, ArtifactKind.Text), WorldSourceRole.PackRoot, PackRoot: root, PackKind: kind), null,
                        state.Outcome, state.Code, null, new()))) return Finish();
                }
                catch (ArgumentException) { diagnostic = "InvalidArtifactPath"; }
            }
        }
        foreach (var entry in physical.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.Scope.ArtifactPaths.IsEmpty && !request.Scope.ArtifactPaths.Contains(entry.RelativePath, StringComparer.Ordinal)) continue;
            WorldSourceLocation location;
            try { location = WorldLayoutLocator.Locate(inventory.WorldId, entry.RelativePath, metadata.Profile, metadata.DimensionDirectories, packs); }
            catch (Exception error) when (DiscoveryFailure.IsExpected(error))
            {
                var failure = DiscoveryFailure.Describe(error);
                try
                {
                    if (!Add(new(new(new(inventory.WorldId, entry.RelativePath, ArtifactKind.Text), WorldSourceRole.Unknown), null, failure.Outcome, failure.Code, null, new()))) break;
                }
                catch (ArgumentException) { diagnostic = "InvalidArtifactPath"; }
                continue;
            }
            if (!WorldSourceScope.Includes(location, request.Scope)) continue;
            if (entry.Outcome != SourceOutcome.Success)
            {
                if (!Add(new(location, null, entry.Outcome, entry.DiagnosticCode, null, new()))) break;
                continue;
            }
            Hash256? rawHash = null;
            try
            {
                if (DirectoryPackDiscovery.Owner(entry.RelativePath, packs) is { } packRoot && packStates.TryGetValue(packRoot, out var packState) && packState.Outcome != SourceOutcome.Success)
                {
                    rawHash = Hash256.Compute(inventory.Read(location.Artifact));
                    if (!Add(new(location with { PackRoot = packRoot, PackKind = packs[packRoot] }, null, packState.Outcome, packState.Code, rawHash, new(), PackMetadataHash: packState.ContentHash))) break;
                    continue;
                }
                if (entry.RelativePath == ".nbtcli-writer.lock")
                {
                    if (!Add(new(location, null, SourceOutcome.Irrelevant, "InternalWriterLease", null, new()))) break;
                    continue;
                }
                if (metadataHashes.TryGetValue(entry.RelativePath, out var metadataHash))
                {
                    if (!Add(new(location, null, SourceOutcome.Success, null, metadataHash, new(Read: SupportLevel.Supported)))) break;
                    continue;
                }
                if (location.Role == WorldSourceRole.Irrelevant)
                {
                    rawHash = Hash256.Compute(inventory.Read(location.Artifact));
                    if (!Add(new(location, null, SourceOutcome.Irrelevant, null, rawHash, new()))) break;
                    continue;
                }
                var bytes = inventory.Read(location.Artifact); var hash = Hash256.Compute(bytes); rawHash = hash;
                if (location.Role == WorldSourceRole.Archive)
                {
                    if (!ArchivePackDiscovery.Discover(location, bytes, limits, Add)) break;
                    continue;
                }
                if (location.PackRoot is not null)
                {
                    if (!packStates.TryGetValue(location.PackRoot, out var state))
                        packStates.Add(location.PackRoot, state = DirectoryPackDiscovery.Read(inventory, location.PackRoot, location.PackKind!.Value, limits));
                    if (entry.RelativePath == location.PackRoot + "/pack.mcmeta" && state.Outcome == SourceOutcome.Success && state.ContentHash != hash)
                        changedPacks.Add(location.PackRoot);
                    if (!Add(new(location, null, state.Outcome, state.Code, hash, new(Read: state.Outcome == SourceOutcome.Success ? SupportLevel.Supported : SupportLevel.Unknown), state.Metadata, state.ContentHash))) break;
                    continue;
                }
                if (location.Role is WorldSourceRole.Terrain or WorldSourceRole.Entities or WorldSourceRole.PointOfInterest)
                {
                    var region = RegionFile.Read(bytes);
                    if (!Add(new(location, null, SourceOutcome.Success, null, hash, new(Read: SupportLevel.Supported)))) break;
                    foreach (var chunk in region.Chunks.Where(chunk => chunk.Present))
                    {
                        var position = new ChunkPosition(checked(location.RegionX!.Value * 32 + chunk.Index % 32), checked(location.RegionZ!.Value * 32 + chunk.Index / 32));
                        var supported = chunk.Capability.Read == SupportLevel.Supported;
                        if (!Add(new(location, position, supported ? SourceOutcome.Success : SourceOutcome.Unsupported, supported ? null : "UnsupportedCodec", hash, new(Read: chunk.Capability.Read)))) return Finish();
                    }
                }
                else
                {
                    var known = location.Role != WorldSourceRole.Unknown;
                    if (!Add(new(location, null, known ? SourceOutcome.Success : SourceOutcome.Unsupported, known ? null : "UnknownSourceLayout", hash,
                        new(Read: known ? SupportLevel.Supported : SupportLevel.Unknown)))) break;
                }
            }
            catch (Exception error) when (DiscoveryFailure.IsExpected(error))
            {
                var failure = DiscoveryFailure.Describe(error);
                if (!Add(new(location, null, failure.Outcome, failure.Code, rawHash, new()))) break;
            }
        }
        return Finish();
    }
}

internal static class DiscoveryFailure
{
    internal static bool IsExpected(Exception error) => error is CodecException or SnbtException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Text.Json.JsonException;
    internal static (SourceOutcome Outcome, string Code) Describe(Exception error) => error switch
    {
        SnbtException { Code: SnbtError.UnsupportedGrammar or SnbtError.UnsupportedValue } => (SourceOutcome.Unsupported, "UnsupportedSyntax"),
        SnbtException { Code: SnbtError.LimitExceeded } => (SourceOutcome.Failed, "LimitExceeded"),
        SnbtException => (SourceOutcome.Failed, "InvalidSnbt"),
        CodecException { Code: CodecError.UnsupportedProfile } => (SourceOutcome.Unsupported, "UnsupportedProfile"),
        CodecException codec => (SourceOutcome.Failed, codec.Code.ToString()),
        NotSupportedException => (SourceOutcome.Unsupported, "UnsupportedSource"),
        System.Text.Json.JsonException => (SourceOutcome.Failed, "InvalidJson"),
        InvalidDataException => (SourceOutcome.Failed, "InvalidContainer"),
        _ => (SourceOutcome.Failed, "SourceReadFailed")
    };
}

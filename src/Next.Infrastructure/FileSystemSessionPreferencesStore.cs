using System.Text.Json;
using System.Text.Json.Serialization;
using Next.Application;

namespace Next.Infrastructure;

public sealed class FileSystemSessionPreferencesStore
{
    private const string Family = "nbtcli.next.session-preferences";
    private sealed record Settings(string SchemaFamily, int SchemaVersion, Guid SessionId, bool ShowGroups);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    private readonly AtomicStateRepository repository;
    public FileSystemSessionPreferencesStore(string root) => repository = new(root, Family + "-store", new ArtifactFileIo());
    public bool? Read(Guid id)
    {
        var bytes = repository.Read(Key(id)); if (bytes is null) return null;
        if (bytes.Length > 4096) throw new InvalidDataException("Session preference size exceeds its budget");
        var value = JsonSerializer.Deserialize<Settings>(bytes, Options) ?? throw new InvalidDataException("Missing session preferences");
        if (value.SchemaFamily != Family || value.SchemaVersion != 1) throw new NotSupportedException("Unsupported session preference family/version");
        if (value.SessionId != id || !JsonSerializer.SerializeToUtf8Bytes(value, Options).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Invalid session preference binding or encoding");
        return value.ShowGroups;
    }
    public void Create(Guid id, bool showGroups)
    {
        var key = Key(id); var bytes = JsonSerializer.SerializeToUtf8Bytes(new Settings(Family, 1, id, showGroups), Options);
        void Same(byte[] previous) { if (!previous.AsSpan().SequenceEqual(bytes)) throw new ArtifactConflictException("Start replay changes the retained session display preferences"); }
        var current = repository.Read(key); if (current is not null) { Same(current); return; }
        try { repository.Write(key, bytes, null); }
        catch (ArtifactConflictException) { Same(repository.Read(key) ?? throw new IOException("Session preferences disappeared after concurrent publication")); }
    }
    private static string Key(Guid id) => id == Guid.Empty ? throw new ArgumentException("Session identity is required", nameof(id)) : "session-" + id.ToString("N") + ".json";
}

using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;

namespace Next.Infrastructure;

public sealed class FileSystemAgentConfigurationStore : IAgentConfigurationStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemAgentConfigurationStore(string root) => repository = new(root, "nbtcli.next.agent-configuration-store", new ArtifactFileIo());
    public byte[]? Read() => repository.Read("configuration.json");
    public void Write(ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        _ = AgentConfigurationJson.ReadState(new UTF8Encoding(false, true).GetString(bytes.Span));
        repository.Write("configuration.json", bytes, expected);
    }
}

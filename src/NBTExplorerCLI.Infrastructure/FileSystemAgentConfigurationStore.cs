using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Infrastructure;

public sealed class FileSystemAgentConfigurationStore : IAgentConfigurationStore
{
    private readonly AtomicStateRepository repository;
    public FileSystemAgentConfigurationStore(string root) => repository = new(root, "nbtcli.agent-configuration-store", new ArtifactFileIo());
    public byte[]? Read() => repository.Read("configuration.json");
    public void Write(ReadOnlyMemory<byte> bytes, Hash256? expected)
    {
        _ = AgentConfigurationJson.ReadState(new UTF8Encoding(false, true).GetString(bytes.Span));
        repository.Write("configuration.json", bytes, expected);
    }
}

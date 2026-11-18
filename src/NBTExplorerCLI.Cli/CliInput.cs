using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;

namespace NBTExplorerCLI.Cli;

internal static class CliInput
{
    private static readonly Guid Namespace = new("645ee403-a9b0-45ec-89ea-e49cd98bbf59");
    internal static string Read(string file) => Decode(ReadBytes(file));
    internal static byte[] ReadBytes(string file, int maximumBytes = 67_108_864)
    {
        if (maximumBytes is < 1 or > 67_108_864) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (file != "-")
        {
            var path = Path.GetFullPath(file);
            return new ArtifactStorage(Path.GetDirectoryName(path)!, Namespace).ReadBounded(new(Namespace, Path.GetFileName(path), ArtifactKind.Json), maximumBytes);
        }
        else
        {
            using var input = Console.OpenStandardInput(); using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, maximumBytes - output.Length + 1))) != 0)
            {
                if (output.Length + count > maximumBytes) throw new CliFailure("RESOURCE_LIMIT", "Input exceeds byte budget");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
    }
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error) { throw new JsonException("Invalid UTF8 input", error); }
    }
}

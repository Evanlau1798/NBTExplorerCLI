using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Infrastructure;

namespace Next.Cli;

internal static class CliInput
{
    private static readonly Guid Namespace = new("645ee403-a9b0-45ec-89ea-e49cd98bbf59");
    internal static string Read(string file)
    {
        byte[] bytes;
        if (file != "-")
        {
            var path = Path.GetFullPath(file); bytes = new ArtifactStorage(Path.GetDirectoryName(path)!, Namespace).Read(new(Namespace, Path.GetFileName(path), ArtifactKind.Json));
        }
        else
        {
            using var input = Console.OpenStandardInput(); using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = input.Read(buffer)) != 0) { if (output.Length + count > 67_108_864) throw new CliFailure("RESOURCE_LIMIT", "Input exceeds byte budget"); output.Write(buffer, 0, count); }
            bytes = output.ToArray();
        }
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error) { throw new JsonException("Invalid UTF8 input", error); }
    }
}

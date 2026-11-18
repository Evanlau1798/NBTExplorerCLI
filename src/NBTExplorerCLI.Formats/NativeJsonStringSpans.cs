using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class NativeJsonStringSpans
{
    public static Dictionary<TypedPath, SnbtSpan> Read(byte[] bytes, DecodeLimits? limits = null)
    {
        using var observation = WorkDiagnostics.Start("json.spans");
        var result = NativeJsonValueSpans.ReadStrings(bytes, limits);
        observation?.SetTag("bytes", bytes.LongLength);
        return result;
    }
}

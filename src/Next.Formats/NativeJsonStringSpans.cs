using Next.Domain;

namespace Next.Formats;

public static class NativeJsonStringSpans
{
    public static Dictionary<TypedPath, SnbtSpan> Read(byte[] bytes, DecodeLimits? limits = null)
        => NativeJsonValueSpans.ReadStrings(bytes, limits);
}

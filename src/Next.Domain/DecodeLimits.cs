namespace Next.Domain;

public sealed record DecodeLimits(long MaxDecodedBytes = 134_217_728, int MaxDepth = 256, long MaxNodes = 2_000_000,
    long MaxContainerEntries = 1_000_000, long MaxCollectionElements = 1_000_000, long MaxStringBytes = 8_388_608,
    int MaxMutf8StringBytes = 65_535, long MaxCompressedBytes = 67_108_864)
{
    public static DecodeLimits Default { get; } = new();
    public void Validate()
    {
        if (MaxDecodedBytes is < 1 or > 134_217_728 || MaxDepth is < 0 or > 256 || MaxNodes is < 1 or > 2_000_000
            || MaxContainerEntries is < 0 or > 1_000_000 || MaxCollectionElements is < 0 or > 1_000_000
            || MaxStringBytes is < 0 or > 8_388_608 || MaxMutf8StringBytes is < 0 or > 65_535 || MaxCompressedBytes is < 1 or > 67_108_864)
            throw new ArgumentOutOfRangeException(nameof(DecodeLimits), "Invalid codec resource limits");
    }
}

public enum CodecError { Truncated, InvalidTag, InvalidLength, InvalidList, MalformedString, LimitExceeded, TrailingData, UnsupportedProfile }
public sealed class CodecException(CodecError code, long offset, string message) : Exception(message)
{
    public CodecError Code { get; } = code;
    public long Offset { get; } = offset;
}
public enum BinaryNbtProfile { JavaNamed }
public sealed record CodecMetrics(long Bytes, long Nodes, long ContainerEntries, long CollectionElements, long StringBytes, int MaximumDepth);
public sealed record NbtReadResult(NbtDocument Document, long ConsumedBytes, CodecMetrics Metrics);

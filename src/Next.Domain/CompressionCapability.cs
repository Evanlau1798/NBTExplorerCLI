namespace Next.Domain;

public enum NbtCompression : byte { GZip = 1, Zlib = 2, Raw = 3, Lz4 = 4 }
public sealed record CompressionCapability(byte CodecId, bool External, SupportLevel Read, SupportLevel Write)
{
    public static CompressionCapability For(byte codecId, bool external = false)
    {
        var level = !external && codecId is 1 or 2 or 3 ? SupportLevel.Supported : SupportLevel.Unsupported;
        return new(codecId, external, level, level);
    }
}

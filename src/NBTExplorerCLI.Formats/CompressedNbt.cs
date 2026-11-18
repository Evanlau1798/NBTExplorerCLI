using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class CompressedNbt
{
    public static NbtReadResult ReadDocument(byte[] source, NbtCompression codec, DecodeLimits? limits = null)
        => BinaryNbt.ReadDocument(NbtCompressionCodec.Decode(source, codec, limits).Bytes, limits: limits);
    public static NbtReadResult ReadDocument(Stream source, NbtCompression codec, DecodeLimits? limits = null)
        => BinaryNbt.ReadDocument(NbtCompressionCodec.Decode(source, codec, limits).Bytes, limits: limits);
    public static byte[] WriteDocument(NbtDocument document, NbtCompression codec, DecodeLimits? limits = null)
        => NbtCompressionCodec.Encode(BinaryNbt.WriteDocument(document, limits), codec, limits);
}

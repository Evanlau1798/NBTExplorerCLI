using Next.Domain;

namespace Next.Formats;

public static class BinaryNbt
{
    public static NbtReadResult ReadDocument(byte[] input, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var stream = new MemoryStream(input, writable: false);
        return ReadDocument(stream, profile, limits);
    }
    public static NbtReadResult ReadDocument(Stream input, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed, DecodeLimits? limits = null)
    {
        using var observation = WorkDiagnostics.Start("nbt.read");
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input must be readable", nameof(input));
        var result = new BinaryNbtReader(input, Checked(profile, limits)).Read();
        observation?.SetTag("bytes", result.ConsumedBytes);
        return result;
    }
    public static CodecMetrics Validate(byte[] input, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed, DecodeLimits? limits = null)
        => ReadDocument(input, profile, limits).Metrics;
    public static CodecMetrics MeasureDocument(NbtDocument document, DecodeLimits? limits = null, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new BinaryNbtWriter(null, Checked(profile, limits)).Write(document);
    }
    public static byte[] WriteDocument(NbtDocument document, DecodeLimits? limits = null, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed)
    {
        using var observation = WorkDiagnostics.Start("nbt.write");
        ArgumentNullException.ThrowIfNull(document);
        var policy = Checked(profile, limits);
        var measured = new BinaryNbtWriter(null, policy).Write(document);
        using var output = new MemoryStream(checked((int)measured.Bytes));
        var written = new BinaryNbtWriter(output, policy).Write(document);
        observation?.SetTag("bytes", written.Bytes);
        return output.ToArray();
    }
    public static CodecMetrics WriteDocument(NbtDocument document, Stream output, DecodeLimits? limits = null, BinaryNbtProfile profile = BinaryNbtProfile.JavaNamed)
    {
        using var observation = WorkDiagnostics.Start("nbt.write");
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Output must be writable", nameof(output));
        var policy = Checked(profile, limits);
        _ = new BinaryNbtWriter(null, policy).Write(document);
        var written = new BinaryNbtWriter(output, policy).Write(document);
        observation?.SetTag("bytes", written.Bytes);
        return written;
    }
    private static DecodeLimits Checked(BinaryNbtProfile profile, DecodeLimits? limits)
    {
        if (profile != BinaryNbtProfile.JavaNamed) throw new CodecException(CodecError.UnsupportedProfile, 0, "Unsupported binary NBT profile");
        var policy = limits ?? DecodeLimits.Default;
        policy.Validate();
        return policy;
    }
}

using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Formats;

public static class ModifiedUtf8
{
    public static string Decode(ReadOnlySpan<byte> bytes, long byteOffset = 0)
    {
        if (byteOffset < 0 || byteOffset > long.MaxValue - bytes.Length) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        if (bytes.Length > 65_535) throw new CodecException(CodecError.LimitExceeded, byteOffset, "MUTF-8 string exceeds 65535 bytes");
        var chars = new char[bytes.Length];
        var count = 0;
        for (var index = 0; index < bytes.Length;)
        {
            var first = bytes[index];
            if (first < 0x80) { chars[count++] = (char)first; index++; continue; }
            var width = first is >= 0xc0 and <= 0xdf ? 2 : first is >= 0xe0 and <= 0xef ? 3 : 0;
            if (width == 0 || bytes.Length - index < width)
                throw new CodecException(CodecError.MalformedString, byteOffset + index, "Invalid or truncated MUTF-8 sequence");
            var value = first & (width == 2 ? 0x1f : 0x0f);
            for (var offset = 1; offset < width; offset++)
            {
                var continuation = bytes[index + offset];
                if ((continuation & 0xc0) != 0x80)
                    throw new CodecException(CodecError.MalformedString, byteOffset + index + offset, "Invalid MUTF-8 continuation");
                value = (value << 6) | (continuation & 0x3f);
            }
            chars[count++] = (char)value;
            index += width;
        }
        return new string(chars, 0, count);
    }
    public static byte[] Encode(string text, int maximumBytes = 65_535)
    {
        var output = new byte[EncodedLength(text, maximumBytes)];
        var index = 0;
        foreach (var unit in text)
        {
            if (unit is > '\0' and <= '\u007f') output[index++] = (byte)unit;
            else if (unit <= '\u07ff')
            {
                output[index++] = (byte)(0xc0 | (unit >> 6));
                output[index++] = (byte)(0x80 | (unit & 0x3f));
            }
            else
            {
                output[index++] = (byte)(0xe0 | (unit >> 12));
                output[index++] = (byte)(0x80 | ((unit >> 6) & 0x3f));
                output[index++] = (byte)(0x80 | (unit & 0x3f));
            }
        }
        return output;
    }
    public static int EncodedLength(string text, int maximumBytes = 65_535)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumBytes is < 0 or > 65_535) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var length = 0;
        foreach (var unit in text)
        {
            length += unit is > '\0' and <= '\u007f' ? 1 : unit <= '\u07ff' ? 2 : 3;
            if (length > maximumBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Encoded MUTF-8 string exceeds its byte limit");
        }
        return length;
    }
}

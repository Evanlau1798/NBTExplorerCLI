using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text.Json;

namespace NBTExplorerCLI.Minecraft;

public sealed record PackMetadata(PackSourceKind Kind, int? IntegerFormat, PackFormatVersion Minimum, PackFormatVersion Maximum)
{
    public bool DeclaresSupport(PackFormatVersion version) => Minimum.CompareTo(version) <= 0 && Maximum.CompareTo(version) >= 0;
    public static PackMetadata Read(byte[] source, PackSourceKind kind, DecodeLimits? limits = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        using var document = BoundedJson.ReadDecoded(source, limits);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetNativeProperty("pack", out var pack) || pack.ValueKind != JsonValueKind.Object
            || !pack.TryGetNativeProperty("description", out var description)
            || description.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new JsonException("Missing pack section or description");
        int? formatNumber = pack.TryGetNativeProperty("pack_format", out var format) ? Integer(format) : null;
        var hasMinimum = pack.TryGetNativeProperty("min_format", out var min);
        var hasMaximum = pack.TryGetNativeProperty("max_format", out var max);
        var hasSupported = pack.TryGetNativeProperty("supported_formats", out var supported);
        var threshold = kind == PackSourceKind.Datapack ? 82 : 65;
        PackFormatVersion minimum, maximum;
        if (hasMinimum || hasMaximum)
        {
            if (!hasMinimum || !hasMaximum) throw new JsonException("Incomplete pack format range");
            minimum = Endpoint(min, false); maximum = Endpoint(max, true);
            if (minimum.Major < threshold)
            {
                if (formatNumber is null || !hasSupported) throw new JsonException("Compatibility with a lower pack format requires integer format fields");
                _ = IntegerRange(supported);
            }
            else if (hasSupported) throw new JsonException("Modern pack cannot declare supported_formats");
        }
        else
        {
            if (formatNumber is null || formatNumber >= threshold) throw new JsonException("Missing pack format range");
            var range = hasSupported ? IntegerRange(supported) : (formatNumber.Value, formatNumber.Value);
            minimum = new(range.Item1, 0); maximum = new(range.Item2, int.MaxValue);
        }
        if (minimum.CompareTo(maximum) > 0 || formatNumber is not null && (formatNumber < minimum.Major || formatNumber > maximum.Major))
            throw new JsonException("Inconsistent pack format range");
        return new(kind, formatNumber, minimum, maximum);
    }
    private static int Integer(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : throw new JsonException("Expected nonnegative pack format integer");
    private static PackFormatVersion Endpoint(JsonElement value, bool maximum)
    {
        if (value.ValueKind != JsonValueKind.Array) return new(Integer(value), maximum ? int.MaxValue : 0);
        if (value.GetArrayLength() is < 1 or > 2) throw new JsonException("Expected one or two format components");
        return new(Integer(value[0]), value.GetArrayLength() == 2 ? Integer(value[1]) : maximum ? int.MaxValue : 0);
    }
    private static (int, int) IntegerRange(JsonElement value)
    {
        int min, max;
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2)
        { min = Integer(value[0]); max = Integer(value[1]); }
        else if (value.ValueKind == JsonValueKind.Object && value.TryGetNativeProperty("min_inclusive", out var lower) && value.TryGetNativeProperty("max_inclusive", out var upper))
        { min = Integer(lower); max = Integer(upper); }
        else { min = max = Integer(value); }
        if (min > max) throw new JsonException("Reversed formatNumber pack range");
        return (min, max);
    }
}

using Next.Domain;
using Next.Formats;
using System.Text.Json;

namespace Next.Minecraft;

public sealed record PackMetadata(PackSourceKind Kind, int? LegacyFormat, PackFormatVersion Minimum, PackFormatVersion Maximum)
{
    public bool DeclaresSupport(PackFormatVersion version) => Minimum.CompareTo(version) <= 0 && Maximum.CompareTo(version) >= 0;
    public static PackMetadata Read(byte[] source, PackSourceKind kind, DecodeLimits? limits = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        using var document = BoundedJson.ReadDecoded(source, limits);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pack", out var pack) || pack.ValueKind != JsonValueKind.Object
            || !pack.TryGetProperty("description", out var description)
            || description.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new JsonException("Missing pack section or description");
        int? legacy = pack.TryGetProperty("pack_format", out var format) ? Integer(format) : null;
        var hasMinimum = pack.TryGetProperty("min_format", out var min);
        var hasMaximum = pack.TryGetProperty("max_format", out var max);
        var hasSupported = pack.TryGetProperty("supported_formats", out var supported);
        var threshold = kind == PackSourceKind.Datapack ? 82 : 65;
        PackFormatVersion minimum, maximum;
        if (hasMinimum || hasMaximum)
        {
            if (!hasMinimum || !hasMaximum) throw new JsonException("Incomplete pack format range");
            minimum = Endpoint(min, false); maximum = Endpoint(max, true);
            if (minimum.Major < threshold)
            {
                if (legacy is null || !hasSupported) throw new JsonException("Legacy compatibility requires legacy fields");
                _ = LegacyRange(supported);
            }
            else if (hasSupported) throw new JsonException("Modern pack cannot declare supported_formats");
        }
        else
        {
            if (legacy is null || legacy >= threshold) throw new JsonException("Missing pack format range");
            var range = hasSupported ? LegacyRange(supported) : (legacy.Value, legacy.Value);
            minimum = new(range.Item1, 0); maximum = new(range.Item2, int.MaxValue);
        }
        if (minimum.CompareTo(maximum) > 0 || legacy is not null && (legacy < minimum.Major || legacy > maximum.Major))
            throw new JsonException("Inconsistent pack format range");
        return new(kind, legacy, minimum, maximum);
    }
    private static int Integer(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : throw new JsonException("Expected nonnegative pack format integer");
    private static PackFormatVersion Endpoint(JsonElement value, bool maximum)
    {
        if (value.ValueKind != JsonValueKind.Array) return new(Integer(value), maximum ? int.MaxValue : 0);
        if (value.GetArrayLength() is < 1 or > 2) throw new JsonException("Expected one or two format components");
        return new(Integer(value[0]), value.GetArrayLength() == 2 ? Integer(value[1]) : maximum ? int.MaxValue : 0);
    }
    private static (int, int) LegacyRange(JsonElement value)
    {
        int min, max;
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2)
        { min = Integer(value[0]); max = Integer(value[1]); }
        else if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("min_inclusive", out var lower) && value.TryGetProperty("max_inclusive", out var upper))
        { min = Integer(lower); max = Integer(upper); }
        else { min = max = Integer(value); }
        if (min > max) throw new JsonException("Reversed legacy pack range");
        return (min, max);
    }
}

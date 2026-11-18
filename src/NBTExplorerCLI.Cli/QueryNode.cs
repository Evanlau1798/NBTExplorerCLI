using System.Globalization;
using System.Text.Json;
using System.Text;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Cli;

internal sealed class QueryNode
{
    private static readonly UTF8Encoding ScalarKeyEncoding = new(false, true);
    internal NbtValue? Nbt { get; }
    private JsonElement? Json { get; }
    internal QueryNode(NbtValue value) => Nbt = value;
    internal QueryNode(JsonElement value) => Json = value;
    internal string Type => Nbt?.Type.ToString() ?? Json!.Value.ValueKind.ToString();
    internal bool Array => Nbt is NbtList or NbtByteArray or NbtIntArray or NbtLongArray || Json?.ValueKind == JsonValueKind.Array;
    internal QueryNode Child(PathSegment segment)
    {
        if (Nbt is not null)
        {
            var result = NbtLocator.Resolve(Nbt, new([segment]));
            return result.Status switch { ResolutionStatus.Found => new(result.Value!), ResolutionStatus.Ambiguous => throw new CliFailure("AMBIGUOUS_PATH"), ResolutionStatus.Missing => throw new CliFailure("NOT_FOUND"), _ => throw new CliFailure("INVALID_ARGUMENTS", "Path segment differs from its container type") };
        }
        var json = Json!.Value;
        if (segment.Kind == PathSegmentKind.Key && json.ValueKind == JsonValueKind.Object && json.TryGetNativeProperty(segment.Key!, out var field)) return new(field);
        if (segment.Kind == PathSegmentKind.Index && json.ValueKind == JsonValueKind.Array && segment.Index < json.GetArrayLength()) return new(json[segment.Index!.Value]);
        throw new CliFailure("NOT_FOUND");
    }
    internal IEnumerable<(PathSegment Segment, string Name, QueryNode Node)> Children()
    {
        switch (Nbt)
        {
            case NbtCompound compound:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in compound.Entries) if (!names.Add(entry.Name)) throw new CliFailure("AMBIGUOUS_PATH", "Duplicate compound keys need typed whole-document inspection");
                foreach (var entry in compound.Entries) yield return (PathSegment.ForKey(entry.Name), entry.Name, new(entry.Value));
                yield break;
            case NbtList list:
                for (var i = 0; i < list.Items.Length; i++) yield return (PathSegment.ForIndex(i), "[" + i + "]", new(list.Items[i])); yield break;
            case NbtByteArray array:
                for (var i = 0; i < array.Values.Length; i++) yield return (PathSegment.ForIndex(i), "[" + i + "]", new(new NbtByte(array.Values[i]))); yield break;
            case NbtIntArray array:
                for (var i = 0; i < array.Values.Length; i++) yield return (PathSegment.ForIndex(i), "[" + i + "]", new(new NbtInt(array.Values[i]))); yield break;
            case NbtLongArray array:
                for (var i = 0; i < array.Values.Length; i++) yield return (PathSegment.ForIndex(i), "[" + i + "]", new(new NbtLong(array.Values[i]))); yield break;
        }
        if (Json is not { } json) yield break;
        if (json.ValueKind == JsonValueKind.Object)
            foreach (var entry in json.EnumerateObject()) { var name = NativeJsonStrings.PropertyName(entry); yield return (PathSegment.ForKey(name), name, new(entry.Value)); }
        if (json.ValueKind == JsonValueKind.Array)
        { var i = 0; foreach (var entry in json.EnumerateArray()) { yield return (PathSegment.ForIndex(i), "[" + i++ + "]", new(entry)); } }
    }
    internal string? Scalar => Nbt switch
    {
        NbtString text => text.Value, NbtByte number => number.Value.ToString(CultureInfo.InvariantCulture), NbtShort number => number.Value.ToString(CultureInfo.InvariantCulture),
        NbtInt number => number.Value.ToString(CultureInfo.InvariantCulture), NbtLong number => number.Value.ToString(CultureInfo.InvariantCulture),
        NbtFloat number => number.Value.ToString("R", CultureInfo.InvariantCulture), NbtDouble number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        _ => Json is { } json ? json.ValueKind switch { JsonValueKind.String => NativeJsonStrings.Read(json), JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => json.GetRawText(), _ => null } : null
    };
    internal object? Display()
    {
        if (Json is { } json) return json.Clone();
        return Nbt switch
        {
            NbtString text => text.Value, NbtByte number => number.Value, NbtShort number => number.Value, NbtInt number => number.Value, NbtLong number => number.Value,
            NbtFloat number when float.IsFinite(number.Value) => number.Value, NbtDouble number when double.IsFinite(number.Value) => number.Value,
            NbtFloat number => new { type = "Float", bits = number.RawBits.ToString("x8", CultureInfo.InvariantCulture), display = Scalar },
            NbtDouble number => new { type = "Double", bits = number.RawBits.ToString("x16", CultureInfo.InvariantCulture), display = Scalar },
            NbtCompound compound => DisplayCompound(compound),
            _ => Children().Select(child => child.Node.Display()).ToArray()
        };
    }
    private object DisplayCompound(NbtCompound compound)
    {
        try { foreach (var entry in compound.Entries) _ = ScalarKeyEncoding.GetByteCount(entry.Name); }
        catch (EncoderFallbackException) { throw new CliFailure("UNSUPPORTED_SOURCE", "Use --types to inspect exact non-scalar UTF16 compound keys"); }
        return Children().ToDictionary(child => child.Name, child => child.Node.Display(), StringComparer.Ordinal);
    }
}

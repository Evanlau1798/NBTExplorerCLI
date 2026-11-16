using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Next.Domain;

namespace Next.Formats;

public static class LegacyInputReader
{
    public const int MaximumInputBytes = 16_777_216;
    public static LegacyParsedInput Read(LegacyInputProfile profile, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!Enum.IsDefined(profile) || bytes.Length > MaximumInputBytes) throw new ArgumentException("Invalid or oversized legacy input");
        LegacyParsedInput Whole(string code) => new([new(0, null, null, null, null, null, null, null, null, 1, code)], code, null);
        if (profile is LegacyInputProfile.ScanCache or LegacyInputProfile.Unknown) return Whole(profile == LegacyInputProfile.ScanCache ? "LegacyCacheRequiresRescan" : "UnidentifiedLegacyProfile");
        if (profile == LegacyInputProfile.NotesText)
        {
            try { return new([new(0, null, null, new UTF8Encoding(false, true).GetString(bytes), null, null, null, null, null, 1)], null, null); }
            catch (DecoderFallbackException) { return Whole("InvalidLegacyUtf8"); }
        }
        try
        {
            using var document = BoundedJson.ReadDecoded(bytes, new(MaxDecodedBytes: MaximumInputBytes, MaxNodes: 500_000, MaxContainerEntries: 100_000, MaxCollectionElements: 100_000));
            var root = document.RootElement; var records = ImmutableArray.CreateBuilder<LegacyRecord>();
            if (profile is LegacyInputProfile.CommandSession or LegacyInputProfile.TranslationSession)
            {
                if (root.ValueKind != JsonValueKind.Object || Text(root, "WorldPath") is null || !root.TryGetNativeProperty("Entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return Whole("UnrecognizedLegacySession");
                foreach (var entry in entries.EnumerateArray()) records.Add(Record(entry, records.Count, false));
                return records.Count == 0 ? Whole("EmptyLegacySession") : new(records.ToImmutable(), null, Text(root, "WorldPath"));
            }
            if (profile == LegacyInputProfile.ReplyArray)
            {
                if (root.ValueKind != JsonValueKind.Array) return Whole("UnrecognizedLegacyReply");
                foreach (var entry in root.EnumerateArray()) records.Add(Record(entry, records.Count, true));
                return records.Count == 0 ? Whole("EmptyLegacyReply") : new(records.ToImmutable(), null, null);
            }
            if (root.ValueKind != JsonValueKind.Object) return Whole("UnrecognizedLegacyObject");
            if (profile is LegacyInputProfile.Glossary or LegacyInputProfile.KeyMapping)
            {
                foreach (var property in root.EnumerateObject())
                {
                    var key = NativeJsonStrings.PropertyName(property); var value = property.Value;
                    var translated = value.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(value) : Text(value, profile == LegacyInputProfile.Glossary ? "translation" : "translated");
                    records.Add(new(records.Count, key, profile == LegacyInputProfile.Glossary ? key : Text(value, "original"), translated, null, null, null, null, null, 1, translated is null ? "UnrecognizedLegacyValue" : null));
                }
                return records.Count == 0 ? Whole("EmptyLegacyObject") : new(records.ToImmutable(), null, null);
            }
            var known = profile == LegacyInputProfile.AgentConfig ? Text(root, "SourceLanguage") is not null && Text(root, "TargetLanguage") is not null
                : root.TryGetNativeProperty("agent_identity", out var identity) && identity.ValueKind == JsonValueKind.Object
                    && root.TryGetNativeProperty("translation_contract", out var contract) && Text(contract, "source_language") is not null && Text(contract, "target_language") is not null;
            return known ? new([new(0, null, null, null, null, null, null, null, null, 1)], null, null) : Whole("UnrecognizedLegacySettings");
        }
        catch (Exception error) when (error is JsonException or CodecException or ArgumentException or OverflowException or InvalidOperationException)
        { return Whole(error is CodecException { Code: CodecError.LimitExceeded } ? "LegacyInputLimit" : "InvalidLegacyJson"); }
    }
    private static LegacyRecord Record(JsonElement item, int index, bool reply)
    {
        if (item.ValueKind != JsonValueKind.Object) return new(index, null, null, null, null, null, null, null, null, 1, "InvalidLegacyRecord");
        var id = Text(item, reply ? "key" : "Id"); var original = Text(item, reply ? "original" : "Original");
        var translated = Text(item, reply ? "translated" : "Translated"); var count = 1; string? problem = null; Hash256? hash = null;
        if (id is null || original is null) problem = "MissingLegacyIdentityOrText";
        if (item.TryGetNativeProperty(reply ? "translated" : "Translated", out var raw) && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) problem = "LegacyTranslationIsNotText";
        if (item.TryGetNativeProperty("LineCount", out var lines) && (lines.ValueKind != JsonValueKind.Number || !lines.TryGetInt32(out count) || count < 1)) problem = "InvalidLegacyLineCount";
        if (item.TryGetNativeProperty("LineIndex", out var line) && (line.ValueKind != JsonValueKind.Number || !line.TryGetInt32(out var number) || number != 0)) problem = "LegacyFragmentAlignmentMissing";
        if (item.TryGetNativeProperty("LinkedIds", out var linked) && linked.ValueKind != JsonValueKind.Null && (linked.ValueKind != JsonValueKind.Array || linked.GetArrayLength() != 0)) problem = "LegacyLinkedAlignmentMissing";
        if (item.TryGetNativeProperty("SourceHash", out var source) && source.ValueKind != JsonValueKind.Null)
        {
            try { hash = new(NativeJsonStrings.Read(source)); } catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException) { problem = "InvalidLegacySourceHash"; }
            if (problem is null && Text(item, "SourceHashKind") != "raw-artifact-sha256") problem = "UnrecognizedLegacySourceHashMeaning";
        }
        return new(index, id, original, translated, Text(item, "Path"), Text(item, "Type"), Text(item, "Context"), Text(item, "Dimension"), hash, count, problem);
    }
    private static string? Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetNativeProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(value) : null;
}

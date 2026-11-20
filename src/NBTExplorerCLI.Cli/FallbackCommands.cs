using System.Globalization;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal static class FallbackCommands
{
    private const string ReplyFamily = "nbtcli.translation-draft-reply";
    private const int ReplyVersion = 2;
    private static readonly DecodeLimits Limits = DecodeLimits.Default;
    private static readonly PlaceholderPolicy Protection = new([], protectVisibleText: true, protectPrivateUseGlyphs: true);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static CliResult Execute(CliArguments request)
    {
        var line = request.Get("line");
        var byteRange = request.Get("byte-range");
        var textPointer = request.Get("text-pointer");
        var format = request.Get("format") ?? (line is not null || byteRange is not null ? "snbt" : "auto");
        if (format is not ("auto" or "json" or "snbt" or "text"))
            throw new CliFailure("INVALID_ARGUMENTS", "Choose auto, json, snbt or text for --format");
        if (line is not null && (byteRange is not null || format is not ("snbt" or "text"))
            || byteRange is not null && format == "auto" || request.Flag("snbt-profile") && format != "snbt")
            throw new CliFailure("INVALID_ARGUMENTS", "Choose one compatible line or byte-range selection and format");
        if (textPointer is { Length: > 0 } && !textPointer.StartsWith('/') || textPointer is not null && format == "text")
            throw new CliFailure("INVALID_ARGUMENTS", "Use a relative JSON pointer for an exact literal in a structured selection");
        if (request.Get("output") is { } output)
        {
            var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Input!));
            if (Directory.Exists(input)) ScanCommands.OutsideWorld(input, output);
        }
        using var source = new QuerySource(request, readPayload: format != "auto");
        var payload = Read(source, request, format);
        using var reply = request.Get("reply") is { } file ? BoundedJson.ReadDecoded(CliInput.ReadBytes(file), Limits) : null;
        if (reply is not null) ValidateReplyDocument(reply.RootElement);
        var unverified = new List<TypedPath>();
        var candidateList = Candidates(source, payload, textPointer, unverified).ToArray();
        if (textPointer is not null && !candidateList.Any(candidate => Pointer(candidate.Leaf.Path) == textPointer))
            throw new CliFailure("UNSUPPORTED_SOURCE", "The confirmed path must select an editable literal within the draft");
        var sourceOption = TranslationLanguages.SourceOption(request.Get("source-language"));
        var targetOption = request.Get("target-language");
        var sourceLocale = reply is not null && sourceOption == TranslationLanguages.Automatic
            ? BoundLocale(reply.RootElement, "sourceLocale", allowUndetermined: true)
            : sourceOption == TranslationLanguages.Automatic
                ? (payload.Representation == "json" ? TranslationLanguages.ResourceSource(source.Artifact) : null)
                    ?? TranslationSourceDetection.Detect(candidateList.Select(candidate => candidate.Slot.SourceText))
                : sourceOption;
        var targetLocale = reply is not null && targetOption is null ? BoundLocale(reply.RootElement, "targetLocale")
            : TranslationLanguages.Target(targetOption);
        var origin = new { source = source.Description(payload.Selection), line, byteRange };
        var draftId = Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = ReplyVersion, origin, payload.Representation, profile = request.Get("snbt-profile"), textPointer, sourceLocale, targetLocale
        })).Hex;
        if (reply is not null)
        {
            if (BoundLocale(reply.RootElement, "sourceLocale", allowUndetermined: true) != sourceLocale
                || BoundLocale(reply.RootElement, "targetLocale") != targetLocale)
                throw new CliFailure("CONFLICT", "Draft reply languages differ from the requested languages");
            var candidates = candidateList.ToDictionary(candidate => candidate.Slot.Id.Digest.Hex, StringComparer.Ordinal);
            var replies = ReadReply(reply.RootElement, draftId, candidates, sourceLocale, targetLocale);
            var edits = replies.GroupBy(reply => reply.Candidate.Leaf).Select(group =>
            {
                var text = group.Key.Text;
                var direct = group.SingleOrDefault(reply => reply.Candidate.Inner is null);
                var replacement = direct.Candidate is not null ? direct.Text : CliInput.Decode(NativeJsonEdits.ReplaceStrings(
                    NativeJsonStrings.EncodeJavaDocument(text, Limits), group.Select(reply => (reply.Candidate.Inner!, reply.Candidate.Slot.SourceText, reply.Text)), Limits));
                return new LiteralEdit(group.Key.Path, text, replacement);
            }).ToArray();
            var content = payload.Rewrite(edits);
            CheckContent(content, payload.Representation, line is not null);
            return new(new
            {
                draftId, origin, payload.Representation, sourceLocale, targetLocale,
                structureVerified = payload.Representation != "text" && unverified.Count == 0,
                unverified, gameplayVerified = false, content, edited = replies.Count
            });
        }
        return new(new
        {
            draftId, origin, payload.Representation, sourceLocale, targetLocale, payload.Context, unverified, gameplayVerified = false,
            candidates = candidateList.Select(candidate => new
            {
                slotId = candidate.Slot.Id.Digest.Hex, sourceTextHash = candidate.Slot.SourceTextHash.Hex,
                path = candidate.Leaf.Path.Segments, componentPath = candidate.Inner?.Segments, text = candidate.Slot.SourceText
            }).ToArray(),
            replyTemplate = new
            {
                schemaFamily = ReplyFamily, schemaVersion = ReplyVersion, draftId, sourceLocale, targetLocale,
                slots = candidateList.Select(candidate => new
                {
                    slotId = candidate.Slot.Id.Digest.Hex, sourceTextHash = candidate.Slot.SourceTextHash.Hex, text = (string?)null
                }).ToArray()
            }
        });
    }

    internal static void CheckExport(string output)
    {
        for (var directory = Path.GetDirectoryName(output); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            foreach (var marker in new[] { "level.dat", "level.dat_old" })
            {
                try { File.GetAttributes(Path.Combine(directory, marker)); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                throw new CliFailure("INVALID_ARGUMENTS", "Draft exports must be outside Minecraft world directories");
            }
        }
    }

    private static void CheckContent(string content, string representation, bool selectedLine)
    {
        if (selectedLine && content.AsSpan().IndexOfAny('\r', '\n') >= 0
            || representation == "command-line" && CommandDraftOperand.Find(content, Limits) is null)
            throw new CliFailure("CONFLICT", "The translation must keep the selected physical line and structured operand");
        if (representation != "text") return;
        try
        {
            if (Utf8.GetByteCount(content) > Limits.MaxDecodedBytes)
                throw new CliFailure("RESOURCE_LIMIT", "Draft text exceeds the byte budget");
        }
        catch (EncoderFallbackException)
        {
            throw new CliFailure("CONFLICT", "The translation cannot be encoded as UTF-8 text");
        }
    }

    private static DraftPayload Read(QuerySource source, CliArguments request, string format)
    {
        if (format == "auto")
        {
            var (node, path) = QueryPath.Select(source.Root!, request);
            if (node.Nbt is not { } nbt) return Json((JsonElement)node.Display()!, path, source, source.Root!);
            var document = new NbtDocument(source.RootName, nbt);
            return new("typed-nbt", path, TypedNbtJson.Write(document), NbtStrings(nbt, TypedPath.Root), edits =>
            {
                var candidate = TypedTreeEdits.Apply(document,
                    edits.Select(edit => TypedEdit.Replace(edit.Path, new NbtString(edit.Expected), new NbtString(edit.Replacement))));
                _ = BinaryNbt.MeasureDocument(candidate, Limits);
                return TypedNbtJson.Write(candidate);
            }, AncestorFont(source.Root!, path));
        }
        var bytes = source.Payload!;
        if (request.Get("byte-range") is { } range)
        {
            var parts = range.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length < 1 || start > bytes.Length || length > bytes.Length - start)
                throw new CliFailure("INVALID_ARGUMENTS", "Byte range must be start,length within the selected file or archive entry");
            bytes = bytes.AsSpan(start, length).ToArray();
        }
        if (format == "json")
        {
            using var json = BoundedJson.ReadDecoded(bytes, Limits);
            var jsonRoot = new QueryNode(json.RootElement);
            var (node, path) = QueryPath.Select(jsonRoot, request);
            return Json((JsonElement)node.Display()!, path, source, jsonRoot);
        }
        if (request.Flag("pointer") || request.Flag("segments"))
            throw new CliFailure("INVALID_ARGUMENTS", "Use a line or byte range for an SNBT operand or text");
        var text = CliInput.Decode(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsSpan(3) : bytes);
        if (request.Flag("line"))
        {
            var number = request.Number("line", 1, 1, int.MaxValue);
            using var lines = new StringReader(text);
            for (var index = 1; index < number; index++)
                if (lines.ReadLine() is null) throw new CliFailure("NOT_FOUND", "Line is absent");
            text = lines.ReadLine() ?? throw new CliFailure("NOT_FOUND", "Line is absent");
        }
        if (format == "text")
            return new("text", TypedPath.Root, text, [new(TypedPath.Root, text)], edits => edits.SingleOrDefault()?.Replacement ?? text);
        var profile = request.Get("snbt-profile") switch
        {
            null or "through-1.21.4" => SnbtProfile.JavaThrough1214, "1.21.5" => SnbtProfile.Java1215,
            "mixed-lists" => SnbtProfile.JavaMixedLists, "1.8.1" => SnbtProfile.Java181,
            _ => throw new CliFailure("INVALID_ARGUMENTS", "Unknown SNBT profile")
        };
        var sourceText = Encoding.UTF8.GetBytes(text);
        var operand = request.Flag("line") ? CommandDraftOperand.Find(text, Limits)
            ?? throw new CliFailure("UNSUPPORTED_SOURCE", "No trailing structured operand. Use --format text to draft the exact line.")
            : new SnbtSpan(0, sourceText.Length);
        SnbtDocument snbt;
        try { snbt = Snbt.ParseDocument(sourceText.AsSpan(operand.StartByte, operand.LengthBytes).ToArray(), profile, Limits); }
        catch (SnbtException error) { throw SyntaxError(error); }
        return new(request.Flag("line") ? "command-line" : "snbt", TypedPath.Root, text, SnbtStrings(snbt.Root, TypedPath.Root), edits =>
        {
            byte[] replacement;
            try
            {
                replacement = Snbt.ReplaceMany(snbt, edits.Select(edit => (edit.Path,
                    (LogicalMinecraftValue)new LogicalMinecraftLeaf(new NbtString(edit.Expected)),
                    (LogicalMinecraftValue)new LogicalMinecraftLeaf(new NbtString(edit.Replacement)))));
            }
            catch (SnbtException error) { throw SyntaxError(error); }
            return CliInput.Decode(sourceText.AsSpan(0, operand.StartByte)) + CliInput.Decode(replacement)
                + CliInput.Decode(sourceText.AsSpan(operand.EndByte));
        });
    }

    private static DraftPayload Json(JsonElement root, TypedPath path, QuerySource source, QueryNode document)
    {
        var raw = root.GetRawText();
        var bytes = Encoding.UTF8.GetBytes(raw);
        var language = TranslationLanguages.ResourceSource(source.Artifact) is not null;
        var blockedFont = !language && AncestorFont(document, path);
        var leaves = blockedFont ? [] : JsonStrings(root, TypedPath.Root, language).Take(100_001).ToArray();
        return new("json", path, raw, leaves,
            edits => CliInput.Decode(NativeJsonEdits.ReplaceStrings(bytes, edits.Select(edit => (edit.Path, edit.Expected, edit.Replacement)), Limits)), blockedFont);
    }

    private static IEnumerable<Candidate> Candidates(QuerySource source, DraftPayload payload, string? textPointer, List<TypedPath> unverified)
    {
        if (payload.BlockedFont) yield break;
        var leaves = payload.Leaves.Take(100_001).ToArray();
        if (leaves.Length > 100_000) throw new CliFailure("RESOURCE_LIMIT", "Draft selection exceeds the string budget");
        var languageFile = payload.Representation == "json" && TranslationLanguages.ResourceSource(source.Artifact) is not null;
        var componentPaths = !languageFile && payload.Representation == "json" && LooksStructured(payload.Context)
            ? ComponentStrings(source.Address(payload.Selection), payload.Context, unverified).Select(leaf => leaf.Path).ToHashSet()
            : [];
        long retained = 0;
        foreach (var leaf in leaves)
        {
            var path = new TypedPath(payload.Selection.Segments.AddRange(leaf.Path.Segments));
            var languageValue = languageFile && path.Segments.Length == 1;
            if (!languageValue && Protected(path)) continue;
            var recognized = componentPaths.Contains(leaf.Path) || languageValue;
            var nested = !recognized && payload.Representation != "text" && LooksStructured(leaf.Text)
                ? ComponentStrings(source.Address(path), leaf.Text, unverified) : null;
            if (nested is null && !recognized && payload.Representation != "text" && (textPointer is null || Pointer(leaf.Path) != textPointer)) continue;
            foreach (var part in nested ?? [leaf])
            {
                if (string.IsNullOrWhiteSpace(part.Text)
                    || !languageValue && (recognized || nested is not null) && char.IsWhiteSpace(part.Text[0]) && RomanToken(part.Text.Trim())
                    || (recognized || nested is not null) && !part.Text.EnumerateRunes().Any(Rune.IsLetter)
                    || nested is not null && Protected(part.Path)) continue;
                var inner = nested is null ? null : part.Path;
                var address = new SourceAddress(source.Artifact, path, "translation.draft", source.Dimension, source.Chunk, inner);
                retained = checked(retained + TextExtractionBudget.RecordBytes(address, part.Text.Length));
                if (retained > Limits.MaxDecodedBytes) throw new CliFailure("RESOURCE_LIMIT", "Draft text candidates exceed the byte budget");
                yield return new(new TextSlot(address, part.Text, "translation.draft", "und", "und", payload.Representation == "text", Protection), leaf, inner);
            }
        }
    }

    private static bool AncestorFont(QueryNode root, TypedPath path)
    {
        foreach (var segment in path.Segments)
        {
            if (root.HasUnprovenFont) return true;
            root = root.Child(segment);
        }
        return false;
    }

    private static bool LooksStructured(string text) => text.AsSpan().TrimStart() is ['{' or '[' or '"', ..];

    private static Leaf[] ComponentStrings(SourceAddress source, string text, List<TypedPath> unverified)
    {
        try
        {
            using var parsed = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(text, Limits), Limits);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object && (root.ValueKind != JsonValueKind.Array
                || !root.EnumerateArray().Any(node => node.ValueKind == JsonValueKind.Object))) return [];
            if (HasInvalidFont(root))
            {
                unverified.Add(source.NodePath);
                return [];
            }
            var profile = MinecraftProfiles.ForId("java-1.21")!;
            var extraction = TextComponentExtractor.EmbeddedJsonString(source, text, profile, Limits);
            if (!extraction.Complete)
            {
                if (extraction.Diagnostics.Any(item => item.Code is "InvalidComponent" or "AmbiguousComponent")) unverified.Add(source.NodePath);
                return [];
            }
            var glyphs = TextComponentExtractor.StorageJsonGlyphs(source, text, profile, Limits);
            if (glyphs is null) return [];
            return extraction.Occurrences
                .Where(item => item.Address.ComponentPath is { } path && !glyphs.ContainsKey(path) && !Protected(path))
                .Select(item => new Leaf(item.Address.ComponentPath!, item.Text)).ToArray();
        }
        catch (JsonException) { unverified.Add(source.NodePath); return []; }
    }

    private static bool HasInvalidFont(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => node.EnumerateObject().Any(field => field.NameEquals("font") && field.Value.ValueKind != JsonValueKind.String
            || HasInvalidFont(field.Value)),
        JsonValueKind.Array => node.EnumerateArray().Any(HasInvalidFont),
        _ => false
    };

    private static string Pointer(TypedPath path) => string.Concat(path.Segments.Select(segment => "/" +
        (segment.Key?.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)
            ?? segment.Index!.Value.ToString(CultureInfo.InvariantCulture))));

    private static IEnumerable<Leaf> JsonStrings(JsonElement node, TypedPath path, bool language)
    {
        if (node.ValueKind == JsonValueKind.String) yield return new(path, NativeJsonStrings.Read(node));
        if (node.ValueKind == JsonValueKind.Object && (language || !new QueryNode(node).HasUnprovenFont))
            foreach (var member in node.EnumerateObject())
                foreach (var leaf in JsonStrings(member.Value, QueryPath.Append(path, PathSegment.ForKey(NativeJsonStrings.PropertyName(member))), language))
                    yield return leaf;
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in node.EnumerateArray())
                foreach (var leaf in JsonStrings(child, QueryPath.Append(path, PathSegment.ForIndex(index++)), language))
                    yield return leaf;
        }
    }

    private static IEnumerable<Leaf> NbtStrings(NbtValue node, TypedPath path)
    {
        if (node is NbtString text) yield return new(path, text.Value);
        if (node is NbtCompound compound && !new QueryNode(node).HasUnprovenFont)
            foreach (var group in compound.Entries.GroupBy(entry => entry.Name, StringComparer.Ordinal))
                if (group.Count() == 1)
                    foreach (var leaf in NbtStrings(group.Single().Value, QueryPath.Append(path, PathSegment.ForKey(group.Key)))) yield return leaf;
        if (node is NbtList list)
            for (var index = 0; index < list.Items.Length; index++)
                foreach (var leaf in NbtStrings(list.Items[index], QueryPath.Append(path, PathSegment.ForIndex(index)))) yield return leaf;
    }

    private static IEnumerable<Leaf> SnbtStrings(SnbtNode node, TypedPath path)
    {
        if (node.Value is LogicalMinecraftLeaf leaf)
        {
            if (leaf.Value is NbtString text) yield return new(path, text.Value);
            yield break;
        }
        if (node.Children.Any(child => child.Selector.Key == "font" && child.Node.Value is not LogicalMinecraftLeaf { Value: NbtString { Value: "minecraft:default" } }))
            yield break;
        foreach (var group in node.Children.GroupBy(child => child.Selector))
            if (group.Count() == 1)
                foreach (var descendant in SnbtStrings(group.Single().Node, QueryPath.Append(path, group.Key))) yield return descendant;
    }

    private static bool Protected(TypedPath path) => !path.Segments.IsEmpty
        && (path.Segments[^1].Key is "id" or "UUID" or "uuid" or "type" or "color" or "font" or "translate" or "keybind"
            or "selector" or "nbt" or "storage" or "block" or "entity" or "command" or "Command" or "action"
            || path.Segments.Any(segment => segment.Key is "score" or "clickEvent" or "click_event"));

    private static CliFailure SyntaxError(SnbtException error)
        => new(error.Code == SnbtError.LimitExceeded ? "RESOURCE_LIMIT" : "MALFORMED_SOURCE", error.Message);

    private static void ValidateReplyDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6
            || Field(root, "schemaFamily") != ReplyFamily || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != ReplyVersion
            || !root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
            throw new CliFailure("INVALID_ARGUMENTS", "Expected the current draft reply schema. Export a new draft from the original selection.");
    }

    private static string BoundLocale(JsonElement root, string key, bool allowUndetermined = false)
    {
        var value = Field(root, key);
        if (value == TranslationLanguages.Undetermined && allowUndetermined) return value;
        if (value is null || TranslationLanguages.Concrete(value) != value)
            throw new CliFailure("INVALID_ARGUMENTS", "Draft reply has an invalid " + key);
        return value;
    }

    private static List<(Candidate Candidate, string Text)> ReadReply(JsonElement root, string draftId,
        Dictionary<string, Candidate> candidates, string sourceLocale, string targetLocale)
    {
        if (Field(root, "draftId") != draftId) throw new CliFailure("CONFLICT", "Draft input or selection changed");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var replies = new List<(Candidate, string)>();
        foreach (var row in root.GetProperty("slots").EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 3 || !row.TryGetProperty("text", out var text))
                throw new CliFailure("INVALID_ARGUMENTS", "Expected slotId, sourceTextHash and text");
            var id = Field(row, "slotId");
            if (id is null || !seen.Add(id) || !candidates.TryGetValue(id, out var candidate)
                || Field(row, "sourceTextHash") != candidate.Slot.SourceTextHash.Hex)
                throw new CliFailure("CONFLICT", "Draft reply includes a stale, unknown or duplicate slot");
            if (text.ValueKind == JsonValueKind.Null) continue;
            if (text.ValueKind != JsonValueKind.String) throw new CliFailure("INVALID_ARGUMENTS", "Draft text must be a string or null");
            var replacement = NativeJsonStrings.Read(text);
            var draft = candidate.Slot;
            var slot = new TextSlot(draft.Address, draft.SourceText, draft.Context, sourceLocale, targetLocale, draft.AllowedEmpty, draft.Placeholders);
            var validation = slot.Validate(new(slot.Id, slot.SourceTextHash, targetLocale, replacement));
            if (validation != SlotValidation.Valid) throw new CliFailure("CONFLICT", validation.ToString());
            if (TrailingLevel(draft.SourceText) != TrailingLevel(replacement))
                throw new CliFailure("CONFLICT", "The draft must preserve the Roman level suffix");
            replies.Add((candidate, replacement));
        }
        return replies;
    }

    private static string? TrailingLevel(string text)
    {
        var trimmed = text.TrimEnd();
        var boundary = trimmed.LastIndexOf(' ');
        if (boundary < 1) return null;
        var word = trimmed[(boundary + 1)..];
        return word.Length >= 2 && RomanToken(word) ? word : null;
    }

    private static bool RomanToken(string word) => word.Length is >= 1 and <= 15
        && word.All(unit => unit is 'I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M');

    private static string? Field(JsonElement record, string key)
        => record.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(field) : null;

    private sealed record Leaf(TypedPath Path, string Text);
    private sealed record LiteralEdit(TypedPath Path, string Expected, string Replacement);
    private sealed record Candidate(TextSlot Slot, Leaf Leaf, TypedPath? Inner);
    private sealed record DraftPayload(string Representation, TypedPath Selection, string Context, IEnumerable<Leaf> Leaves,
        Func<LiteralEdit[], string> Rewrite, bool BlockedFont = false);
}

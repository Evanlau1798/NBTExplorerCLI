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
    private static readonly DecodeLimits Limits = DecodeLimits.Default;
    private static readonly PlaceholderPolicy Protection = new([], protectVisibleText: true, protectPrivateUseGlyphs: true);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static CliResult Execute(CliArguments request)
    {
        var line = request.Get("line");
        var byteRange = request.Get("byte-range");
        var format = request.Get("format") ?? (line is not null || byteRange is not null ? "snbt" : "auto");
        if (format is not ("auto" or "json" or "snbt" or "text"))
            throw new CliFailure("INVALID_ARGUMENTS", "Choose auto, json, snbt or text for --format");
        if (line is not null && (byteRange is not null || format is not ("snbt" or "text"))
            || byteRange is not null && format == "auto" || request.Flag("snbt-profile") && format != "snbt")
            throw new CliFailure("INVALID_ARGUMENTS", "Choose one compatible line or byte-range selection and format");
        if (request.Get("output") is { } output)
        {
            var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Input!));
            if (Directory.Exists(input)) ScanCommands.OutsideWorld(input, output);
        }
        using var source = new QuerySource(request, readPayload: format != "auto");
        var payload = Read(source, request, format);
        var origin = new { source = source.Description(payload.Selection), line, byteRange };
        var draftId = Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, origin, payload.Representation, profile = request.Get("snbt-profile")
        })).Hex;
        var candidates = Candidates(source, payload).ToDictionary(candidate => candidate.Slot.Id.Digest.Hex, StringComparer.Ordinal);
        if (request.Get("reply") is { } file)
        {
            var replies = ReadReply(file, draftId, candidates);
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
                draftId, origin, payload.Representation, structureVerified = payload.Representation != "text",
                gameplayVerified = false, content, edited = replies.Count
            });
        }
        return new(new
        {
            draftId, origin, payload.Representation, payload.Context, gameplayVerified = false,
            candidates = candidates.Values.Select(candidate => new
            {
                slotId = candidate.Slot.Id.Digest.Hex, sourceTextHash = candidate.Slot.SourceTextHash.Hex,
                path = candidate.Leaf.Path.Segments, componentPath = candidate.Inner?.Segments, text = candidate.Slot.SourceText
            }).ToArray(),
            replyTemplate = new
            {
                schemaFamily = ReplyFamily, schemaVersion = 1, draftId,
                slots = candidates.Values.Select(candidate => new
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
            if (node.Nbt is not { } nbt) return Json((JsonElement)node.Display()!, path);
            var document = new NbtDocument(source.RootName, nbt);
            return new("typed-nbt", path, TypedNbtJson.Write(document), NbtStrings(nbt, TypedPath.Root), edits =>
            {
                var candidate = TypedTreeEdits.Apply(document,
                    edits.Select(edit => TypedEdit.Replace(edit.Path, new NbtString(edit.Expected), new NbtString(edit.Replacement))));
                _ = BinaryNbt.MeasureDocument(candidate, Limits);
                return TypedNbtJson.Write(candidate);
            });
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
            var (node, path) = QueryPath.Select(new QueryNode(json.RootElement), request);
            return Json((JsonElement)node.Display()!, path);
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

    private static DraftPayload Json(JsonElement root, TypedPath path)
    {
        var raw = root.GetRawText();
        var bytes = Encoding.UTF8.GetBytes(raw);
        return new("json", path, raw, JsonStrings(bytes),
            edits => CliInput.Decode(NativeJsonEdits.ReplaceStrings(bytes, edits.Select(edit => (edit.Path, edit.Expected, edit.Replacement)), Limits)));
    }

    private static IEnumerable<Candidate> Candidates(QuerySource source, DraftPayload payload)
    {
        long retained = 0;
        foreach (var leaf in payload.Leaves)
        {
            var path = new TypedPath(payload.Selection.Segments.AddRange(leaf.Path.Segments));
            if (Protected(path)) continue;
            var nested = payload.Representation != "text" ? EmbeddedJsonStrings(leaf.Text) : null;
            foreach (var part in nested ?? [leaf])
            {
                if (string.IsNullOrWhiteSpace(part.Text) || nested is not null && Protected(part.Path)) continue;
                var inner = nested is null ? null : part.Path;
                var address = new SourceAddress(source.Artifact, path, "translation.draft", source.Dimension, source.Chunk, inner);
                retained = checked(retained + TextExtractionBudget.RecordBytes(address, part.Text.Length));
                if (retained > Limits.MaxDecodedBytes) throw new CliFailure("RESOURCE_LIMIT", "Draft text candidates exceed the byte budget");
                yield return new(new TextSlot(address, part.Text, "translation.draft", "und", "und", true, Protection), leaf, inner);
            }
        }
    }

    private static Leaf[]? EmbeddedJsonStrings(string text)
    {
        var content = text.AsSpan().TrimStart();
        if (content.IsEmpty || content[0] is not ('{' or '[' or '"')) return null;
        try { return JsonStrings(NativeJsonStrings.EncodeJavaDocument(text, Limits)).ToArray(); }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<Leaf> JsonStrings(byte[] bytes)
    {
        foreach (var (path, span) in NativeJsonStringSpans.Read(bytes, Limits))
            yield return new(path, JsonString(bytes, span));
    }

    private static string JsonString(byte[] bytes, SnbtSpan span)
    {
        var reader = new Utf8JsonReader(bytes.AsSpan(span.StartByte, span.LengthBytes));
        _ = reader.Read();
        return NativeJsonStrings.ReadToken(ref reader);
    }

    private static IEnumerable<Leaf> NbtStrings(NbtValue node, TypedPath path)
    {
        if (node is NbtString text) yield return new(path, text.Value);
        if (node is NbtCompound compound)
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

    private static List<(Candidate Candidate, string Text)> ReadReply(string file, string draftId, Dictionary<string, Candidate> candidates)
    {
        using var data = BoundedJson.ReadDecoded(CliInput.ReadBytes(file), Limits);
        var root = data.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4
            || Field(root, "schemaFamily") != ReplyFamily || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
            || !root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
            throw new CliFailure("INVALID_ARGUMENTS", "Expected the exact draft reply schema");
        if (Field(root, "draftId") != draftId) throw new CliFailure("CONFLICT", "Draft input or selection changed");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var replies = new List<(Candidate, string)>();
        foreach (var row in slots.EnumerateArray())
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
            var validation = candidate.Slot.Validate(new(candidate.Slot.Id, candidate.Slot.SourceTextHash, "und", replacement));
            if (validation != SlotValidation.Valid) throw new CliFailure("CONFLICT", validation.ToString());
            replies.Add((candidate, replacement));
        }
        return replies;
    }

    private static string? Field(JsonElement record, string key)
        => record.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? NativeJsonStrings.Read(field) : null;

    private sealed record Leaf(TypedPath Path, string Text);
    private sealed record LiteralEdit(TypedPath Path, string Expected, string Replacement);
    private sealed record Candidate(TextSlot Slot, Leaf Leaf, TypedPath? Inner);
    private sealed record DraftPayload(string Representation, TypedPath Selection, string Context, IEnumerable<Leaf> Leaves, Func<LiteralEdit[], string> Rewrite);
}

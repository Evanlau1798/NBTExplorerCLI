using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NBTExplorerCLI.Cli;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.CliScanTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class CliFallbackTests
{
    internal static void RespectsTextEncodingAndExportBoundaries()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-boundaries-").FullName;
        try
        {
            var source = Path.Combine(work, "source.mcfunction");
            var reply = Path.Combine(work, "reply.json");
            File.WriteAllText(source, "data modify storage example:state message set value 'Welcome'");
            var line = new[] { "translate", "fallback", source, "--line", "1" };
            const string multiline = "第一行\r\n第二行";
            var rejectedLine = Submit([.. line, "--text-pointer="], multiline);
            var rejectedSelectedText = Submit([.. line, "--format", "text"], multiline);
            var rejectedEncoding = Submit(["translate", "fallback", source, "--format", "text"], "SURROGATE_PROBE");

            string VerifiedCommand(string[] arguments)
            {
                using var applied = Success(Submit(arguments, multiline));
                var data = applied.RootElement.GetProperty("data");
                var content = data.GetProperty("content").GetString()!;
                Require(data.GetProperty("structureVerified").GetBoolean() && !content.Contains('\r') && !content.Contains('\n'),
                    "an escaped replacement remains one physical command line");
                return content[(content.LastIndexOf(" value ", StringComparison.Ordinal) + " value ".Length)..];
            }
            var modern = Snbt.ParseDocument(Encoding.UTF8.GetBytes(VerifiedCommand([.. line, "--text-pointer=", "--snbt-profile", "1.21.5"])), SnbtProfile.Java1215);
            Require(modern.Root.Value is LogicalMinecraftLeaf { Value: NbtString modernText } && modernText.Value == multiline,
                "the modern SNBT parser reads the full escaped text");
            File.WriteAllText(source, """data modify storage example:state message set value '{"text":"Welcome"}'""");
            var embedded = Snbt.ParseDocument(Encoding.UTF8.GetBytes(VerifiedCommand(line)));
            Require(embedded.Root.Value is LogicalMinecraftLeaf { Value: NbtString embeddedText }
                && JsonNode.Parse(embeddedText.Value)!["text"]!.GetValue<string>() == multiline, "embedded JSON retains escaped multiline text");
            using var plain = Success(Submit(["translate", "fallback", source, "--format", "text"], multiline));
            Require(plain.RootElement.GetProperty("data").GetProperty("content").GetString() == multiline,
                "whole plain-text drafts can contain multiple lines");

            var world = Directory.CreateDirectory(Path.Combine(work, "world")).FullName;
            var folder = Directory.CreateDirectory(Path.Combine(world, "data")).FullName;
            var notes = Path.Combine(folder, "notes.json");
            File.WriteAllText(notes, """{"title":"Welcome"}""");
            var inner = Path.Combine(folder, "draft.json");
            var outer = Path.Combine(Directory.CreateDirectory(Path.Combine(work, "world-drafts")).FullName, "draft.json");
            var export = new[] { "translate", "fallback", world, "--path", "data/notes.json", "--output" };
            var rejectedExport = Run(work, [.. export, inner]);
            using var acceptedExport = Success(Run(work, [.. export, outer]));
            Require(File.Exists(outer), "a sibling directory that shares the world name prefix accepts draft exports");
            var marker = Path.Combine(world, "level.dat");
            File.WriteAllText(marker, "unreadable world metadata");
            var fileExport = new[] { "translate", "fallback", notes, "--output" };
            var rejectedWorldFile = Run(work, [.. fileExport, inner]);
            using var acceptedWorldFile = Success(Run(work, [.. fileExport, Path.Combine(work, "outside.json")]));
            File.Move(marker, marker + "_old");
            var backupDraft = Path.Combine(folder, "backup-draft.json");
            var rejectedBackupWorld = Run(work, [.. fileExport, backupDraft]);
            Require(rejectedWorldFile.Exit == 2 && rejectedBackupWorld.Exit == 2 && !File.Exists(inner) && !File.Exists(backupDraft),
                $"standalone input must not export into a world with live or backup metadata: {rejectedWorldFile.Exit}, {rejectedBackupWorld.Exit}");
            Require(rejectedLine.Exit == 3 && rejectedSelectedText.Exit == 3 && rejectedEncoding.Exit == 3
                && rejectedExport.Exit == 2 && !File.Exists(inner),
                $"draft guards reject raw command and text line breaks, invalid UTF-8 and world-local output: " +
                $"{rejectedLine.Exit}, {rejectedSelectedText.Exit}, {rejectedEncoding.Exit}, {rejectedExport.Exit}; world file exists={File.Exists(inner)}");

            var lateExports = new List<string>();
            foreach (var phase in new[] { "storage.read", "candidate.verify" })
            {
                var destination = Directory.CreateDirectory(Path.Combine(work, phase)).FullName;
                var exportPath = Path.Combine(destination, "draft.json");
                var lateMarker = Path.Combine(destination, "level.dat");
                using var listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == WorkDiagnostics.SourceName,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStopped = activity =>
                    {
                        if (activity.OperationName == phase) File.WriteAllText(lateMarker, "world marker appeared during export");
                    }
                };
                ActivitySource.AddActivityListener(listener);
                using var stdout = new StringWriter();
                var exit = CliApplication.Run([.. fileExport, exportPath], stdout);
                if (exit != 2 || !Directory.EnumerateFileSystemEntries(destination).SequenceEqual(new[] { lateMarker }))
                    lateExports.Add($"{phase} exit={exit}");
            }
            Require(lateExports.Count == 0, "a newly introduced world marker must survive while the draft and any candidate are absent: " + string.Join(", ", lateExports));

            Invocation Submit(string[] arguments, string text)
            {
                using var preview = Success(Run(work, arguments));
                var template = JsonNode.Parse(preview.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText())!;
                template["slots"]![0]!["text"] = text;
                File.WriteAllText(reply, template.ToJsonString().Replace("SURROGATE_PROBE", @"\uD800", StringComparison.Ordinal));
                return Run(work, [.. arguments, "--reply", reply]);
            }
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static void DraftLanguagesStayBoundOnSubmission()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-").FullName;
        try
        {
            var input = Path.Combine(work, "dialogue.txt");
            var reply = Path.Combine(work, "reply.json");
            File.WriteAllText(input, "Ce village est entouré de montagnes. Les habitants préparent le marché depuis ce matin.");
            var args = new[] { "translate", "fallback", input, "--format", "text" };
            using var implicitDraft = Success(Run(work, args));
            var implicitData = implicitDraft.RootElement.GetProperty("data");
            Require(implicitData.GetProperty("sourceLocale").GetString() == "fr"
                && implicitData.GetProperty("targetLocale").GetString() is { Length: > 1 } target && target != "und",
                "fallback resolves the source text and system display language without either flag");
            using var explicitDraft = Success(Run(work, [.. args, "--target-language", "ja-JP"]));
            var template = JsonNode.Parse(explicitDraft.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText())!;
            Require(template["schemaVersion"]!.GetValue<int>() == 2 && template["sourceLocale"]!.GetValue<string>() == "fr"
                && template["targetLocale"]!.GetValue<string>() == "ja_jp", "the versioned template carries the bound source and target");
            File.WriteAllText(reply, template.ToJsonString());
            using var resumed = Success(Run(work, [.. args, "--reply", reply]));
            Require(resumed.RootElement.GetProperty("data").GetProperty("targetLocale").GetString() == "ja_jp",
                "the reply continues with its bound target without reselecting the display language");
            Require(Run(work, [.. args, "--target-language", "de-DE", "--reply", reply]).Exit == 3,
                "an explicitly mismatched target conflicts");
            template["targetLocale"] = "de_de";
            File.WriteAllText(reply, template.ToJsonString());
            Require(Run(work, [.. args, "--reply", reply]).Exit == 3, "the draft hash covers the language pair");
            var languages = Directory.CreateDirectory(Path.Combine(work, "assets", "sample", "lang"));
            File.WriteAllText(Path.Combine(languages.FullName, "de_de.json"), """{"font":"Bienvenue","type":"Bonjour","firstPerson":"I","kitchen":"MIX"}""");
            using var marked = Success(Run(work, ["translate", "fallback", work, "--path", "assets/sample/lang/de_de.json"]));
            var markedData = marked.RootElement.GetProperty("data");
            Require(markedData.GetProperty("sourceLocale").GetString() == "de_de" && markedData.GetProperty("candidates").GetArrayLength() == 4,
                "the selected language resource provides its locale and treats its arbitrary keys as language entries");
            using var markedKey = Success(Run(work, "translate", "fallback", work, "--path", "assets/sample/lang/de_de.json", "--pointer", "/type"));
            Require(markedKey.RootElement.GetProperty("data").GetProperty("candidates")[0].GetProperty("text").GetString() == "Bonjour",
                "a language entry named font does not style the other entries when a single key is selected");

            var mixed = Path.Combine(work, "mixed.json");
            var french = new[] { "chat", "chien", "maison", "école", "forêt", "moulin", "château", "puits", "pont", "marché", "jardin", "bateau" };
            File.WriteAllText(mixed, JsonSerializer.Serialize(french.Select(noun => "Les habitants de ce village ont découvert un " + noun + " ce matin.")
                .Append("Das große Haus liegt neben der Kirche und es ist seit vielen Jahren geschlossen.")
                .Select(text => new { text })));
            using var ambiguous = Success(Run(work, "translate", "fallback", mixed));
            Require(ambiguous.RootElement.GetProperty("data").GetProperty("sourceLocale").GetString() == "und",
                "a finite recognition budget cannot turn a multilingual selection into one guessed source language");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static void DraftKeepsSplitLevelsAndAncestorFonts()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-").FullName;
        try
        {
            var input = Path.Combine(work, "text.nbt");
            var document = new NbtDocument("", new NbtCompound([
                new("description", new NbtString("""[{"text":"Bonus"},{"text":" I"}]""")),
                new("caption", new NbtString("""{"text":"MIX"}"""))]));
            File.WriteAllBytes(input, BinaryNbt.WriteDocument(document));
            using var level = Success(Run(work, "translate", "fallback", input));
            Require(level.RootElement.GetProperty("data").GetProperty("candidates").EnumerateArray()
                    .Select(value => value.GetProperty("text").GetString()).SequenceEqual(new[] { "Bonus", "MIX" }),
                "a separated level suffix is protected while ordinary letter-only captions remain editable");

            document = new("", new NbtCompound([new("description", new NbtString(
                """["",{"text":"Recover items"},{"text":"A","font":"test:meter"}]"""))]));
            File.WriteAllBytes(input, BinaryNbt.WriteDocument(document));
            using var array = Success(Run(work, "translate", "fallback", input));
            Require(array.RootElement.GetProperty("data").GetProperty("candidates").EnumerateArray()
                    .Select(value => value.GetProperty("text").GetString()).SequenceEqual(new[] { "Recover items" }),
                "a leading empty component preserves the remaining compatible text objects");

            input = Path.Combine(work, "pane.json");
            File.WriteAllText(input, """{"pane":{"text":"Icon A","font":"test:meter"}}""");
            foreach (var format in new[] { "auto", "json" })
                Require(Run(work, "translate", "fallback", input, "--format", format, "--pointer", "/pane/text", "--text-pointer=").Exit == 4,
                    "selecting a precise leaf cannot bypass its unproven ancestor font");

            File.WriteAllText(input, """{"pane":{"text":"Icon A","font":{"resource":"test:meter"}}}""");
            Require(Run(work, "translate", "fallback", input, "--text-pointer", "/pane/text").Exit == 4,
                "confirmation of a relative JSON path cannot bypass a non-string font field");
            File.WriteAllText(input, """{pane:{text:"Icon A",font:{resource:"test:meter"}}}""");
            Require(Run(work, "translate", "fallback", input, "--format", "snbt", "--text-pointer", "/pane/text").Exit == 4,
                "the same font guard applies to a relative SNBT path");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static void DraftDoesNotTreatUnprovenOrMalformedStringsAsVisibleText()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-").FullName;
        try
        {
            var input = Path.Combine(work, "dialogue.nbt");
            var reply = Path.Combine(work, "reply.json");
            var before = new NbtDocument("", new NbtCompound([
                new("broken", new NbtString("""{"text":"Broken}""")),
                new("duplicate", new NbtString("""{"text":"One","text":"Two"}""")),
                new("message", new NbtString("""{"text":"Welcome","extra":[{"text":"A","font":"test:meter"},{"text":"Friend"}]}""")),
                new("callback", new NbtString("open_screen")),
                new("caption", new NbtString("Title XX"))]));
            File.WriteAllBytes(input, BinaryNbt.WriteDocument(before));
            var args = new[] { "translate", "fallback", input };
            using var draft = Success(Run(work, args));
            var data = draft.RootElement.GetProperty("data");
            Require(data.GetProperty("candidates").EnumerateArray().Select(item => item.GetProperty("text").GetString())
                .SequenceEqual(new[] { "Welcome", "Friend" }), "only established presentation strings are editable; bad layers and unknown font text stay in context");
            var template = JsonNode.Parse(data.GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = "Bienvenue";
            File.WriteAllText(reply, template.ToJsonString());
            using var applied = Success(Run(work, [.. args, "--reply", reply]));
            var result = applied.RootElement.GetProperty("data");
            Require(!result.GetProperty("structureVerified").GetBoolean(), "report cannot mark known malformed inner structures as verified");
            var translated = TypedNbtJson.Read(result.GetProperty("content").GetString()!);
            Require(translated == new NbtDocument("", new NbtCompound(((NbtCompound)before.Root).Entries.Select(item =>
                item.Name == "message" ? new NbtEntry(item.Name, new NbtString(((NbtString)item.Value).Value.Replace("Welcome", "Bienvenue", StringComparison.Ordinal))) : item))),
                "only the verified sibling slot changes");
            string[] selected = [.. args, "--text-pointer", "/caption"];
            using var title = Success(Run(work, selected));
            var titleTemplate = JsonNode.Parse(title.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText())!;
            titleTemplate["slots"]![0]!["text"] = "Titre XXI";
            File.WriteAllText(reply, titleTemplate.ToJsonString());
            Require(Run(work, [.. selected, "--reply", reply]).Exit == 3, "a trailing Roman level cannot be changed");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    internal static void DraftsKeepStructureAndSourceBinding()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-").FullName;
        try
        {
            var input = Path.Combine(work, "custom.config");
            const string json = """{"id":"example:item","threshold":1e2,"panel":{"color":"red","caption":"−2% power"}}""";
            File.WriteAllText(input, json);
            var reply = Path.Combine(work, "reply.json");
            var args = new[] { "translate", "fallback", input, "--format", "json", "--text-pointer", "/panel/caption" };
            using var draft = Success(Run(work, args));
            var data = draft.RootElement.GetProperty("data");
            Require(data.GetProperty("context").GetString() == json && !data.GetProperty("gameplayVerified").GetBoolean()
                && data.GetProperty("candidates").GetArrayLength() == 1, "fallback exports the whole tag with only the possible caption as an editable draft slot");
            var template = JsonNode.Parse(data.GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = "2% 能量";
            File.WriteAllText(reply, template.ToJsonString());
            Require(Run(work, [.. args, "--reply", reply]).Exit == 3, "fallback protects the signed percentage");
            template["slots"]![0]!["text"] = "−2% 能量";
            File.WriteAllText(reply, template.ToJsonString());
            using var valid = Success(Run(work, [.. args, "--reply", reply]));
            var expected = json.Replace("−2% power", "−2% 能量", StringComparison.Ordinal);
            Require(valid.RootElement.GetProperty("data").GetProperty("content").GetString() == expected
                && File.ReadAllText(input) == json, "validated JSON draft preserves unchanged lexemes and the original source");
            Require(Run(work, [.. args, "--apply"]).Exit == 2, "drafts have no world-write switch");
            var id = template["slots"]![0]!["slotId"]!.GetValue<string>();
            template["slots"]![0]!["slotId"] = new string('0', 64);
            File.WriteAllText(reply, template.ToJsonString());
            Require(Run(work, [.. args, "--reply", reply]).Exit == 3, "undeclared edits fail");
            template["slots"]![0]!["slotId"] = id;
            File.WriteAllText(reply, template.ToJsonString());
            File.AppendAllText(input, "\n");
            Require(Run(work, [.. args, "--reply", reply]).Exit == 3, "reply is bound to the original source bytes");

            input = Path.Combine(work, "run.mcfunction");
            const string command = """execute if score a b matches 1 run data modify storage example:state lines append value '{"text":"Power +3%","font":"minecraft:default"}'""";
            File.WriteAllText(input, "# header\r\n" + command + "\r\n");
            args = ["translate", "fallback", input, "--line", "2"];
            using var commandDraft = Success(Run(work, args));
            data = commandDraft.RootElement.GetProperty("data");
            Require(data.GetProperty("context").GetString() == command && data.GetProperty("candidates").GetArrayLength() == 1,
                "a complex command keeps its whole line as context and exposes the embedded component text");
            template = JsonNode.Parse(data.GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = "能量 +3%";
            File.WriteAllText(reply, template.ToJsonString());
            using var commandResult = Success(Run(work, [.. args, "--reply", reply]));
            var content = commandResult.RootElement.GetProperty("data").GetProperty("content").GetString()!;
            var prefix = command[..command.LastIndexOf(" value ", StringComparison.Ordinal)] + " value ";
            var parsed = Snbt.ParseDocument(Encoding.UTF8.GetBytes(content[prefix.Length..])).Root.Value;
            Require(content.StartsWith(prefix, StringComparison.Ordinal) && parsed is LogicalMinecraftLeaf { Value: NbtString inner }
                && inner.Value == """{"text":"能量 +3%","font":"minecraft:default"}"""
                && File.ReadAllText(input) == "# header\r\n" + command + "\r\n", "command operands and component metadata are invariant");

            input = Path.Combine(work, "custom.nbt");
            var original = new NbtDocument("native", new NbtCompound([new("caption", new NbtString("Readable prose")), new("hint", new NbtByte(7))]));
            var bytes = BinaryNbt.WriteDocument(original);
            File.WriteAllBytes(input, bytes);
            args = ["translate", "fallback", input, "--text-pointer", "/caption"];
            using var nbtDraft = Success(Run(work, args));
            template = JsonNode.Parse(nbtDraft.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = "可閱讀的文字";
            File.WriteAllText(reply, template.ToJsonString());
            using var nbtResult = Success(Run(work, [.. args, "--reply", reply]));
            var document = TypedNbtJson.Read(nbtResult.RootElement.GetProperty("data").GetProperty("content").GetString()!);
            Require(document == new NbtDocument("native", new NbtCompound([new("caption", new NbtString("可閱讀的文字")), new("hint", new NbtByte(7))]))
                && File.ReadAllBytes(input).SequenceEqual(bytes), "NBT fallback exports an exact typed draft while retaining the original binary input");

            input = Path.Combine(work, "custom-script");
            const string unusual = "背景|{caption:'Read this',count:7b}|next";
            File.WriteAllText(input, unusual);
            const string field = "{caption:'Read this',count:7b}";
            var start = Encoding.UTF8.GetByteCount("背景|");
            args = ["translate", "fallback", input, "--byte-range", $"{start},{Encoding.UTF8.GetByteCount(field)}", "--text-pointer", "/caption"];
            using var tag = Success(Run(work, args));
            data = tag.RootElement.GetProperty("data");
            Require(data.GetProperty("context").GetString() == field && data.GetProperty("candidates").GetArrayLength() == 1,
                "an exact UTF-8 byte range can select a tag anywhere in an unfamiliar input");
            template = JsonNode.Parse(data.GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = "請閱讀";
            File.WriteAllText(reply, template.ToJsonString());
            using var tagResult = Success(Run(work, [.. args, "--reply", reply]));
            var changed = Encoding.UTF8.GetBytes(tagResult.RootElement.GetProperty("data").GetProperty("content").GetString()!);
            var count = Snbt.ParseDocument(changed).Resolve(new([PathSegment.ForKey("count")])).Value;
            Require(count is LogicalMinecraftLeaf { Value: NbtByte { Value: 7 } }
                && File.ReadAllText(input) == unusual, "byte-range drafts retain numeric suffixes and protect the containing source");
            Require(Run(work, ["translate", "fallback", input, "--byte-range", "1,3", "--format", "text"]).Exit == 5,
                "a draft range must not cut a UTF-8 character");

            args = ["translate", "fallback", input, "--format", "text"];
            using var plain = Success(Run(work, args));
            data = plain.RootElement.GetProperty("data");
            Require(data.GetProperty("context").GetString() == unusual && data.GetProperty("candidates").GetArrayLength() == 1,
                "an opaque UTF-8 source can still be drafted as selected text");
            template = JsonNode.Parse(data.GetProperty("replyTemplate").GetRawText())!;
            template["slots"]![0]!["text"] = unusual.Replace("Read this", "請閱讀", StringComparison.Ordinal);
            File.WriteAllText(reply, template.ToJsonString());
            using var textResult = Success(Run(work, [.. args, "--reply", reply]));
            Require(!textResult.RootElement.GetProperty("data").GetProperty("structureVerified").GetBoolean()
                && File.ReadAllText(input) == unusual, "plain-text drafts declare their verification boundary");
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}

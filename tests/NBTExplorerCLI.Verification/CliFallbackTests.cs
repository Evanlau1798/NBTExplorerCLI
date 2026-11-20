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
            var rejectedLine = Submit(line, multiline);
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
            var modern = Snbt.ParseDocument(Encoding.UTF8.GetBytes(VerifiedCommand([.. line, "--snbt-profile", "1.21.5"])), SnbtProfile.Java1215);
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

    internal static void DraftsKeepStructureAndSourceBinding()
    {
        var work = Directory.CreateTempSubdirectory("nbtcli-fallback-").FullName;
        try
        {
            var input = Path.Combine(work, "custom.config");
            const string json = """{"id":"example:item","threshold":1e2,"panel":{"color":"red","caption":"−2% power"}}""";
            File.WriteAllText(input, json);
            var reply = Path.Combine(work, "reply.json");
            var args = new[] { "translate", "fallback", input, "--format", "json" };
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
            args = ["translate", "fallback", input];
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
            args = ["translate", "fallback", input, "--byte-range", $"{start},{Encoding.UTF8.GetByteCount(field)}"];
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

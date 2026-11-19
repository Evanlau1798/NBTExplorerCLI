using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class CliResourceTests
{
    private static Invocation Call(string world, string work, Guid id, long revision, string action, params string[] args)
        => Run(work, ["translate", action, world, "--session-id", id.ToString(), "--revision", revision.ToString(), "--state-root", Path.Combine(work, "state"), .. args]);
    public static void ExportLanguagesPreviewsCanonicalPlansAndUsesThePublicApplyRecoveryPath()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var output = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = CliKeyPlanTests.Session(world, work); var original = Directory.GetFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var file = Path.Combine(work, "language-plan.json");
            using var preview = Success(Call(world, work, id, revision, "export-lang", "--output", output, "--plan-output", file, "--prefix", "test."));
            var plan = PatchPlanJson.Read(File.ReadAllText(file));
            Require(plan.Operations.Single() is ResourcePatch { Generation.Format: ResourceOutputFormat.LanguageFiles }
                && plan.Outputs.Length == 2 && !Directory.GetFiles(output).Any(), "resource preview exports one canonical plan without interpreting --output DIR as a JSON export filename");
            using var applied = Success(Run(work, "apply", output, "--plan", file, "--world-offline", "--state-root", Path.Combine(work, "state")));
            var state = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(work, "state", "sessions"))).Read(id);
            foreach (var locale in new[] { "en_us", "zh_tw" })
            {
                using var language = BoundedJson.ReadDecoded(File.ReadAllBytes(Path.Combine(output, locale + ".json")), DecodeLimits.Default);
                Require(language.RootElement.EnumerateObject().Count() == state.Slots.Length, "both original and translated locales have every declared key");
                foreach (var slot in state.Slots)
                    Require(NativeJsonStrings.Read(language.RootElement.GetProperty("test." + slot.Slot.Id.Digest.Hex)) == (locale == "en_us" ? slot.Slot.SourceText : slot.Reply!.Text), "language keys exactly match session key-plan slot identity and decoded values");
            }
            Require(state.Revision == revision && state.Slots.All(slot => slot.State == SessionSlotState.Translated)
                && original.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)), "resource output does not mutate gameplay or seal accepted session replies");
            using var status = Success(Run(work, "status", output, "--plan-hash", plan.PlanHash.Hex, "--state-root", Path.Combine(work, "state")));
            var rolledBack = Run(work, "recover", output, "--plan-hash", plan.PlanHash.Hex, "--rollback", "--world-offline", "--state-root", Path.Combine(work, "state"));
            Require(rolledBack.Exit == 8 && !File.Exists(Path.Combine(output, "en_us.json")) && !File.Exists(Path.Combine(output, "zh_tw.json")), "public recovery restores the original resource absence with rollback accounting");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); Directory.Delete(output, true); }
    }
    public static void PackCommandsFreezeMergeInputsAndKeepOutputNamesAndDirectoriesDistinct()
    {
        foreach (var format in new[] { "zip", "directory" })
        {
            var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var output = ScratchDirectory.CreateChild();
            try
            {
                var (id, revision) = CliKeyPlanTests.Session(world, work); var merge = Path.Combine(work, "source.zip");
                var metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep\"},\"opaque\":7}";
                File.WriteAllBytes(merge, Zip(("pack.mcmeta", metadata), ("assets/test/opaque.bin", "Keep")));
                var file = Path.Combine(work, "pack-plan.json");
                using var preview = Success(Call(world, work, id, revision, "create-pack", ["--merge", merge, "--pack-format", "34", "--plan-output", file,
                    .. (format == "directory" ? new[] { "--format", "directory", "--name", "Bundle", "--output", output } : Array.Empty<string>())]));
                var plan = PatchPlanJson.Read(File.ReadAllText(file)); var target = format == "zip" ? world : output;
                Require(plan.Operations.Single() is ResourcePatch { Generation.Merge: not null } && !File.Exists(Path.Combine(world, "resources.zip"))
                    && !File.Exists(Path.Combine(output, "Bundle/pack.mcmeta")), "pack preview freezes input without publishing a pack");
                File.WriteAllBytes(merge, [9]);
                using var applied = Success(Run(work, "apply", target, "--plan", file, "--world-offline", "--state-root", Path.Combine(work, "state")));
                if (format == "directory") Require(File.ReadAllText(Path.Combine(output, "Bundle/pack.mcmeta")) == metadata && File.ReadAllText(Path.Combine(output, "Bundle/assets/test/opaque.bin")) == "Keep", "named directory pack keeps frozen metadata and opaque content");
                else
                {
                    using var zip = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "resources.zip")));
                    using var stream = zip.GetEntry("assets/test/opaque.bin")!.Open(); using var text = new StreamReader(stream);
                    Require(text.ReadToEnd() == "Keep" && zip.GetEntry("assets/minecraft/lang/en_us.json") is not null && zip.GetEntry("assets/minecraft/lang/zh_tw.json") is not null, "directory-layout default world/resources.zip contains both languages and frozen opaque merge bytes");
                }
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); Directory.Delete(output, true); }
        }
    }
    public static void DefaultWorldLanguageAndModernPackParentsAreCreatedOnlyByApply()
    {
        foreach (var modern in new[] { false, true })
        {
            var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
            try
            {
                if (modern) Level(world, true);
                var (id, revision) = CliKeyPlanTests.Session(world, work); var file = Path.Combine(work, "default-plan.json");
                var directory = Path.Combine(world, modern ? "resourcepacks" : ".nbtcli.language");
                using var preview = Success(Call(world, work, id, revision, modern ? "create-pack" : "export-lang", "--plan-output", file));
                var plan = PatchPlanJson.Read(File.ReadAllText(file));
                Require(!Directory.Exists(directory) && plan.Outputs.All(output => output.Artifact.RelativePath.StartsWith(modern ? "resourcepacks/" : ".nbtcli.language/", StringComparison.Ordinal)), "default missing directories remain absent and are declared relative to the existing physical world root");
                using var applied = Success(Run(work, "apply", world, "--plan", file, "--world-offline", "--state-root", Path.Combine(work, "state")));
                Require(Directory.Exists(directory) && File.Exists(Path.Combine(directory, modern ? "resources.zip" : "zh_tw.json")), "default directory-based and namespaced resource placement is created only during durable apply");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void VerifiedIntermediateWorldsInferTheActualPackFormat()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = CliKeyPlanTests.Session(world, work);
            var unsupported = new List<int>();
            foreach (var (version, format) in new[] { (3690, 21), (3819, 28), (3946, 34), (3947, 34) })
            {
                Put(world, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(version, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                var file = Path.Combine(work, $"pack-{version}.json");
                var result = Call(world, work, id, revision, "create-pack", "--plan-output", file);
                if (result.Exit == 4) { unsupported.Add(version); continue; }
                using var preview = Success(result);
                var operation = (ResourcePatch)PatchPlanJson.Read(File.ReadAllText(file)).Operations.Single();
                Require(operation.Generation.PackFormat == new PackFormatVersion(format, 0),
                    $"DataVersion {version} uses its actual resource pack format without an override");
            }
            Require(unsupported.Count == 0, "verified default resource pack formats were rejected: " + string.Join(", ", unsupported));
            Require(!File.Exists(Path.Combine(world, "resources.zip")), "format inference only previews a plan");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void DirectResourceWritesRequireExplicitSafetyAndKeepSessionStateTruthful()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var output = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = CliKeyPlanTests.Session(world, work); Directory.CreateDirectory(Path.Combine(world, "data"));
            var original = Directory.GetFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            Require(Call(world, work, id, revision, "export-lang", "--apply", "--output", output).Exit == 2, "direct resource writes require offline acknowledgement");
            Require(Call(world, work, id, revision + 1, "export-lang", "--output", output).Exit == 3, "resource values bind the requested session revision");
            Require(Call(world, work, id, revision, "export-lang", "--output", output, "--original-lang", "zh-TW", "--target-lang", "zh_tw").Exit == 2, "locale normalization cannot turn two languages into one overwritten file");
            Require(Call(world, work, id, revision, "export-lang", "--output", Path.Combine(world, "data"), "--apply", "--world-offline").Exit == 4, "public resource commands refuse actual gameplay scopes");
            Require(Call(world, work, id, revision, "create-pack", "--output", output, "--pack-format", "999", "--apply", "--world-offline").Exit == 4, "unknown pack format cannot publish outputs");
            Require(!Directory.GetFiles(output).Any() && original.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)), "all refusals preserve source and resource data");
            var file = Path.Combine(work, "direct-resource-plan.json");
            using var applied = Success(Call(world, work, id, revision, "export-lang", "--output", output, "--plan-output", file, "--apply", "--world-offline", "--original-lang", "en-US", "--target-lang", "zh-TW"));
            Require(!applied.RootElement.GetProperty("data").GetProperty("sourceSession").GetProperty("sessionModified").GetBoolean()
                && PatchPlanJson.Read(File.ReadAllText(file)).Operations.Single() is ResourcePatch && File.Exists(Path.Combine(output, "en_us.json")) && File.Exists(Path.Combine(output, "zh_tw.json")), "direct export retains a canonical plan, normalizes resource locales and reports that session state was not modified");
            var session = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(work, "state", "sessions"))).Read(id);
            Require(session.Revision == revision && session.Slots.All(slot => slot.State == SessionSlotState.Translated), "resource completion never implies session seal or gameplay apply");
            using var help = Success(Run(work, "translate", "create-pack", "--help")); var options = help.RootElement.GetProperty("data").GetProperty("options").EnumerateArray().Select(item => item.GetString()).ToArray();
            Require(options.Contains("--output") && options.Contains("--plan-output") && options.Contains("--merge") && options.Contains("--world-offline"), "public help reports the actual resource command contract");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); Directory.Delete(output, true); }
    }
    public static void DirectoryMergeAliasAndExistingKeyConflictsPreserveBothInputsAndOutputs()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var output = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = CliKeyPlanTests.Session(world, work); var merge = Path.Combine(work, "merge");
            Put(merge, "pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Preserved\"},\"opaque\":8}"u8.ToArray());
            Put(merge, "assets/test/opaque.bin", [0, 255, 13]);
            using var applied = Success(Call(world, work, id, revision, "create-pack", "--output", output, "--name", "Bundle", "--pack-format", "34", "--merge", merge, "--apply", "--world-offline"));
            Require(File.ReadAllBytes(Path.Combine(output, "Bundle/assets/test/opaque.bin")).SequenceEqual(new byte[] { 0, 255, 13 }), "directory merge alias uses immutable verified input and preserves binary assets");
            var target = Path.Combine(output, "Bundle/assets/minecraft/lang/zh_tw.json"); using var language = BoundedJson.ReadDecoded(File.ReadAllBytes(target), DecodeLimits.Default);
            var values = language.RootElement.EnumerateObject().ToDictionary(NativeJsonStrings.PropertyName, property => NativeJsonStrings.Read(property.Value), StringComparer.Ordinal);
            values[values.Keys.First()] = "External conflicting value"; File.WriteAllBytes(target, JsonSerializer.SerializeToUtf8Bytes(values)); var before = File.ReadAllBytes(target);
            var result = Call(world, work, id, revision, "create-pack", "--output", output, "--name", "Bundle", "--pack-format", "34", "--merge", merge, "--apply", "--world-offline");
            Require(result.Exit == 3 && File.ReadAllBytes(target).SequenceEqual(before) && File.ReadAllBytes(Path.Combine(merge, "assets/test/opaque.bin")).SequenceEqual(new byte[] { 0, 255, 13 }), "existing claimed language keys conflict before commit and neither merge source nor target is overwritten");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); Directory.Delete(output, true); }
    }
    public static void ExplicitExternalPackOptionsUseFrozenSessionValuesWithoutSourceMetadata()
    {
        foreach (var shape in new[] { "missing", "malformed", "unknown" })
        {
            var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild(); var output = ScratchDirectory.CreateChild();
            try
            {
                var (id, revision) = CliKeyPlanTests.Session(world, work);
                if (shape == "missing") File.Delete(Path.Combine(world, "level.dat"));
                else Put(world, "level.dat", shape == "malformed" ? [9] : CompressedNbt.WriteDocument(WorldMetadataTests.Level(999999, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                var before = Directory.GetFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                using var preview = Success(Call(world, work, id, revision, "create-pack", "--output", output, "--pack-format", "34"));
                Require(!File.Exists(Path.Combine(output, "resources.zip")), "explicit frozen pack preview does not need the changed source metadata or write outputs: " + shape);
                var expectedFailure = shape == "unknown" ? 4 : 5;
                Require(Call(world, work, id, revision, "create-pack", "--output", output).Exit == expectedFailure
                    && Call(world, work, id, revision, "create-pack", "--pack-format", "34").Exit == expectedFailure, "metadata remains required to infer a pack format or default output layout: " + shape);
                Directory.CreateDirectory(Path.Combine(world, "data"));
                Require(Call(world, work, id, revision, "create-pack", "--output", Path.Combine(world, "data"), "--pack-format", "34", "--apply", "--world-offline").Exit != 0
                    && Call(world, work, id, revision, "export-lang", "--output", Path.Combine(world, "data"), "--apply", "--world-offline").Exit != 0,
                    "a known source world's damaged metadata cannot turn its gameplay paths into external output authority: " + shape);
                using var applied = Success(Call(world, work, id, revision, "create-pack", "--output", output, "--pack-format", "34", "--apply", "--world-offline"));
                Require(File.Exists(Path.Combine(output, "resources.zip")) && before.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)), "explicit external resource generation preserves the changed source exactly: " + shape);
                var session = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(work, "state", "sessions"))).Read(id);
                Require(session.Revision == revision && session.Slots.All(slot => slot.State == SessionSlotState.Translated), "source metadata exemption grants no session mutation or sealing authority");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); Directory.Delete(output, true); }
        }
    }
}

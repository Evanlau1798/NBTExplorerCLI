using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class CliConvertTests
{
    public static void EmptyConversionPlansRoundTripWithoutResourceOrGameplayWrites()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var planFile = Path.Combine(work, "empty.json");
            using var preview = Success(Run(work, "convert", world, "--lang", "en_us", "--match-pattern", "^No selected text$", "--state-root", State(work), "--output", planFile));
            Require(PatchPlanJson.Read(File.ReadAllText(planFile)).Operations.IsEmpty, "a real zero-match query exports an honest empty semantic plan");
            using var applied = Success(Run(work, "apply", world, "--plan", planFile, "--state-root", State(work), "--world-offline"));
            using var direct = Success(Run(work, "convert", world, "--lang", "en_us", "--match-pattern", "^No selected text$", "--state-root", State(work), "--apply", "--world-offline"));
            Require(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && !File.Exists(Path.Combine(world, "resources.zip")), "both exported and immediate empty conversions preserve gameplay bytes and create no resource dependency");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ScopedInputsBindExistingLanguageDependenciesWithoutWideningSelection()
    {
        foreach (var zip in new[] { true, false })
            foreach (var existing in new[] { true, false })
            {
                var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
                try
                {
                    const string metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Keep metadata\"}}";
                    const string entry = "assets/test/lang/en_us.json";
                    var language = zip ? "resources.zip#" + entry : "resourcepacks/test/" + entry;
                    if (zip) Put(world, "resources.zip", Zip([("pack.mcmeta", metadata), ("assets/test/textures/opaque.bin", "Opaque"), .. existing ? new[] { (entry, "{\"keep.key\":\"Keep\"}") } : Array.Empty<(string, string)>()]));
                    else
                    {
                        Put(world, "resourcepacks/test/pack.mcmeta", Encoding.UTF8.GetBytes(metadata));
                        Put(world, "resourcepacks/test/assets/test/textures/opaque.bin", "Opaque"u8.ToArray());
                        if (existing) Put(world, "resourcepacks/test/" + entry, "{\"keep.key\":\"Keep\"}"u8.ToArray());
                    }
                    var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(Path.GetFullPath, File.ReadAllBytes);
                    var planFile = Path.Combine(work, "scoped.json");
                    var result = Run(work, "convert", world, "--path", WorldQueryTests.Function(0), "--language-output", language, "--lang", "en_us", "--state-root", State(work), "--output", planFile);
                    Require(result.Exit == 0, $"scoped dependency zip={zip} existing={existing}: {result.Output}");
                    using var preview = Success(result);
                    var plan = PatchPlanJson.Read(File.ReadAllText(planFile));
                    Require(plan.Operations.Length == 1 && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "exact input path selects one gameplay slot and preview does not modify dependencies");
                    var boundDependency = Path.Combine(world, zip ? "resources.zip" : "resourcepacks/test/pack.mcmeta"); var boundBytes = File.ReadAllBytes(boundDependency);
                    File.WriteAllBytes(boundDependency, [9]);
                    Require(Run(work, "apply", world, "--plan", planFile, "--state-root", State(work), "--world-offline").Exit == 3
                        && File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0))).SequenceEqual(before[Path.GetFullPath(Path.Combine(world, WorldQueryTests.Function(0)))]), "changed output dependency is rejected before any gameplay write");
                    File.WriteAllBytes(boundDependency, boundBytes);
                    using var applied = Success(Run(work, "apply", world, "--plan", planFile, "--state-root", State(work), "--world-offline"));
                    byte[] content;
                    if (zip)
                    {
                        using var pack = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "resources.zip")));
                        using var source = pack.GetEntry(entry)!.Open(); using var bytes = new MemoryStream(); source.CopyTo(bytes); content = bytes.ToArray();
                        byte[] ReadEntry(string name) { using var stream = pack.GetEntry(name)!.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); }
                        Require(ReadEntry("assets/test/textures/opaque.bin").SequenceEqual("Opaque"u8.ToArray()) && ReadEntry("pack.mcmeta").SequenceEqual(Encoding.UTF8.GetBytes(metadata)), "merge retains unrelated entry and metadata bytes");
                    }
                    else content = File.ReadAllBytes(Path.Combine(world, "resourcepacks/test/" + entry));
                    using var lang = BoundedJson.ReadDecoded(content);
                    Require(lang.RootElement.EnumerateObject().Count() == (existing ? 2 : 1) && (!existing || lang.RootElement.GetProperty("keep.key").GetString() == "Keep"), "dependency merge preserves old keys and generates exactly the selected key");
                    var changed = new[] { Path.GetFullPath(Path.Combine(world, WorldQueryTests.Function(0))), Path.GetFullPath(Path.Combine(world, zip ? "resources.zip" : "resourcepacks/test/" + entry)) };
                    Require(before.Where(pair => !changed.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "bringing in output dependencies never selects extra gameplay text or mutates unrelated files");
                }
                finally { Directory.Delete(world, true); Directory.Delete(work, true); }
            }
    }
    private static string State(string work) => Path.Combine(work, "state");
    private static string WorldRoot()
    {
        var world = WorldPreparationTests.CreateWorld();
        Put(world, WorldQueryTests.Function(1), Encoding.UTF8.GetBytes("tellraw @a[name=Guard] \"Text0\"\n"));
        var document = CompressedNbt.ReadDocument(File.ReadAllBytes(Path.Combine(world, "level.dat")), NbtCompression.GZip).Document;
        var path = new TypedPath([PathSegment.ForKey("Data"), PathSegment.ForKey("CustomBossEvents"), PathSegment.ForKey("test:event"), PathSegment.ForKey("Name")]);
        var changed = TypedTreeEdits.Apply(document, [TypedEdit.Replace(path, NbtLocator.Resolve(document.Root, path).Value!, new NbtString("{\"text\":\"Text0\"}"))]);
        Put(world, "level.dat", CompressedNbt.WriteDocument(changed, NbtCompression.GZip)); return world;
    }
    public static void PreviewApplyAndContextAwareDeduplicationUseTheSharedPipeline()
    {
        foreach (var keep in new[] { false, true })
        {
            var world = WorldRoot(); var work = ScratchDirectory.CreateChild();
            try
            {
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var file = Path.Combine(work, "convert.json");
                using var preview = Success(Run(work, ["convert", world, "--lang", "en_us", "--prefix", "demo", "--output", file, "--state-root", State(work), .. (keep ? new[] { "--keepdup" } : Array.Empty<string>())]));
                var plan = PatchPlanJson.Read(File.ReadAllText(file)); var keys = plan.Operations.OfType<KeyConversionPatch>().ToArray();
                Require(keys.Length == 6 && keys.Select(key => key.Conversion.Dependency.Key).Distinct().Count() == (keep ? 6 : 5)
                    && keys.All(key => key.Conversion.Dependency.Key.StartsWith("demo.", StringComparison.Ordinal)) && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "preview keeps all exact occurrences, shares only matching profile/context keys and preserves world bytes");
                Require(keys.Where(key => key.Conversion.Slot.SourceText == "Text0").Select(key => key.Conversion.Dependency.Key).Distinct().Count() == (keep ? 3 : 2), "same-spelled bossbar and tellraw text retain different context authority");
                using var applied = Success(Run(work, "apply", world, "--plan", file, "--world-offline", "--state-root", State(work)));
                using var pack = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "resources.zip"))); using var stream = pack.GetEntry("assets/nbt/lang/en_us.json")!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                using var language = BoundedJson.ReadDecoded(bytes.ToArray());
                Require(language.RootElement.EnumerateObject().Count() == (keep ? 6 : 5), "duplicate-key sharing still stages one consistent physical language object");
                var readback = new WorldQueryService(new WorldInventory(world, plan.WorldId), new FileSystemScanCacheStore(Path.Combine(work, "after"))).QueryAsync(new(new(SnapshotScope.All))).GetAwaiter().GetResult().Snapshot!;
                Require(readback.ReadSources().SelectMany(row => row.Text?.TranslationKeys ?? []).Count(item => keys.Any(key => key.Conversion.Dependency.Key == item.Key)) >= 6, "all component conversions are read back, including both occurrences sharing one language key");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void LanguageLocaleAndOptionalBackupNeverDisableRequiredRecovery()
    {
        foreach (var noBackup in new[] { false, true })
        {
            var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
            try
            {
                Require(Run(work, "convert", world, "--lang", "zh_tw", "--apply", "--state-root", State(work)).Exit == 2, "convert write requires the normal explicit offline acknowledgement");
                using var applied = Success(Run(work, ["convert", world, "--lang", "zh_tw", "--ui-locale", "en-US", "--apply", "--world-offline", "--state-root", State(work), .. (noBackup ? new[] { "--no-backup" } : Array.Empty<string>())]));
                var data = applied.RootElement.GetProperty("data");
                Require(data.GetProperty("conversion").GetProperty("language").GetString() == "zh_tw" && data.GetProperty("result").GetProperty("backupManifestHash").ValueKind == JsonValueKind.String
                    && (data.GetProperty("convenienceBackupHash").ValueKind == JsonValueKind.Null) == noBackup, "convert language is independent from UI locale; no-backup only disables the extra convenience copy");
                using var pack = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "resources.zip")));
                Require(pack.GetEntry("assets/nbt/lang/zh_tw.json") is not null, "the explicit conversion language names the required language output");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void UnsafeNamesAndInvalidOptionsRefuseBeforeWorldMutation()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var file = Path.Combine(work, "unsafe.json");
            Require(Run(work, "convert", world, "--lang", "en_us", "--output", file, "--state-root", State(work)).Exit == 3 && !File.Exists(file)
                && before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "conversion cannot borrow identifying-name reference authority");
            Require(Run(work, "convert", world, "--apply", "--world-offline", "--state-root", State(work)).Exit == 2
                && Run(work, "convert", world, "--lang", "../escape", "--state-root", State(work)).Exit == 2, "conversion language must be explicit and a safe resource filename");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void SharedLanguageTargetsCannotDisagreeOnLocaleOrContent()
    {
        var world = WorldRoot(); var work = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(world, Path.Combine(work, "cache")); var inventory = new WorldInventory(world, World);
            var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Text == "Text0" && item.Address.Artifact.Kind == ArtifactKind.McFunction).Select(item => item.ToSlot("en_us", "en_us")).ToArray();
            Require(slots.Length == 2, "control: two real command occurrences share visible text");
            var operations = new WorldKeyConversionService(inventory).Create(snapshot, slots.Select((slot, index) => new LiteralTranslation(slot, new(slot.Id, slot.SourceTextHash, "en_us", "Value" + index))), new(World, "resources.zip", ArtifactKind.Archive, "assets/nbt/lang/en_us.json"), "test.")
                .Select(key => { var value = key.Conversion; return new KeyConversionPatch(key.Id, new(value.Address, value.Expected, value.Rule, value.Profile, value.ProfileVersion, value.Slot, value.Reply,
                    new(value.Dependency.LanguageArtifact, value.Dependency.PackArtifact, value.Dependency.Locale, "shared", value.Dependency.Text))); }).ToArray();
            var refused = false; try { _ = new WorldPlanBuilder(inventory).Build(snapshot, operations); } catch (ArgumentException) { refused = true; }
            Require(refused, "one shared language key cannot accept conflicting declared values");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ScopedPlansApplyTheirOriginalScopeAndKeepUnselectedNames()
    {
        var fields = new SnapshotScope(["minecraft:overworld"], ["r.0.0.mca"], ["command.function"], [WorldQueryTests.Function(0)]);
        var identity = SourceIdentity.Snapshot(World, [], fields, new("codec", "profile", "rules", "policy")); var restored = SourceIdentity.ReadSnapshotScope(identity);
        Require(restored.Dimensions.SequenceEqual(fields.Dimensions) && restored.Regions.SequenceEqual(fields.Regions) && restored.Types.SequenceEqual(fields.Types) && restored.ArtifactPaths.SequenceEqual(fields.ArtifactPaths), "canonical scope decoding retains all four independent sets");
        var rejectedKind = false; try { _ = SourceIdentity.ReadSnapshotScope(SourceIdentity.Artifact(new(World, "level.dat", ArtifactKind.Nbt))); } catch (ArgumentException) { rejectedKind = true; }
        Require(rejectedKind, "a non-snapshot identity cannot supply an apply scope");
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var file = Path.Combine(work, "scoped.json");
            using var preview = Success(Run(work, "convert", world, "--lang", "en_us", "--types", "McFunction", "--match-pattern", "^Guard$", "--workers", "1", "--output", file, "--state-root", State(work)));
            var plan = PatchPlanJson.Read(File.ReadAllText(file)); Require(plan.Operations.Length == 1, "the explicit scope selects one command literal without selecting identifying names");
            using var applied = Success(Run(work, "apply", world, "--plan", file, "--world-offline", "--state-root", State(work)));
            var selected = Path.GetFullPath(Path.Combine(world, WorldQueryTests.Function(0)));
            Require(before.Where(pair => !Path.GetFullPath(pair.Key).Equals(selected, StringComparison.OrdinalIgnoreCase)).All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "a scoped key plan preserves all unselected physical artifacts and name/reference bytes; changed=" + string.Join(",", before.Where(pair => !File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)).Select(pair => pair.Key)));
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}

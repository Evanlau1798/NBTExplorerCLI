using System.Collections.Immutable;
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

internal static class CliKeyPlanTests
{
    private static string State(string work) => Path.Combine(work, "state");
    private static Invocation Call(string world, string work, Guid id, string action, params string[] args) => Run(work,
        ["translate", action, world, "--session-id", id.ToString(), "--state-root", State(work), .. args]);
    internal static (Guid Id, long Revision) Session(string world, string work, string? pattern = null)
    {
        var id = Guid.NewGuid();
        using var started = Success(Call(world, work, id, "start", ["--source-language", "en_us", "--target-language", "zh_tw", .. (pattern is null ? Array.Empty<string>() : new[] { "--match-pattern", pattern })]));
        using var exported = Success(Call(world, work, id, "export"));
        var page = SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
        var reply = SessionExchangeJson.ReadReply(exported.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText());
        reply = reply with { Slots = reply.Slots.Select(slot => slot with { State = SessionReplyState.Translated, Text = "譯:" + page.Slots.Single(item => item.SlotId == slot.SlotId).SourceText }).ToImmutableArray() };
        var file = Path.Combine(work, "reply-" + id.ToString("N") + ".json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply));
        using var submitted = Success(Call(world, work, id, "submit", "--reply", file));
        return (id, submitted.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64());
    }
    private static Invocation Plan(string world, string work, Guid id, long revision, string language, string output) => Call(world, work, id, "plan",
        "--keys", "--prefix", "test.", "--language-output", language, "--revision", revision.ToString(), "--output", output);
    public static void PublicKeysPlansGenerateDependenciesAndSealPureAndMixedWorlds()
    {
        foreach (var mode in new[] { "datapack", "nbt", "mixed" })
        {
            var world = mode == "datapack" ? WorldQueryTests.CreateWorld() : WorldPreparationTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
            try
            {
                if (mode == "nbt") Directory.Delete(Path.Combine(world, "datapacks"), true);
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                var (id, revision) = Session(world, work); var planFile = Path.Combine(work, "keys.json");
                using var preview = Success(Plan(world, work, id, revision, "generated.zip#assets/test/lang/zh_tw.json", planFile));
                var plan = PatchPlanJson.Read(File.ReadAllText(planFile)); var session = new TranslationSessionService(new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions"))).Read(id);
                Require(plan.Operations.OfType<KeyConversionPatch>().Count() == session.Slots.Length && plan.Preconditions.Any(input => input.Artifact.RelativePath == "generated.zip" && input.Image.ExpectedAbsent)
                    && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "every selected translated component has an exact conversion and an absent-bound pack dependency; preview preserves the world");
                using var applied = Success(Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", State(work)));
                using var pack = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "generated.zip"))); using var stream = pack.GetEntry("assets/test/lang/zh_tw.json")!.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                using var language = BoundedJson.ReadDecoded(bytes.ToArray());
                Require(language.RootElement.EnumerateObject().Count() == session.Slots.Length && language.RootElement.EnumerateObject().All(item => NativeJsonStrings.Read(item.Value).StartsWith("譯:", StringComparison.Ordinal)), "all language outputs join the verified physical set");
                using var sealedResult = Success(Call(world, work, id, "seal", "--revision", revision.ToString(), "--operation-id", Guid.NewGuid().ToString(), "--plan-hash", plan.PlanHash.Hex));
                Require(sealedResult.RootElement.GetProperty("data").GetProperty("status").GetProperty("applied").GetInt32() == session.Slots.Length, "complete key and language proofs seal the same validated session");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void ExistingPacksMergeOnlyDeclaredKeysAndKeepUnselectedEntries()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var original = Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Keep description\"}}"),
                ("assets/test/lang/zh_tw.json", "{\"keep.key\":\"Keep\"}"), ("opaque.bin", "Keep opaque")); Put(world, "resources.zip", original);
            var (id, revision) = Session(world, work, "^Text0$"); var planFile = Path.Combine(work, "merge.json");
            using var preview = Success(Plan(world, work, id, revision, "resources.zip#assets/test/lang/zh_tw.json", planFile));
            using var applied = Success(Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", State(work)));
            using var prior = ZipArchiveValidation.OpenRead(original); using var current = ZipArchiveValidation.OpenRead(File.ReadAllBytes(Path.Combine(world, "resources.zip")));
            byte[] Entry(System.IO.Compression.ZipArchive zip, string name) { using var value = zip.GetEntry(name)!.Open(); using var bytes = new MemoryStream(); value.CopyTo(bytes); return bytes.ToArray(); }
            Require(Entry(current, "opaque.bin").SequenceEqual(Entry(prior, "opaque.bin")) && Entry(current, "pack.mcmeta").SequenceEqual(Entry(prior, "pack.mcmeta")), "key planning preserves every unselected pack entry");
            using var language = BoundedJson.ReadDecoded(Entry(current, "assets/test/lang/zh_tw.json"));
            Require(language.RootElement.GetProperty("keep.key").GetString() == "Keep" && language.RootElement.EnumerateObject().Count() == 2, "merge retains the existing language key and adds exactly one declared key");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void InvalidModesAndIdentifyingNamesRetainAcceptedReplies()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = Session(world, work, "^Guard$"); var store = new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions")); var before = store.Read(id)!;
            var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes); var output = Path.Combine(work, "unsafe.json");
            var result = Plan(world, work, id, revision, "generated.zip#assets/test/lang/zh_tw.json", output);
            Require(result.Exit == 3 && !File.Exists(output) && store.Read(id)!.SequenceEqual(before) && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "key conversion never borrows identifying-name authority and never consumes replies on refusal");
            Require(Call(world, work, id, "plan", "--keys", "--direct", "--revision", revision.ToString()).Exit == 2
                && Call(world, work, id, "plan", "--keys", "--synchronize-references", "--revision", revision.ToString()).Exit == 2
                && Call(world, work, id, "plan", "--keys", "--revision", revision.ToString()).Exit == 2, "key/direct/reference modes and required output arguments are explicit");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void NestedModernComponentsAndEncodedCommandsUseTheirExactRoots()
    {
        foreach (var modern in new[] { true, false })
        {
            var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
            try
            {
                if (modern)
                {
                    Directory.Delete(Path.Combine(world, "datapacks"), true);
                    var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
                    var component = new NbtCompound([new("text", new NbtString("Hello")), new("color", new NbtString("red")), new("extra", new NbtList(NbtType.Compound, [new NbtCompound([new("text", new NbtString("Tail"))])]))]);
                    var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", component)]))]);
                    Put(world, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
                }
                else
                {
                    Put(world, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":26,\"description\":\"Old\"}}"u8.ToArray());
                    Put(world, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("give @p written_book{pages:[\u0027[{\"text\":\"Hello\",\"color\":\"red\"},\"Tail\"]\u0027]}\\n".Replace("\\n", "\n", StringComparison.Ordinal)));
                }
                var (id, revision) = Session(world, work, "^(Hello|Tail)$"); var planFile = Path.Combine(work, "nested.json");
                using var preview = Success(Plan(world, work, id, revision, "generated.zip#assets/test/lang/zh_tw.json", planFile));
                var plan = PatchPlanJson.Read(File.ReadAllText(planFile)); Require(plan.Operations.OfType<KeyConversionPatch>().Count() == 2, "both exact component literals become independent declared conversions");
                using var applied = Success(Run(work, "apply", world, "--plan", planFile, "--world-offline", "--state-root", State(work)));
                if (modern)
                {
                    var document = CompressedNbt.ReadDocument(File.ReadAllBytes(Path.Combine(world, "level.dat")), NbtCompression.GZip).Document;
                    var component = (NbtCompound)NbtLocator.Resolve(document.Root, new([PathSegment.ForKey("Data"), PathSegment.ForKey("CustomBossEvents"), PathSegment.ForKey("test:event"), PathSegment.ForKey("Name")])).Value!;
                    Require(component.Entries.Any(entry => entry.Name == "translate") && component.Entries.Single(entry => entry.Name == "color").Value == new NbtString("red")
                        && ((NbtList)component.Entries.Single(entry => entry.Name == "extra").Value).Items[0] is NbtCompound child && child.Entries.Any(entry => entry.Name == "translate"), "nested modern keys preserve styling and both converted roots");
                }
                else Require(File.ReadAllText(Path.Combine(world, WorldQueryTests.Function(0))).Contains("translate", StringComparison.Ordinal), "the encoded SNBT/JSON leaf is rewritten by its existing grammar adapter");
                using var completed = Success(Call(world, work, id, "seal", "--revision", revision.ToString(), "--operation-id", Guid.NewGuid().ToString(), "--plan-hash", plan.PlanHash.Hex));
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void PhysicalStringListsRequireAnExplicitSupportedShapeChange()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            Directory.Delete(Path.Combine(world, "datapacks"), true);
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var component = new NbtCompound([new("text", new NbtString("Hello")), new("extra", new NbtList(NbtType.String, [new NbtString("Tail")]))]);
            var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", component)]))]);
            Put(world, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            var original = File.ReadAllBytes(Path.Combine(world, "level.dat")); var (id, revision) = Session(world, work, "^Tail$"); var planFile = Path.Combine(work, "unsafe-list.json");
            var result = Plan(world, work, id, revision, "generated.zip#assets/test/lang/zh_tw.json", planFile);
            Require(result.Exit == 4 && !File.Exists(planFile) && File.ReadAllBytes(Path.Combine(world, "level.dat")).SequenceEqual(original), "a homogeneous physical string list cannot silently promote one element to a compound or bypass the supported-shape boundary, actual=" + result.Exit);
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void LazyKeyInputsAreChargedBeforeAnotherUnboundedAllocation()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var limits = new DecodeLimits(MaxDecodedBytes: 65_536); var inventory = new WorldInventory(world, World);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(work, "cache"))).QueryAsync(new(new(SnapshotScope.All, limits))).GetAwaiter().GetResult().Snapshot!;
            var enumerated = 0; var text = new string('a', 4096);
            IEnumerable<LiteralTranslation> Inputs()
            {
                for (var index = 0; index < 100; index++)
                {
                    if (++enumerated > 20) throw new InvalidOperationException("The caller produced more input than the bounded consumer should request");
                    var slot = new TextOccurrence(new(new(World, WorldQueryTests.Function(0), ArtifactKind.McFunction), new([PathSegment.ForKey("item" + index)]), "command.tellraw"), "Hello", "Hello", true).ToSlot("en_us", "zh_tw");
                    yield return new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", text));
                }
            }
            var limited = false;
            try { _ = new WorldKeyConversionService(inventory).Create(snapshot, Inputs(), new(World, "generated.zip", ArtifactKind.Archive, "assets/test/lang/zh_tw.json"), "test."); }
            catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { limited = true; }
            catch (InvalidOperationException) { }
            Require(limited && enumerated <= 20, "retained input bytes must be charged during enumeration, before the caller can supply another unbounded recipe");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void GeneratedFieldsCannotCompeteWithExplicitTypedEdits()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var metadata = WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions); var data = (NbtCompound)((NbtCompound)metadata.Root).Entries.Single().Value;
            var component = new NbtCompound([new("text", new NbtString("Hello")), new("color", new NbtString("red"))]);
            var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", component)]))]);
            Put(world, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            var snapshot = WorldQueryTests.Query(world, Path.Combine(work, "cache")); var inventory = new WorldInventory(world, World);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var keys = new WorldKeyConversionService(inventory).Create(snapshot, [new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "譯"))], new(World, "generated.zip", ArtifactKind.Archive, "assets/test/lang/zh_tw.json"), "test.");
            var path = new TypedPath(keys.Single().Conversion.Address.NodePath.Segments.Add(PathSegment.ForKey("color"))); var address = new SourceAddress(slot.Address.Artifact, path, "typed.value");
            var refused = false;
            try { _ = new WorldPlanBuilder(inventory).Build(snapshot, [.. keys, new TypedPatch("competing", address, TypedEdit.Rename(path, new NbtString("red"), "translate"))]); }
            catch (ArgumentException) { refused = true; }
            var independent = new WorldPlanBuilder(inventory).Build(snapshot, [.. keys, new TypedPatch("style", address, TypedEdit.Replace(path, new NbtString("red"), new NbtString("blue")))]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(world, World)).Prepare(snapshot, independent, true);
            var output = prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == "level.dat");
            var candidate = CompressedNbt.ReadDocument(File.ReadAllBytes(output.CandidatePath!), NbtCompression.GZip).Document;
            Require(NbtLocator.Resolve(candidate.Root, path).Value == new NbtString("blue"), "an independent explicitly authorized style edit composes with the exact key conversion");
            Require(refused, "a typed rename into a key-generated translate field must conflict at plan construction, not depend on staging order");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CliScanTests;

namespace NBTExplorerCLI.Verification;

internal static class CliReferencePlanTests
{
    private static string State(string work) => Path.Combine(work, "state");
    private static Invocation Call(string work, string world, Guid id, string action, params string[] args) => Run(work,
        ["translate", action, world, "--session-id", id.ToString(), "--state-root", State(work), .. args]);
    internal static (Guid Id, long Revision) Session(string world, string work, bool bothTypes = false, bool partial = false, bool scoped = false, bool merge = false)
    {
        var id = Guid.NewGuid(); using var started = Success(Call(work, world, id, "start", ["--source-language", "en_us", "--target-language", "zh_tw", .. (scoped ? new[] { "--path", WorldReferenceTests.Entities } : Array.Empty<string>())]));
        using var exported = Success(Call(work, world, id, "export")); var page = SessionExchangeJson.ReadPage(exported.RootElement.GetProperty("data").GetProperty("page").GetRawText());
        Require(page.Slots.Any(slot => slot.Context == "armor_stand.name"), "control: the public page contains the real typed identifying name");
        var reply = SessionExchangeJson.ReadReply(exported.RootElement.GetProperty("data").GetProperty("replyTemplate").GetRawText()); var selected = 0;
        reply = reply with { Slots = reply.Slots.Select(slot =>
        {
            var source = page.Slots.Single(item => item.SlotId == slot.SlotId); string? text = null;
            if (source.Context == "armor_stand.name" && (source.SourceText == "Guard" || merge && source.SourceText == "Other") && (!partial || selected == 0)) { text = "Sentinel"; selected++; }
            else if (bothTypes && source.Context == "entity.name") text = "Undead";
            else if (source.Context == "command.click.message" && source.SourceText == "Clicked") text = "Opened";
            return slot with { State = text is null ? SessionReplyState.Unchanged : SessionReplyState.Translated, Text = text };
        }).ToImmutableArray() };
        var file = Path.Combine(work, "names-reply.json"); File.WriteAllText(file, SessionExchangeJson.WriteReply(reply));
        using var submitted = Success(Call(work, world, id, "submit", "--reply", file));
        return (id, submitted.RootElement.GetProperty("data").GetProperty("status").GetProperty("revision").GetInt64());
    }
    private static Invocation Plan(string world, string work, Guid id, long revision, string file, bool synchronize) => Call(work, world, id, "plan",
        ["--direct", "--revision", revision.ToString(), "--operation-id", Guid.NewGuid().ToString(), "--output", file, .. (synchronize ? new[] { "--synchronize-references" } : Array.Empty<string>())]);
    private static void ApplyAndSeal(string world, string work, Guid id, long revision, string file, PatchPlan plan)
    {
        using var applied = Success(Run(work, "apply", world, "--plan", file, "--world-offline", "--state-root", State(work)));
        using var completed = Success(Call(work, world, id, "seal", "--revision", revision.ToString(), "--operation-id", Guid.NewGuid().ToString(), "--plan-hash", plan.PlanHash.Hex));
        var status = completed.RootElement.GetProperty("data").GetProperty("status"); Require(status.GetProperty("applied").GetInt32() == status.GetProperty("total").GetInt32(), "every validated decision seals after the complete group receipt");
    }
    public static void ExplicitAuthorityBuildsCompleteSingleAndMultipleTypedGroups()
    {
        foreach (var (duplicate, bothTypes) in new[] { (false, false), (true, false), (false, true) })
        {
            var world = WorldReferenceTests.CreateWorld(duplicate); var work = ScratchDirectory.CreateChild();
            try
            {
                var (id, revision) = Session(world, work, bothTypes); var file = Path.Combine(work, "names-plan.json");
                Require(Plan(world, work, id, revision, file, false).Exit == 3 && !File.Exists(file), "ordinary literal authority continues to refuse identifying-name changes");
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                using var preview = Success(Plan(world, work, id, revision, file, true)); var plan = PatchPlanJson.Read(File.ReadAllText(file));
                Require(plan.Operations.OfType<ReferencePatch>().Count() == (bothTypes ? 2 : 1) && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "preview creates one complete operation per typed old/new name group without touching originals");
                ApplyAndSeal(world, work, id, revision, file, plan);
                var region = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, WorldReferenceTests.Entities)));
                foreach (var index in duplicate ? new[] { 0, 1 } : new[] { 0 })
                {
                    var root = region.ReadChunk(index).Document.Root;
                    string Name(int entity) => ((NbtString)NbtLocator.Resolve(root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(entity), PathSegment.ForKey("CustomName")])).Value!).Value;
                    Require(Name(0) == "{\"text\":\"Sentinel\",\"color\":\"red\"}" && Name(1) == (bothTypes ? "{\"text\":\"Undead\"}" : "{\"text\":\"Guard\"}"), "typed name changes preserve styling and unselected same-spelled names");
                    Require(NbtLocator.Resolve(root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("UUID")])).Value == new NbtIntArray([1, 2, 3, 4]), "UUID remains outside the rename authorization");
                }
                var command = File.ReadAllText(Path.Combine(world, WorldQueryTests.Function(0)));
                Require(command.Contains("Sentinel", StringComparison.Ordinal) && command.Contains("{\"text\":\"Guard\"}", StringComparison.Ordinal), "the reference changes while identical literal text remains unchanged");
                var terrain = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, "region/r.0.0.mca"))).ReadChunk(0).Document;
                var native = ((NbtString)NbtLocator.Resolve(terrain.Root, new([PathSegment.ForKey("block_entities"), PathSegment.ForIndex(0), PathSegment.ForKey("front_text"), PathSegment.ForKey("messages"), PathSegment.ForIndex(0)])).Value!).Value;
                Require(native.Contains("Sentinel", StringComparison.Ordinal), "the literal-free native selector joins the same physical commit");
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }

    public static void CommandSummonTemplatesAndLiveNamesCommitInOneTranslationPlan()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var function = Path.Combine(world, WorldQueryTests.Function(0));
            File.AppendAllText(function, "summon minecraft:armor_stand ~ ~ ~ {CustomName:'{\"text\":\"Guard\",\"color\":\"gold\"}',ShowArms:1b}\n");
            var before = File.ReadAllBytes(function);
            var (id, revision) = Session(world, work); var output = Path.Combine(work, "name-template-plan.json");
            using var preview = Success(Plan(world, work, id, revision, output, true));
            var plan = PatchPlanJson.Read(File.ReadAllText(output));
            Require(plan.Operations.OfType<ReferencePatch>().Single().Synchronization.Members.Length == 4 && File.ReadAllBytes(function).SequenceEqual(before),
                "one bounded group covers a spawned entity, its exact summon template and both real selectors");
            ApplyAndSeal(world, work, id, revision, output, plan);
            var changed = File.ReadAllText(function);
            Require(changed.Contains("type=minecraft:armor_stand,name=\"Sentinel\"", StringComparison.Ordinal)
                && changed.Contains("summon minecraft:armor_stand ~ ~ ~ {CustomName:", StringComparison.Ordinal)
                && changed.Split('\n').Single(line => line.StartsWith("summon ", StringComparison.Ordinal)).Contains("Sentinel", StringComparison.Ordinal)
                && changed.Contains("ShowArms:1b", StringComparison.Ordinal) && changed.Contains("{\"text\":\"Guard\"}", StringComparison.Ordinal),
                "live selector and spawn template use the translated name while literal chat and unrelated summon data survive");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void StaticClickMessageTranslatesWithoutRewritingItsCommand()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var function = Path.Combine(world, WorldQueryTests.Function(1));
            const string component = """{"text":"Open","clickEvent":{"action":"run_command","value":"/tellraw @p {\"text\":\"Clicked\",\"color\":\"red\"}"}}""";
            var direct = "tellraw @s " + component;
            var book = "give @s written_book[written_book_content={title:'Title',author:'Author',pages:['" + component.Replace("\\", "\\\\", StringComparison.Ordinal) + "']}]";
            File.WriteAllText(function, direct + "\n" + book + "\n");
            var (id, revision) = Session(world, work); var output = Path.Combine(work, "click-message-plan.json");
            using var preview = Success(Plan(world, work, id, revision, output, true));
            ApplyAndSeal(world, work, id, revision, output, PatchPlanJson.Read(File.ReadAllText(output)));
            var lines = File.ReadAllLines(function);
            Require(lines.Length == 2 && lines[0] == direct.Replace("Clicked", "Opened", StringComparison.Ordinal)
                && lines[1].StartsWith("give @s written_book[written_book_content={title:'Title',author:'Author',pages:[", StringComparison.Ordinal),
                "the direct click stays byte exact outside the message; the book retains its command and cover: " + string.Join("\n", lines));
            var page = Snbt.ParseDocument(Encoding.UTF8.GetBytes(lines[1].Split("pages:[", StringSplitOptions.None)[1][..^3]),
                SnbtProfile.JavaThrough1214, DecodeLimits.Default);
            Require(page.Root.Value is LogicalMinecraftLeaf { Value: NbtString value } && value.Value == component.Replace("Clicked", "Opened", StringComparison.Ordinal),
                "the book page preserves its exact decoded component outside the translated message");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MissingMembersFilteredScopeAndUntypedUsesPreserveAllWork()
    {
        foreach (var mode in new[] { "partial", "scope", "untyped" })
        {
            var world = WorldReferenceTests.CreateWorld(mode == "partial"); var work = ScratchDirectory.CreateChild();
            try
            {
                if (mode == "untyped") File.WriteAllText(Path.Combine(world, WorldQueryTests.Function(1)), "execute as @e[name=Guard] run tellraw @s \"untyped\"\n");
                var (id, revision) = Session(world, work, partial: mode == "partial", scoped: mode == "scope"); var file = Path.Combine(work, "refused-plan.json");
                var store = new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions")); var before = store.Read(id)!;
                var original = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                var result = Plan(world, work, id, revision, file, true);
                Require(result.Exit == 3 && !File.Exists(file) && store.Read(id)!.SequenceEqual(before)
                    && original.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "incomplete member/scope/reference authority refuses before publication and retains every validated reply: " + mode + " " + result.Output);
            }
            finally { Directory.Delete(world, true); Directory.Delete(work, true); }
        }
    }
    public static void FragmentedNamesAreGroupedByTheWholeTypedTarget()
    {
        var world = WorldReferenceTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = Path.Combine(world, WorldReferenceTests.Entities); var region = RegionFile.Read(File.ReadAllBytes(file)); var document = region.ReadChunk(0).Document;
            var path = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")]); var previous = NbtLocator.Resolve(document.Root, path).Value!;
            const string component = "{\"text\":\"Guard\",\"extra\":[{\"text\":\" Dog\"}],\"color\":\"red\"}";
            var changed = TypedTreeEdits.Apply(document, [TypedEdit.Replace(path, previous, new NbtString(component))]);
            File.WriteAllBytes(file, region.WithChunk(0, changed, NbtCompression.Zlib, 17));
            var command = File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0))); var (id, revision) = Session(world, work); var planFile = Path.Combine(work, "fragmented-plan.json");
            using var preview = Success(Plan(world, work, id, revision, planFile, true)); var plan = PatchPlanJson.Read(File.ReadAllText(planFile));
            Require(plan.Operations.OfType<ReferencePatch>().Single().Synchronization.Members.Length == 2, "a changed fragment and unchanged sibling form one whole-name group, without guessing references to the fragment alone");
            ApplyAndSeal(world, work, id, revision, planFile, plan);
            var actual = ((NbtString)NbtLocator.Resolve(RegionFile.Read(File.ReadAllBytes(file)).ReadChunk(0).Document.Root, path).Value!).Value;
            Require(actual == component.Replace("Guard", "Sentinel", StringComparison.Ordinal) && File.ReadAllBytes(Path.Combine(world, WorldQueryTests.Function(0))).SequenceEqual(command), "the whole name becomes Sentinel Dog while unrelated Guard selectors/literals remain untouched");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void DistinctOldIdentifiersCannotMergeThroughIndependentGroups()
    {
        var world = MergeWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var (id, revision) = Session(world, work, merge: true);
            var store = new FileSystemTranslationSessionStore(Path.Combine(State(work), "sessions")); var before = store.Read(id)!; var output = Path.Combine(work, "merge-plan.json");
            Require(Plan(world, work, id, revision, output, true).Exit == 3 && !File.Exists(output) && store.Read(id)!.SequenceEqual(before),
                "two independently addressed old identifiers cannot broaden selectors by merging into one final typed name");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    private static string MergeWorld()
    {
        var world = WorldReferenceTests.CreateWorld(true); var file = Path.Combine(world, WorldReferenceTests.Entities); var region = RegionFile.Read(File.ReadAllBytes(file)); var document = region.ReadChunk(1).Document;
        var path = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")]);
        var uuid = new TypedPath([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("UUID")]);
        var changed = TypedTreeEdits.Apply(document, [TypedEdit.Replace(path, NbtLocator.Resolve(document.Root, path).Value!, new NbtString("{\"text\":\"Other\",\"color\":\"red\"}")),
            TypedEdit.Replace(uuid, NbtLocator.Resolve(document.Root, uuid).Value!, new NbtIntArray([5, 6, 7, 8]))]);
        File.WriteAllBytes(file, region.WithChunk(1, changed, NbtCompression.GZip, 18)); return world;
    }
    public static void IndividuallyValidGroupsCannotBypassCombinedPlanValidation()
    {
        var world = MergeWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(world, Path.Combine(work, "cache")); var inventory = new WorldInventory(world, snapshot.Manifest.WorldId); var service = new WorldReferenceService(inventory);
            var names = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.SemanticRole == "armor_stand.name").Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
            Require(names.Length == 2 && names.Select(slot => slot.SourceText).Order(StringComparer.Ordinal).SequenceEqual(new[] { "Guard", "Other" }), "control: two distinct typed old names and UUIDs exist");
            var groups = names.Select((slot, index) => service.Create(snapshot, "group-" + index, [new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Sentinel"))])).ToArray();
            PatchPlan? plan = null; var refused = false;
            try { plan = new WorldPlanBuilder(inventory).Build(snapshot, groups); }
            catch (EditRejectedException) { refused = true; }
            if (!refused)
            {
                var storage = new ArtifactStorage(world, snapshot.Manifest.WorldId); using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan!, true);
                var engine = new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(Path.Combine(work, "objects")), new FileSystemApplyJournalStore(Path.Combine(work, "journals")));
                Require(engine.Apply(prepared, true).Success, "adverse control: the unguarded combined plan commits successfully");
                var region = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, WorldReferenceTests.Entities)));
                Require(new[] { 0, 1 }.All(index => ((NbtString)NbtLocator.Resolve(region.ReadChunk(index).Document.Root,
                    new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")])).Value!).Value.Contains("Sentinel", StringComparison.Ordinal)), "adverse control: distinct target identities now share the new selector name");
            }
            Require(refused, "the common planner must refuse collective typed-name collisions even when each individual group is valid");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void MemberBudgetsApplyAcrossAllGroupsBeforePlanPublication()
    {
        var world = MergeWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = Path.Combine(world, "region/r.0.0.mca"); var region = RegionFile.Read(File.ReadAllBytes(file)); var document = region.ReadChunk(0).Document;
            var path = new TypedPath([PathSegment.ForKey("block_entities")]);
            var signs = Enumerable.Range(0, 8).Select(index => (NbtValue)new NbtCompound([new("id", new NbtString("minecraft:sign")),
                new("front_text", new NbtCompound([new("messages", new NbtList(NbtType.String, [new NbtString(JsonSerializer.Serialize(new { selector = "@e[type=minecraft:armor_stand,name=" + (index < 4 ? "Guard" : "Other") + "]" }))]))]))]));
            File.WriteAllBytes(file, region.WithChunk(0, TypedTreeEdits.Apply(document, [TypedEdit.Replace(path, NbtLocator.Resolve(document.Root, path).Value!, new NbtList(NbtType.Compound, signs))]), NbtCompression.Zlib, 19));
            var limits = new DecodeLimits(MaxDecodedBytes: 65_536); var inventory = new WorldInventory(world, World);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(work, "cache"))).QueryAsync(new(new(SnapshotScope.All, limits))).GetAwaiter().GetResult().Snapshot!;
            Require(snapshot.Manifest.CoverageComplete, "control: the original world is fully readable within the selected budget");
            var names = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.SemanticRole == "armor_stand.name").Select(item => item.ToSlot("en_us", "zh_tw"))
                .Select(slot => new LiteralTranslation(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", new string(slot.SourceText == "Guard" ? 'a' : 'b', 4096)))).ToArray();
            var service = new WorldReferenceService(inventory); var individual = names.Select((name, index) => service.Create(snapshot, "solo-" + index, [name])).ToArray();
            long Cost(ReferencePatch group) => group.Synchronization.Members.Sum(member => TextExtractionBudget.RecordBytes(member.Address, (long)member.ExpectedText.Length + member.ReplacementText.Length));
            Require(individual.Length == 2 && individual.All(group => Cost(group) <= limits.MaxDecodedBytes) && individual.Sum(Cost) > limits.MaxDecodedBytes, "control: each valid group fits, while their combined retained members exceed the budget");
            var refused = 0;
            try { _ = service.CreateGroups(snapshot, "combined", names); } catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { refused++; }
            try { _ = new WorldPlanBuilder(inventory).Build(snapshot, individual); } catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { refused++; }
            Require(refused == 2 && !Directory.EnumerateFiles(world, ".nbtcli-*.candidate", SearchOption.AllDirectories).Any(), "both bulk construction and common validation enforce the aggregate budget before staging");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void RawTypedEditsCannotInvalidateASemanticReferenceGroup()
    {
        var world = MergeWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = WorldQueryTests.Query(world, Path.Combine(work, "cache")); var inventory = new WorldInventory(world, snapshot.Manifest.WorldId);
            var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.SemanticRole == "armor_stand.name").Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
            var first = slots.Single(slot => slot.SourceText == "Guard"); var other = slots.Single(slot => slot.SourceText == "Other");
            var group = new WorldReferenceService(inventory).Create(snapshot, "rename", [new(first, new(first.Id, first.SourceTextHash, "zh_tw", "Sentinel"))]);
            var address = new SourceAddress(other.Address.Artifact, other.Address.NodePath, "typed.value", other.Address.Dimension, other.Address.Chunk);
            var edit = new TypedPatch("raw-name", address, TypedEdit.Replace(address.NodePath, new NbtString("{\"text\":\"Other\",\"color\":\"red\"}"), new NbtString("{\"text\":\"Sentinel\",\"color\":\"red\"}")));
            PatchPlan? plan = null; var refused = false;
            try { plan = new WorldPlanBuilder(inventory).Build(snapshot, [group, edit]); } catch (NotSupportedException) { refused = true; }
            if (!refused)
            {
                var storage = new ArtifactStorage(world, snapshot.Manifest.WorldId); using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan!, true);
                Require(new WorldApplyEngine(inventory, storage, new FileSystemWorldBackupStore(Path.Combine(work, "objects")), new FileSystemApplyJournalStore(Path.Combine(work, "journals"))).Apply(prepared, true).Success,
                    "adverse control: an unguarded raw edit can accompany the reference apply");
                var region = RegionFile.Read(File.ReadAllBytes(Path.Combine(world, WorldReferenceTests.Entities)));
                Require(new[] { 0, 1 }.All(index => ((NbtString)NbtLocator.Resolve(region.ReadChunk(index).Document.Root,
                    new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")])).Value!).Value.Contains("Sentinel", StringComparison.Ordinal)), "adverse control: the raw edit introduces the same widened typed-name collision");
            }
            Require(refused, "raw typed edits and semantic reference synchronization require separate plans until their combined final graph is verified");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}

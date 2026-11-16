using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using Next.Minecraft;
using System.Text;
using static Next.Verification.TextSlotTests;
using static Next.Verification.CandidateIoTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class WorldReferenceSafetyTests
{
    private static readonly TypedPath Name = new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(0), PathSegment.ForKey("CustomName")]);
    private static void Edit(string root, params TypedEdit[] edits)
    {
        var path = Path.Combine(root, WorldReferenceTests.Entities); var region = RegionFile.Read(File.ReadAllBytes(path));
        var source = region.ReadChunk(0).Document; var candidate = TypedTreeEdits.Apply(source, edits);
        File.WriteAllBytes(path, region.WithChunk(0, candidate, NbtCompression.Zlib, region.Chunks[0].Timestamp));
    }
    private static void Replace(string root, TypedPath path, NbtValue value)
    {
        var document = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities))).ReadChunk(0).Document;
        Edit(root, TypedEdit.Replace(path, NbtLocator.Resolve(document.Root, path).Value!, value));
    }
    public static void CompositeNamesUseVisiblePartsWithoutHoverText()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            const string original = "{\"text\":\"Gu\",\"extra\":[{\"text\":\"ard\",\"bold\":true}],\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"Guard\"}}}";
            Replace(root, Name, new NbtString(original));
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var names = WorldReferenceTests.Names(snapshot).Select(item => new LiteralTranslation(item.Slot,
                new(item.Slot.Id, item.Slot.SourceTextHash, "zh_tw", item.Slot.SourceText == "Gu" ? "Sen" : "tinel"))).ToArray();
            Require(names.Length == 2, "only the two actual visible name slots are explicitly selected");
            var operation = new WorldReferenceService(inventory).Create(snapshot, "composite", names);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            Require(WorldApplyTests.Engine(root, state).Apply(prepared, true).Success, "whole visible-name projection drives both real reference kinds");
            var document = RegionFile.Read(File.ReadAllBytes(Path.Combine(root, WorldReferenceTests.Entities))).ReadChunk(0).Document;
            var actual = ((NbtString)NbtLocator.Resolve(document.Root, Name).Value!).Value;
            Require(actual == original.Replace("\"Gu\"", "\"Sen\"", StringComparison.Ordinal).Replace("\"ard\"", "\"tinel\"", StringComparison.Ordinal), "hover text, style and structure remain outside name replacement");
            Require(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(0)))).Contains("name=\"Sentinel\"", StringComparison.Ordinal), "selectors use composed Sentinel, not either leaf or hover value");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void CollisionsAndExecutionContextDoNotWidenGroups()
    {
        foreach (var mode in new[] { "target", "reference", "context" })
        {
            var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
            try
            {
                if (mode == "target")
                {
                    Replace(root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(1), PathSegment.ForKey("id")]), new NbtString("minecraft:armor_stand"));
                    Replace(root, new([PathSegment.ForKey("Entities"), PathSegment.ForIndex(1), PathSegment.ForKey("CustomName")]), new NbtString("{\"text\":\"Sentinel\"}"));
                }
                else Put(root, WorldQueryTests.Function(1), Encoding.UTF8.GetBytes(mode == "reference"
                    ? "execute as @e[type=armor_stand,name=Sentinel] run tellraw @s \"existing\""
                    : "execute as @s[name=Guard] run tellraw @s \"context\""));
                var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache"));
                Reject<EditRejectedException>(() => new WorldReferenceService(new WorldInventory(root, World)).Create(snapshot, "unsafe", WorldReferenceTests.Names(snapshot, false)));
                Require(!Directory.GetFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "collision/context ambiguity rejects instead of selecting similar names");
            }
            finally { Directory.Delete(root, true); Directory.Delete(state, true); }
        }
    }
    private sealed class AddReferenceAfterReplace(string root) : ArtifactFileIo
    {
        private int count;
        public override void Replace(string candidate, string target, bool absent)
        {
            base.Replace(candidate, target, absent);
            if (++count == 1) Put(root, "datapacks/p/data/test/function/added.mcfunction", Encoding.UTF8.GetBytes("execute as @e[type=armor_stand,name=Guard] run tellraw @s \"added\""));
        }
    }
    public static void ANewSourceDuringCommitRequiresRecoveryWithoutFurtherWrites()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var operation = new WorldReferenceService(inventory).Create(snapshot, "rename", WorldReferenceTests.Names(snapshot));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            var storage = new ArtifactStorage(root, World, new AddReferenceAfterReplace(root));
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var receipt = WorldApplyTests.Engine(root, state, storage).Apply(prepared, true);
            Require(!receipt.Success && receipt.Counts[OperationOutcome.RecoveryRequired] == 1, "new relevant source after first replace cannot become complete group success");
            var paths = snapshot.Manifest.InputSources.Where(source => source.ContentHash is not null).Select(source => source.Artifact.RelativePath).ToArray();
            var partial = paths.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(root, path)));
            var recovery = WorldApplyTests.Engine(root, state).Recover(plan.PlanHash, true);
            Require(!recovery.Success && partial.All(pair => File.ReadAllBytes(Path.Combine(root, pair.Key)).SequenceEqual(pair.Value)), "recovery preserves the known partial state and the unknown new source without continuing stale writes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    private sealed class ChangeReferenceAfterReplace(string root) : ArtifactFileIo
    {
        internal int Replacements { get; private set; }
        public override void Replace(string candidate, string target, bool absent)
        {
            base.Replace(candidate, target, absent);
            if (++Replacements == 1) Put(root, WorldQueryTests.Function(1), Encoding.UTF8.GetBytes("execute as @e[type=armor_stand,name=Guard] run tellraw @s \"changed\""));
        }
    }
    public static void ChangedExistingDependenciesStopTheNextReferenceWrite()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var operation = new WorldReferenceService(inventory).Create(snapshot, "rename", WorldReferenceTests.Names(snapshot));
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            var io = new ChangeReferenceAfterReplace(root); var storage = new ArtifactStorage(root, World, io);
            using var prepared = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true);
            var receipt = WorldApplyTests.Engine(root, state, storage).Apply(prepared, true);
            Require(!receipt.Success && receipt.Counts[OperationOutcome.Conflict] == 1, "a changed existing dependency is a third image, never group success");
            Require(io.Replacements == 1, "after the first replace changes a reference dependency, no further group output may commit");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void SnapshotDriftAndPartialScopeRejectBeforePlanning()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            Put(root, "datapacks/p/data/test/function/added.mcfunction", Encoding.UTF8.GetBytes("tellraw @s \"new source\""));
            Reject<ArtifactConflictException>(() => new WorldReferenceService(inventory).Create(snapshot, "stale", WorldReferenceTests.Names(snapshot)));
            var scoped = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "scoped"))).QueryAsync(new(new(new([], [], [], [WorldReferenceTests.Entities])))).GetAwaiter().GetResult().Snapshot!;
            Require(scoped.Manifest.CoverageComplete, "scoped fixture is complete within its limited extraction scope");
            Reject<EditRejectedException>(() => new WorldReferenceService(inventory).Create(scoped, "limited", WorldReferenceTests.Names(scoped)));
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void UnicodeQuotedNamesAndNegatedReferencesRoundTrip()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            const string translated = "隊長 \"Ω\\x";
            Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes("execute as @e[type=armor_stand,name=Guard] at @e[type=armor_stand,name=!Guard] run tellraw @s {\"text\":\"Guard\"}"));
            var snapshot = LegacyImportMappingTests.Query(root, Path.Combine(state, "cache")); var inventory = new WorldInventory(root, World);
            var names = WorldReferenceTests.Names(snapshot).Select(item => new LiteralTranslation(item.Slot, new(item.Slot.Id, item.Slot.SourceTextHash, "zh_tw", translated)));
            var operation = new WorldReferenceService(inventory).Create(snapshot, "unicode", names); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            Require(WorldApplyTests.Engine(root, state).Apply(prepared, true).Success, "Unicode quoted identifier commits as one verified group");
            var function = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(0))));
            var result = CommandTextExtractor.Extract(new(new(new(World, WorldQueryTests.Function(0), ArtifactKind.McFunction), TypedPath.Root, "command.function"), function), MinecraftProfiles.ForDataVersion(3953)!);
            Require(result.Complete && result.References.Count(reference => reference.Name == translated) == 2 && result.References.Count(reference => reference.Negated) == 1
                && result.Text.Occurrences.Single().Text == "Guard", "decoded names, negation and unrelated literal text all survive real readback");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void ProposedNameOutputRespectsTheSnapshotBudget()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                .QueryAsync(new(new(SnapshotScope.All, new(MaxDecodedBytes: 8192)))).GetAwaiter().GetResult().Snapshot!;
            Require(snapshot.Manifest.CoverageComplete, "the small original world is valid within the explicit budget");
            var names = WorldReferenceTests.Names(snapshot).Select(item => new LiteralTranslation(item.Slot,
                new(item.Slot.Id, item.Slot.SourceTextHash, "zh_tw", new string('x', 20_000))));
            try { _ = new WorldReferenceService(inventory).Create(snapshot, "oversized", names); }
            catch (CodecException error) when (error.Code == CodecError.LimitExceeded) { return; }
            throw new InvalidOperationException("reference proposal must bound replacement and member buffers before constructing the full group");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void LazyNameInputsStopAtTheAggregateBudget()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                .QueryAsync(new(new(SnapshotScope.All, new(MaxDecodedBytes: 8192)))).GetAwaiter().GetResult().Snapshot!;
            Require(snapshot.Manifest.CoverageComplete, "the input budget fixture has complete original sources");
            var consumed = 0;
            IEnumerable<LiteralTranslation> Inputs()
            {
                for (var index = 0; index < 100; index++)
                {
                    var address = new SourceAddress(new(World, WorldReferenceTests.Entities, ArtifactKind.Entities), new([PathSegment.ForIndex(index)]), "entity.name", "minecraft:overworld", new(0, 0));
                    var slot = new TextSlot(address, "Guard", "entity.name", "en_us", "zh_tw", true, PlaceholderPolicy.None);
                    consumed++;
                    yield return new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", new string('x', 3000)));
                }
            }
            try { _ = new WorldReferenceService(inventory).Create(snapshot, "lazy-budget", Inputs()); }
            catch (CodecException error) when (error.Code == CodecError.LimitExceeded)
            {
                Require(consumed is >= 2 and < 100, "individually valid replies stop enumeration as soon as their total retention exceeds the budget");
                return;
            }
            catch (EditRejectedException) { throw new InvalidOperationException($"aggregate input budget must reject before live target validation; consumed={consumed}/100"); }
            throw new InvalidOperationException($"aggregate input budget did not reject; consumed={consumed}/100");
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
    public static void LazyNameAddressesAlsoConsumeTheRetentionBudget()
    {
        var root = WorldReferenceTests.CreateWorld(); var state = ScratchDirectory.CreateChild();
        try
        {
            var inventory = new WorldInventory(root, World);
            var snapshot = new WorldQueryService(inventory, new FileSystemScanCacheStore(Path.Combine(state, "cache")))
                .QueryAsync(new(new(SnapshotScope.All, new(MaxDecodedBytes: 8192)))).GetAwaiter().GetResult().Snapshot!;
            Require(snapshot.Manifest.CoverageComplete, "the address-budget fixture has complete bounded source data");
            foreach (var field in new[] { "node", "artifact", "entry", "role", "dimension", "component", "grammar", "syntax", "encoding" })
            {
                var consumed = 0; var disposed = false;
                IEnumerable<LiteralTranslation> Inputs()
                {
                    try
                    {
                        for (var index = 0; index < 100; index++)
                        {
                            var large = new string('x', 1500) + index;
                            var artifact = field == "entry" ? new ArtifactAddress(World, "datapacks/test.zip", ArtifactKind.Archive, large + ".json")
                                : new ArtifactAddress(World, field == "artifact" ? large + ".dat" : "data/test.dat", ArtifactKind.Nbt);
                            var path = new TypedPath([PathSegment.ForKey(field == "node" ? large : "name" + index)]);
                            var syntax = field is "grammar" or "syntax" or "encoding" ? new SyntaxLocator(field == "grammar" ? large : "grammar", 1, 0, 1, new string('0', 64), field == "encoding" ? large : "utf8", "none", field == "syntax" ? new([PathSegment.ForKey(large)]) : null) : null;
                            var address = new SourceAddress(artifact, path, field == "role" ? large : "entity.name", field == "dimension" ? large : null,
                                componentPath: field == "component" ? new([PathSegment.ForKey(large)]) : null, syntax: syntax);
                            var slot = new TextSlot(address, "Guard", "entity.name", "en_us", "zh_tw", true, PlaceholderPolicy.None);
                            consumed++; yield return new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Sentinel"));
                        }
                    }
                    finally { disposed = true; }
                }
                try { _ = new WorldReferenceService(inventory).Create(snapshot, "address-budget", Inputs()); }
                catch (CodecException error) when (error.Code == CodecError.LimitExceeded)
                {
                    Require(consumed is >= 1 and <= 3 && disposed, $"{field} strings and retained identity bytes must stop within 8192 bytes; consumed={consumed}/100");
                    continue;
                }
                catch (EditRejectedException) { throw new InvalidOperationException($"{field} address budget must reject before live validation; consumed={consumed}/100"); }
                throw new InvalidOperationException($"{field} address budget did not reject; consumed={consumed}/100");
            }
        }
        finally { Directory.Delete(root, true); Directory.Delete(state, true); }
    }
}

using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class StorageAndDataTextTests
{
    internal static readonly VerificationCase[] Cases =
    [
        new("text-storage-explicit-rules-and-candidates", "discovery", _ => ExplicitStorageRulesAndUnknownCandidates()),
        new("command-storage-display-modes", "discovery", _ => CommandStorageDisplayModes()),
        new("text-namespaced-data-json-literal-artifact", "discovery", _ => NamespacedDataJsonKeepsLiteralArtifact())
    ];

    private static TypedPath StoragePath(params string[] keys) => new(new[] { "data", "contents" }.Concat(keys).Select(PathSegment.ForKey));
    public static void ExplicitStorageRulesAndUnknownCandidates()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var contents = C(E("golden:test", C(E("value", S("Literal")), E("message", Text("Component", modern)), E("internal", S("Unknown")), E("number", new NbtInt(7)))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", C(E("contents", contents)))));
            var source = new WorldSourceLocation(new(World, modern ? "data/minecraft/command_storage_golden.dat" : "data/command_storage_golden.dat", ArtifactKind.Nbt), WorldSourceRole.SavedData);
            var unknown = NbtTextExtractor.Extract(source, document);
            Require(!unknown.Complete && unknown.Occurrences.IsEmpty && unknown.Unclassified.Length == 3
                && unknown.Unclassified.All(candidate => candidate.Address.Artifact == source.Artifact && candidate.Address.SemanticRole == "command_storage.candidate"), "arbitrary storage strings are exact unclassified candidates, never automatically authorized slots");
            StorageTextRule[] rules = [new(StoragePath("golden:test", "value"), StorageTextKind.Literal), new(StoragePath("golden:test", "message"), StorageTextKind.TextComponent)];
            var partial = NbtTextExtractor.Extract(source, document, storageRules: rules);
            var component = modern ? "/data/contents/golden:test/message/text|<physical>|" : "/data/contents/golden:test/message|/text|";
            Require(!partial.Complete && partial.Unclassified.Single().Text == "Unknown" && partial.Occurrences.Select(Row).Order(StringComparer.Ordinal).SequenceEqual(new[] {
                "/data/contents/golden:test/value|<physical>|command_storage.literal|Literal", component + "command_storage.component|Component" }.Order(StringComparer.Ordinal)), "only exact explicit rules authorize literal/value or component paths; unknowns remain separate");
            var complete = NbtTextExtractor.Extract(source, document, storageRules: [.. rules, new(StoragePath("golden:test", "internal"), StorageTextKind.Literal)]);
            Require(complete.Complete && complete.Unclassified.IsEmpty && complete.Occurrences.Length == 3, "complete explicit rule coverage can resolve storage candidates");
            Reject<ArgumentException>(() => NbtTextExtractor.Extract(source, document, storageRules: [new(TypedPath.Root, StorageTextKind.Literal)]));
            Reject<ArgumentException>(() => NbtTextExtractor.Extract(source, document, storageRules: [new(StoragePath("golden:test"), StorageTextKind.TextComponent), rules[0]]));
        }
    }
    public static void NamespacedDataJsonKeepsLiteralArtifact()
    {
        var world = WorldMetadata.Read(WorldMetadataTests.Level(4774), WorldMetadataTests.Separate(4774, WorldMetadataTests.Dimensions));
        var source = new WorldSourceLocation(new(World, "data/minecraft/bossbars/example.json", ArtifactKind.Json), WorldSourceRole.SavedData);
        var bytes = Encoding.UTF8.GetBytes("{\"Name\":{\"text\":\"Boss\",\"color\":\"red\"},\"Value\":10,\"Players\":[\"Protected\"],\"value\":\"Protected\"}");
        var result = DataTextExtractor.Json(source, bytes, world);
        Require(result.Complete && Row(result.Occurrences.Single()) == "/Name/text|<physical>|bossbar.name|Boss" && result.Occurrences.Single().Address.Artifact == source.Artifact,
            "namespaced data JSON points to actual path once and authorizes only known Name component");
        var wrong = source with { Artifact = new(World, "data/other/data/minecraft/bossbars/example.json", ArtifactKind.Json) };
        var invalid = DataTextExtractor.Json(wrong, bytes, world);
        Require(!invalid.Complete && invalid.Occurrences.IsEmpty, "embedded path segment cannot impersonate declared world-data root");
    }

    private static void CommandStorageDisplayModes()
    {
        var binding = new SourceAddress(new(World, "show.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        foreach (var (version, parsed, raw) in new[]
        {
            (3465, """{"nbt":"lines[]","storage":"messages","interpret":true}""", """{"nbt":"lines[]","storage":"messages"}"""),
            (4774, "{nbt:'lines[]',storage:'messages',interpret:true}", "{nbt:'lines[]',storage:'messages',interpret:false}")
        })
        {
            var uses = new List<CommandStorageAccess>();
            var result = CommandTextExtractor.Extract(new(binding, $"tellraw @a {parsed}\ntellraw @a {raw}"), MinecraftProfiles.ForDataVersion(version)!, storageAccess: uses.Add);
            Require(uses.Select(use => (use.Storage, use.Path, use.Use)).SequenceEqual(new[] {
                ("minecraft:messages", "lines[]", CommandStorageUse.InterpretedDisplay), ("minecraft:messages", "lines[]", CommandStorageUse.LiteralDisplay) }),
                "the original component grammar reports normalized storage reads and separates parsed and raw modes");
            Require(!result.Complete && result.Text.Occurrences.IsEmpty, "observing a display address does not certify dynamic data or create an editable slot");
        }

        const string writtenBook = """give @s written_book{title:'Guide',author:'Author',pages:['[{"text":"Intro"},{"nbt":"lines[]","storage":"demo:messages","interpret":true}]'],custom:{opaque:'{"nbt":"secret[]","storage":"demo:private","interpret":true}'}}""";
        var bookUses = new List<CommandStorageAccess>();
        var book = CommandTextExtractor.Extract(new(binding, writtenBook), MinecraftProfiles.ForDataVersion(3465)!, storageAccess: bookUses.Add);
        Require(bookUses.Count == 1 && bookUses[0] is { Storage: "demo:messages", Path: "lines[]", Use: CommandStorageUse.InterpretedDisplay }
            && !book.Complete, "only typed book page components supply storage observations, while dynamic reference coverage remains unresolved");

        var operands = new List<CommandStorageAccess>();
        _ = CommandTextExtractor.Extract(new(binding, """
            say storage demo:message
            data modify storage demo:target lines set from storage demo:source lines
            execute store result storage demo:count value int 1 run data get storage demo:raw lines[]
            data modify storage demo:root_copy value set from storage demo:entire_source
            data modify storage demo:extra_target value set from storage demo:extra_src path trailing
            data remove storage demo:missing_path
            """), MinecraftProfiles.ForDataVersion(3465)!, storageAccess: operands.Add);
        Require(operands.All(item => item.Use == CommandStorageUse.Other)
            && operands.Select(item => item.Storage).SequenceEqual(new[] { "demo:target", "demo:source", "demo:count", "demo:raw", "demo:root_copy", "demo:entire_source" }),
            "storage observations only use grammar operand positions and never arbitrary command message words");
        const string macroCall = """
            function demo:show with storage demo:root_arguments
            function demo:show with storage demo:nested_arguments message
            function demo:show with storage demo:extra_arguments message trailing
            """;
        foreach (var (version, supported) in new[] { (3465, false), (3566, false), (3567, true), (3578, true), (3700, true), (5130, true) })
            CheckMacroStorage(MinecraftProfiles.ForDataVersion(version)!, supported);
        foreach (var format in new[] { 15, 16, 18 })
            CheckMacroStorage(MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, format, new(format, 0), new(format, 0)))!, format >= 16);

        void CheckMacroStorage(CapabilityProfile profile, bool supported)
        {
            operands.Clear();
            _ = CommandTextExtractor.Extract(new(binding, macroCall), profile, storageAccess: operands.Add);
            string[] expected = supported ? ["demo:root_arguments", "demo:nested_arguments"] : [];
            Require(operands.Select(item => item.Storage).SequenceEqual(expected),
                $"macro storage observations are constrained by their actual grammar boundary: {profile.Id}");
        }

        var world = ScratchDirectory.CreateChild();
        try
        {
            const string path = "datapacks/p/data/demo/functions/producer.mcfunction";
            const string payload = """'{"text":"Welcome"}'""";
            var level = WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions);
            Put(world, "level.dat", CompressedNbt.WriteDocument(level, NbtCompression.GZip));
            Put(world, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Test"}}"""u8.ToArray());
            var bytes = Encoding.UTF8.GetBytes($"execute if data storage demo:state ready run data modify storage messages lines append value {payload}\r\n");
            Put(world, path, bytes);
            const string entry = "data/demo/functions/reader.mcfunction";
            Put(world, "datapacks/reader.zip", Zip(("pack.mcmeta", """{"pack":{"pack_format":15,"description":"Test"}}"""), (entry, writtenBook)));
            var uses = new List<CommandStorageAccess>();
            var rows = new WorldTextExtraction(new WorldInventory(world, World), new(SnapshotScope.All)).ReadSources(uses.Add).ToArray();
            var writer = uses.Single(use => use.Use == CommandStorageUse.ListElementWrite);
            var other = uses.Single(use => use.Use == CommandStorageUse.Other);
            var reader = uses.Single(use => use.Use == CommandStorageUse.InterpretedDisplay);
            var syntax = writer.Address.Syntax!;
            var otherSyntax = other.Address.Syntax!;
            Require(writer.Storage == "minecraft:messages" && writer.Path == "lines" && writer.Address.Artifact.RelativePath == path
                && Encoding.UTF8.GetString(bytes.AsSpan((int)syntax.StartByte, (int)syntax.LengthBytes)) == payload,
                "a list write binds only its literal value and preserves the function address");
            Require(other.Storage == "demo:state"
                && Encoding.UTF8.GetString(bytes.AsSpan((int)otherSyntax.StartByte, (int)otherSyntax.LengthBytes)) == "demo:state"
                && rows.Single(row => row.Source.Location.Artifact.RelativePath == path).Outcome == SourceOutcome.Unsupported,
                "an additional storage operand is observed without promoting incomplete reference coverage");
            Require(reader.Address.Artifact is { RelativePath: "datapacks/reader.zip", ContainerEntry: entry },
                "archived command facts retain their physical artifact and exact member");
        }
        finally { Directory.Delete(world, true); }
    }
}

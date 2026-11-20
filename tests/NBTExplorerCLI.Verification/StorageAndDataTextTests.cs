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
        new("command-filtered-item-tag-data-controls", "discovery", _ => FilteredItemTagDataControls()),
        new("command-storage-item-tag-metadata", "discovery", _ => StorageItemTagMetadata()),
        new("command-storage-item-tag-trim-profile", "discovery", _ => StorageTrimRequiresProfile()),
        new("text-storage-item-tag-display-collision", "discovery", _ => StorageCommandProofTests.ItemMetadataIsNotDisplayText()),
        new("command-entity-uuid-widths", "discovery", _ => EntityUuidWidths()),
        new("command-numeric-entity-data-copy", "discovery", _ => NumericEntityDataCopy()),
        new("text-storage-command-display-consumers", "discovery", _ => StorageCommandProofTests.CrossFileDisplayConsumers()),
        new("text-storage-saved-path-alias", "discovery", _ => StorageCommandProofTests.AliasedSeedBeforeScan()),
        new("text-storage-namespaced-seed-path-alias", "discovery", _ => StorageCommandProofTests.AliasedSeedBeforeScan(true)),
        new("text-storage-namespaced-trailing-dot", "discovery", _ => StorageCommandProofTests.NamespacedLogicalAliases(false)),
        new("text-storage-namespaced-trailing-dot-reader", "discovery", _ => StorageCommandProofTests.NamespacedLogicalAliases(true)),
        new("text-storage-world-evidence-budget", "discovery", _ => StorageCommandProofTests.WorldEvidenceBudget()),
        new("prepare-world-storage-role-freshness", "preparation", _ => StorageCommandProofTests.PreparationRevalidatesStorageUses()),
        new("prepare-world-storage-alias-freshness", "preparation", _ => StorageCommandProofTests.AliasedSeedAfterPreparation(false)),
        new("apply-storage-alias-forward-recovery", "apply", _ => StorageCommandProofTests.AliasedSeedAfterPreparation(true)),
        new("prepare-world-storage-late-namespaced-reader", "preparation", _ => StorageCommandProofTests.AliasedSeedAfterPreparation(false, true)),
        new("apply-storage-late-namespaced-reader-forward-recovery", "apply", _ => StorageCommandProofTests.AliasedSeedAfterPreparation(true, true)),
        new("cli-session-write-storage-archive", "cli-session-write", _ => StorageCommandProofTests.CliArchiveSession()),
        new("prepare-world-storage-list-replacements", "preparation", _ => StorageCommandProofTests.WholeListReplacements()),
        new("prepare-world-storage-saved-list", "preparation", _ => StorageCommandProofTests.SavedDisplayList()),
        new("text-storage-native-seed-with-element-writes", "discovery", _ => StorageCommandProofTests.NativeSeedElementWrites()),
        new("text-storage-seed-runtime-component-schema", "discovery", _ => StorageCommandProofTests.NativeSeedRuntimeSchema()),
        new("command-copy-sign-text-source", "discovery", _ => CopySignTextSource()),
        new("command-storage-structural-metadata-filters", "discovery", _ => StorageMetadataFilters()),
        new("text-storage-component-list-copy-chain", "discovery", _ => ComponentListCopyChain()),
        new("text-namespaced-data-json-literal-artifact", "discovery", _ => NamespacedDataJsonKeepsLiteralArtifact())
    ];

    private static void CopySignTextSource()
    {
        var address = new SourceAddress(new(World, "sign-transfer.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var uses = new List<CommandStorageAccess>();
        const string script = """
            setblock 0 65 0 oak_sign{front_text:{messages:['{"text":"Name"}','{"text":"First"}','{"text":"Second"}','{"text":"Fourth"}']}}
            data modify storage demo:display name set from block 0 65 0 front_text.messages[0]
            data modify storage demo:display lore append from block 0 65 0 front_text.messages[-1]
            execute if entity @e[name=Guard] run data modify storage demo:display lore prepend from block 0 65 0 back_text.filtered_messages[]
            data modify storage demo:display lore set from block 0 65 0 front_text.messages
            """;
        var result = CommandTextExtractor.Extract(new(address, script), profile, storageAccess: uses.Add);
        Require(result.Complete && result.Text.Occurrences.Select(row => row.Text).SequenceEqual(new[] { "Name", "First", "Second", "Fourth" })
            && result.References.Single().Name == "Guard", "verified sign sources keep writable text at its original field and preserve adjacent selector references");
        Require(uses.Count == 4 && uses.All(use => use.Use == CommandStorageUse.Other), "copy sources do not imply exclusive interpreted storage authority");
        foreach (var source in new[] { "block 0 65 0 front_text.messages[4]", "block 0 65 0 front_text.messages[0].extra",
            "block 0 65 0 front_text.private", "entity @s front_text.messages[0]", "block 0 65 0 front_text.messages[{text:Hidden}]" })
            Require(!CommandTextExtractor.Extract(new(address, "data modify storage demo:display name set from " + source), profile).Complete,
                $"unproven sign source is still unresolved: {source}");
        foreach (var destination in new[] { "entity @e[type=armor_stand,tag=copy_target,limit=1] CustomName",
            "block 2 65 0 Items[0].tag.display.Name" })
            Require(!CommandTextExtractor.Extract(new(address, "data modify " + destination + " set from block 0 65 0 front_text.messages[0]"), profile)
                .Complete, $"sign text cannot create an unproven destination binding: {destination}");
        const string oldCopy = "data modify storage demo:display name set from block 0 65 0 Text1";
        Require(CommandTextExtractor.Extract(new(address, oldCopy), profile, runtimeDataVersion: 2205).Complete
            && !CommandTextExtractor.Extract(new(address, oldCopy), profile, runtimeDataVersion: 2204).Complete,
            "classic JSON sign fields are recognized once the runtime provides storage");
    }

    private static void StorageMetadataFilters()
    {
        var address = new SourceAddress(new(World, "filters.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var uses = new List<CommandStorageAccess>();
        const string script = """
            data remove storage demo:inventory items[{id:"minecraft:elytra",Slot:1b}]
            execute if data storage demo:inventory items[{Slot:0b,id:"minecraft:stick"}] run tellraw @a[name=Guard] {"text":"Found"}
            execute unless data storage demo:equipment attributes[{AttributeName:"minecraft:generic.armor"}] run tellraw @a {"text":"No armor"}
            data modify storage demo:inventory items[{id:"minecraft:stick",Slot:0b}].Count set value 2b
            """;
        var result = CommandTextExtractor.Extract(new(address, script), profile, storageAccess: uses.Add);
        Require(result.Complete && result.Text.Occurrences.Select(item => item.Text).SequenceEqual(new[] { "Found", "No armor" })
            && result.References.Single().Name == "Guard", "storage metadata predicates preserve nested text and keep exact name references");
        Require(uses.Count == 4 && uses.All(use => use.Use == CommandStorageUse.Other), "metadata predicates do not establish a display role");
        foreach (var filter in new[] { """{id:"Not an id",Slot:1b}""", """{id:"minecraft:elytra",Slot:"head"}""",
            """{id:"minecraft:elytra",Slot:1b,Name:"Visible"}""", """{AttributeName:"minecraft:generic.armor",Name:"Visible"}""",
            """{id:"minecraft:elytra",id:"minecraft:stick"}""" })
            Require(!CommandTextExtractor.Extract(new(address, "data remove storage demo:inventory items[" + filter + "]"), profile).Complete,
                $"unproven metadata stays unresolved: {filter}");
        const string command = """data remove storage demo:inventory items[{id:"minecraft:elytra",Slot:1b}]""";
        Require(!CommandTextExtractor.Extract(new(address, command), profile, runtimeDataVersion: 2204).Complete,
            "storage metadata is unavailable before the runtime supports storage");
    }

    private static void ComponentListCopyChain()
    {
        var root = ScratchDirectory.CreateChild();
        var cache = ScratchDirectory.CreateChild();
        const string folder = "datapacks/p/data/demo/functions/";
        const string producer = folder + "producer.mcfunction";
        const string copy = folder + "copy.mcfunction";
        const string reader = folder + "reader.mcfunction";
        const string commands = """
            data modify storage demo:messages interim set from storage demo:messages staged
            data modify storage demo:messages lines append from storage demo:messages interim[]
            """;
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(3465, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "datapacks/p/pack.mcmeta", """{"pack":{"pack_format":15,"description":"Storage commands"}}"""u8.ToArray());
            Put(root, producer, """data modify storage demo:messages staged set value ['{"text":"First"}','{"text":"Second"}']"""u8.ToArray());
            Put(root, copy, Encoding.UTF8.GetBytes(commands));
            Put(root, reader, """tellraw @a {"storage":"demo:messages","nbt":"lines[]","interpret":true}"""u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            Require(snapshot.Manifest.CoverageComplete && Slots(snapshot).SequenceEqual(new[] { "First", "Second" }),
                "a complete typed copy chain retains the original producer's text, including across sibling paths");
            Put(root, copy, Encoding.UTF8.GetBytes(commands + "\ndata get storage demo:messages interim[0]"));
            snapshot = WorldQueryTests.Query(root, cache);
            Require(!snapshot.Manifest.CoverageComplete && !Slots(snapshot).Any(),
                "an overlapping raw read of any intermediate must revoke the unchanged producer's inferred slots");
            Put(root, copy, Encoding.UTF8.GetBytes(commands + "\ndata modify storage demo:messages unused set from storage demo:messages interim"));
            snapshot = WorldQueryTests.Query(root, cache);
            Require(!snapshot.Manifest.CoverageComplete && !Slots(snapshot).Any(),
                "copying to a path with undetermined uses cannot authorize interpreting the original producer");
            Put(root, copy, Encoding.UTF8.GetBytes("execute store success score #saved demo run " + commands));
            snapshot = WorldQueryTests.Query(root, cache);
            Require(!snapshot.Manifest.CoverageComplete && !Slots(snapshot).Any(),
                "copying with an observed command result must remain unresolved");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        static IEnumerable<string> Slots(WorldQuerySnapshot snapshot)
            => snapshot.ReadSources().Single(row => row.Source.Location.Artifact.RelativePath == producer).Text!.Occurrences.Select(item => item.Text);
    }

    private static void EntityUuidWidths()
    {
        var address = new SourceAddress(new(World, "entities.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var script = "data get entity a-0-0-0-8 Health\ndata get entity 0000000a-0000-0000-0000-000000000008 Health";
        var result = CommandTextExtractor.Extract(new(address, script), profile);
        Require(result.Complete && result.Text.Occurrences.IsEmpty && result.References.IsEmpty, "canonical and shorter Java UUID fields name the same kind of nontext entity target");
        foreach (var id in new[] { "1-0-0-0", "1-g-0-0-8", "1--0-0-8" })
            Require(!CommandTextExtractor.Extract(new(address, "data get entity " + id + " Health"), profile).Complete, "invalid UUID shapes remain unresolved");
    }

    private static void NumericEntityDataCopy()
    {
        const string script = """
            data modify entity @e[type=pig,limit=1] Health set from entity @e[type=pig,limit=1,sort=furthest] Health
            data modify entity @e[type=wolf,limit=1] AngryAt set from entity @p UUID
            tellraw @a {"text":"Visible"}
            """;
        var address = new SourceAddress(new(World, "entities.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var result = CommandTextExtractor.Extract(new(address, script), profile);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Visible", "typed numeric entity state copies keep adjacent text without adding editable values");
        const string unknown = "data modify entity @e[type=pig,limit=1] CustomName set from entity @p CustomName";
        Require(!CommandTextExtractor.Extract(new(address, unknown), profile).Complete, "copying arbitrary text data requires its own semantic proof");
    }

    private static void FilteredItemTagDataControls()
    {
        const string script = """
            data get storage demo:data item.tag.Enchantments[{id:"minecraft:sharpness"}].lvl
            data remove storage demo:data item.tag.StoredEnchantments[{id:"minecraft:mending"}]
            execute store result storage demo:data item.tag.Enchantments[{id:"minecraft:sharpness"}].lvl short 1 run scoreboard players get #power stats
            execute if data storage demo:data item.tag.Enchantments[{id:"minecraft:sharpness"}].lvl run tellraw @a[name=Guide] {"text":"Visible"}
            """;
        var binding = new SourceAddress(new(World, "controls.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var uses = new List<CommandStorageAccess>();
        var result = CommandTextExtractor.Extract(new(binding, script), profile, storageAccess: uses.Add);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Visible" && result.References.Single().Name == "Guide",
            "filtered item tag controls preserve subsequent text and exact selector references");
        Require(uses.Count == 4 && uses.All(access => access.Use == CommandStorageUse.Other), "metadata filters do not authorize changing ordinary storage values");
        foreach (var path in new[] { "item.tag.Enchantments[{id:\"Readable label\"}]", "item.tag.Enchantments[{id:\"minecraft:sharpness\",Name:\"Label\"}]",
            "item.rawtag.Enchantments[{id:\"minecraft:sharpness\"}]", "item.tag.Enchantments[{Name:\"Label\"}]" })
        {
            var unresolved = CommandTextExtractor.Extract(new(binding, "data get storage demo:data " + path), profile);
            Require(!unresolved.Complete && unresolved.Text.Occurrences.IsEmpty, "unknown filter values and locations remain unresolved");
        }
    }

    private static void StorageItemTagMetadata()
    {
        const string script = """
            data modify storage demo:items stack.tag.Enchantments append value {id:"minecraft:sharpness",lvl:1s}
            data modify storage demo:items stack.tag.StoredEnchantments prepend value {id:"minecraft:mending",lvl:1s}
            data modify storage demo:items stack.tag.AttributeModifiers append value {AttributeName:"minecraft:generic.attack_damage",Name:"minecraft:generic.attack_damage",Amount:1,Operation:0,UUID:[I;1,2,3,4],Slot:"mainhand"}
            execute if data storage demo:items stack.tag.AttributeModifiers[{Slot:"mainhand"}] run tellraw @a {"text":"Equipped"}
            data remove storage demo:items stack.tag.AttributeModifiers[{Name:"minecraft:generic.attack_damage",Operation:0}]
            """;
        var address = new SourceAddress(new(World, "item-controls.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var uses = new List<CommandStorageAccess>();
        var result = CommandTextExtractor.Extract(new(address, script), profile, storageAccess: uses.Add);
        Require(result.Complete && result.Text.Occurrences.Single().Text == "Equipped", "standard item metadata does not block adjacent visible text");
        Require(uses.Count == 5 && uses.All(use => use.Use == CommandStorageUse.Other), "known item metadata remains visible without authorizing display inference");

        foreach (var (path, operation) in new[]
        {
            ("stack.extra.Enchantments", "append value {id:'minecraft:sharpness',lvl:1s}"),
            ("stack.tag.Enchantments", "append value {id:'minecraft:sharpness',lvl:1s,Note:'Readable'}"),
            ("stack.tag.Trim", "set value {material:'minecraft:lapis',pattern:'Custom text'}"),
            ("stack.tag.AttributeModifiers", "append value {Name:'Custom text',Amount:1}"),
        })
        {
            var unresolved = CommandTextExtractor.Extract(new(address, $"data modify storage demo:items {path} {operation}"), profile);
            Require(!unresolved.Complete && unresolved.Text.Occurrences.IsEmpty, "undetermined locations or payloads stay unresolved");
        }
    }

    private static void StorageTrimRequiresProfile()
    {
        var address = new SourceAddress(new(World, "trim.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        const string command = """data modify storage demo:items stack.tag.Trim set value {material:"minecraft:lapis",pattern:"minecraft:silence"}""";
        foreach (var profile in new[] { MinecraftProfiles.ForDataVersion(1631)!, MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, 6, new(6, 0), new(6, 0)))! })
        {
            var result = CommandTextExtractor.Extract(new(address, command), profile);
            Require(!result.Complete && result.Text.Diagnostics.Single().Code == "UnknownDataTextSchema", "an earlier command profile cannot infer the later trim schema");
        }
        var tagged = MinecraftProfiles.ForPackMetadata(new(PackSourceKind.Datapack, 13, new(13, 0), new(13, 0)))!;
        foreach (var (version, expected) in new[] { (3441, false), (3442, true), (3465, true), (3818, true), (3819, false) })
        {
            var uses = new List<CommandStorageAccess>();
            var result = CommandTextExtractor.Extract(new(address, command), tagged, storageAccess: uses.Add, runtimeDataVersion: version);
            Require(result.Complete == expected && result.Text.Occurrences.IsEmpty
                && uses is [{ Use: CommandStorageUse.Other, Path: "stack.tag.Trim" }],
                $"runtime {version} must distinguish the standard tagged item trim without inferring a storage display use");
        }
        foreach (var value in new[] { "{material:'minecraft:lapis'}", "{material:'minecraft:lapis',pattern:'minecraft:silence',note:'Display text'}" })
            Require(!CommandTextExtractor.Extract(new(address, command[..command.IndexOf('{')] + value), tagged, runtimeDataVersion: 3442).Complete,
                "missing required IDs or unrelated string fields remain unresolved");
    }

    private static TypedPath StoragePath(params string[] keys) => new(new[] { "data", "contents" }.Concat(keys).Select(PathSegment.ForKey));
    public static void ExplicitStorageRulesAndUnknownCandidates()
    {
        foreach (var (version, modern) in new[] { (3953, false), (4774, true) })
        {
            var contents = C(E("golden:test", C(E("value", S("Literal")), E("message", Text("Component", modern)), E("internal", S("Unknown")), E("number", new NbtInt(7)))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(version)), E("data", C(E("contents", contents)))));
            var source = new WorldSourceLocation(new(World, modern ? "data/golden/command_storage.dat" : "data/command_storage_golden.dat", ArtifactKind.Nbt), WorldSourceRole.SavedData);
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

        const string nativeFlags = """
            tellraw @a {storage:'demo:messages',nbt:'lines[]',interpret:true,plain:true}
            tellraw @a {storage:'demo:messages',nbt:'lines[]',interpret:true,type:'text'}
            tellraw @a {storage:'demo:messages',nbt:'lines[]',interpret:true,type:'nbt',plain:false}
            """;
        var flagUses = new List<CommandStorageAccess>();
        _ = CommandTextExtractor.Extract(new(binding, nativeFlags), MinecraftProfiles.ForDataVersion(5130)!, storageAccess: flagUses.Add);
        Require(flagUses.Count == 3 && flagUses[0].Use != CommandStorageUse.InterpretedDisplay && flagUses[1].Use != CommandStorageUse.InterpretedDisplay
            && flagUses[2].Use == CommandStorageUse.InterpretedDisplay, "invalid native component flags and types cannot prove interpreted display");

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
        Require(operands[0] is { Use: CommandStorageUse.ListCopy, Storage: "demo:target", Path: "lines", CopySource: ("demo:source", "lines") }
            && operands.Skip(1).All(item => item.Use == CommandStorageUse.Other)
            && operands.Skip(1).Select(item => item.Storage).SequenceEqual(new[] { "demo:count", "demo:raw", "demo:root_copy", "demo:entire_source" }),
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

using System.Collections.Immutable;
using System.Text.Json;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.TranslationSessionTests;

namespace Next.Verification;

internal static class SessionGroupingSourceTests
{
    public static void SnbtContainersKeepOrderedMembers()
    {
        var legacy = JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "minecraft:written_book",
            functions = new[] { new { function = "set_nbt", tag = """
                {pages:['{"text":"First","extra":[{"text":"Tail"}]}','{"text":"Second"}']}
                """ } } } } } } });
        Check(26, "loot_tables", legacy, [("book.pages", new[] { "First", "Tail", "Second" })]);
        var modern = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = """
            {id:'minecraft:text_display',text:{text:'Root',extra:[{text:'Tail'}]},Passengers:[{id:'minecraft:text_display',text:{text:'Other',extra:[{text:'OtherTail'}]}}]}
            """ } });
        Check(71, "item_modifier", modern, [("styled.fragments", new[] { "Root", "Tail" }), ("styled.fragments", new[] { "Other", "OtherTail" })]);
        var lists = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = """
            {id:'minecraft:armor_stand',CustomName:[{text:'One'},{text:'Two'}],Passengers:[{id:'minecraft:armor_stand',CustomName:['OtherOne','OtherTwo']}]}
            """ } });
        Check(71, "item_modifier", lists, [("styled.fragments", new[] { "One", "Two" }), ("styled.fragments", new[] { "OtherOne", "OtherTwo" })]);
        var fallbacks = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = """
            {id:'minecraft:armor_stand',CustomName:[{translate:'a',fallback:'One',hover_event:{action:'show_text',value:{translate:'h',fallback:'Hover'}}},{translate:'b',fallback:'Two'}],Passengers:[{id:'minecraft:armor_stand',CustomName:[{translate:'c',fallback:'Other'}]}]}
            """ } });
        Check(71, "item_modifier", fallbacks, [("styled.fragments", new[] { "One", "Hover", "Two" }), ("styled.fragments", new[] { "Other" })]);
        var lore = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = """
            {id:'minecraft:armor_stand',Items:[{id:'minecraft:paper',components:{'minecraft:lore':[{text:'One',extra:[{translate:'a',fallback:'Two',hover_event:{action:'show_text',value:{translate:'h',fallback:'Hover'}}}]},'Next']}}]}
            """ } });
        Check(71, "item_modifier", lore, [("item.lore", new[] { "One", "Two", "Hover", "Next" })]);
        var nested = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = """
            {id:'minecraft:armor_stand',Items:[{id:'minecraft:written_book',components:{'minecraft:written_book_content':{title:'A',author:'A',pages:[{raw:'A1'},{raw:'A2'}]}}},{id:'minecraft:written_book',components:{'minecraft:written_book_content':{title:'B',author:'B',pages:[{raw:'B1'},{raw:'B2'}]}}}]}
            """ } });
        Check(71, "item_modifier", nested, [("book.pages", new[] { "A1", "A2" }), ("book.pages", new[] { "B1", "B2" })], "book.page");
        var commandArtifact = new ArtifactAddress(World, "test.mcfunction", ArtifactKind.McFunction);
        const string command = "summon armor_stand ~ ~ ~ {CustomName:[{text:'One'},{text:'Two'}],Passengers:[{id:'armor_stand',CustomName:'Other'}]}";
        var extractedCommand = CommandTextExtractor.Extract(new(new(commandArtifact, TypedPath.Root, "command.function"), command), MinecraftProfiles.ForDataVersion(4325)!);
        Require(extractedCommand.Complete, "command SNBT component boundary is completely recognized");
        CheckSlots(commandArtifact, Encoding.UTF8.GetBytes(command), extractedCommand.Text.Occurrences.Select(item => item.ToSlot("en_us", "zh_tw")).ToImmutableArray(),
            [("styled.fragments", new[] { "One", "Two" }), ("styled.fragments", new[] { "Other" })]);
        var languageArtifact = new ArtifactAddress(World, "packs/p/assets/test/lang/en_us.json", ArtifactKind.Json); var languageBytes = Encoding.UTF8.GetBytes("{\"text\":\"One\",\"extra\":\"Two\"}");
        var language = PackTextExtractor.Extract(new(languageArtifact, WorldSourceRole.Language, PackRoot: "packs/p", PackKind: PackSourceKind.ResourcePack), languageBytes,
            new(PackSourceKind.ResourcePack, 55, new(55, 0), new(55, int.MaxValue)));
        Require(language.Complete, "language entries named text/extra are ordinary scalar values");
        CheckSlots(languageArtifact, languageBytes, language.Occurrences.Select(item => item.ToSlot("en_us", "zh_tw")).ToImmutableArray(),
            [("styled.fragments", new[] { "One" }), ("styled.fragments", new[] { "Two" })]);
        const string loreCommand = "give @s paper[lore=[{text:'First'},'Second']]";
        var commandLore = CommandTextExtractor.Extract(new(new(commandArtifact, TypedPath.Root, "command.function"), loreCommand), MinecraftProfiles.ForDataVersion(4325)!);
        Require(commandLore.Complete, "short item component name lore is recognized by the command grammar");
        CheckSlots(commandArtifact, Encoding.UTF8.GetBytes(loreCommand), commandLore.Text.Occurrences.Select(item => item.ToSlot("en_us", "zh_tw")).ToImmutableArray(), [("item.lore", new[] { "First", "Second" })]);
        var lectern = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["block_entity_data"] = """
            {id:'minecraft:lectern',Book:{id:'written_book',components:{'minecraft:written_book_content':{title:'Title',author:'Author',pages:[{raw:'First'},{raw:'Second'}]}}}}
            """ } });
        Check(71, "item_modifier", lectern, [("book.pages", new[] { "First", "Second" })], "lectern.book.page");
        var sign = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["block_entity_data"] = """
            {id:'minecraft:hanging_sign',front_text:{messages:['First','Second']},back_text:{messages:['Back']}}
            """ } });
        Check(71, "item_modifier", sign, [("sign.front", new[] { "First", "Second" }), ("sign.back", new[] { "Back" })]);
    }
    private static void Check(int format, string folder, byte[] bytes, (string Kind, string[] Texts)[] expected, string? role = null)
    {
        var artifact = new ArtifactAddress(World, "datapacks/p/data/test/" + folder + "/syntax.json", ArtifactKind.Json);
        var source = new WorldSourceLocation(artifact, WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
        var extracted = PackTextExtractor.Extract(source, bytes, new(PackSourceKind.Datapack, format, new(format, 0), new(format, int.MaxValue)));
        Require(extracted.Complete, "real pack SNBT input is recognized and completely extracted: " + format + ":" + string.Join(',', extracted.Diagnostics.Select(item => item.Code)));
        var slots = extracted.Occurrences.Where(item => role is null || item.Address.SemanticRole == role).Select(item => item.ToSlot("en_us", "zh_tw")).ToImmutableArray();
        CheckSlots(artifact, bytes, slots, expected);
    }
    private static void CheckSlots(ArtifactAddress artifact, byte[] bytes, ImmutableArray<TextSlot> slots, (string Kind, string[] Texts)[] expected)
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var request = StartRequest(); var sources = ImmutableArray.Create(new ManifestSource(artifact, Hash256.Compute(bytes), SourceOutcome.Success));
            var snapshot = request.Source with { Sources = sources, SnapshotId = SourceIdentity.Snapshot(World, sources, SnapshotScope.All, request.Source.Versions) };
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(root)); service.Start(request with { Source = snapshot, Slots = slots }, Guid.NewGuid());
            var groups = service.Groups(request.SessionId); var text = slots.ToDictionary(slot => slot.Id.Digest, slot => slot.SourceText);
            Require(groups.Length == expected.Length && expected.All(wanted => groups.Count(group => group.Kind == wanted.Kind && group.Slots.Select(id => text[id]).SequenceEqual(wanted.Texts)) == 1),
                "same SNBT semantic container retains every ordered member without merging different nested containers");
            Require(groups.All(group => group.MemberDigest == SourceIdentity.Members(group.GroupId, group.Slots.Select(id => slots.Single(slot => slot.Id.Digest == id).Id), 0)), "member digest binds the exact ordered source slots");
        }
        finally { Directory.Delete(root, true); }
    }
}

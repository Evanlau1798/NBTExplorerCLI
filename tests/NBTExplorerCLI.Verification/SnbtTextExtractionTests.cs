using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class SnbtTextExtractionTests
{
    private static WorldSourceLocation Source(string folder) => new(new(World, "datapacks/p/data/test/" + folder + "/syntax.json", ArtifactKind.Json),
        WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack);
    public static void LootNbtTagLocations()
    {
        const string tag = """
            {display:{Name:'{"text":"Name雪"}',Lore:['{"text":"Lore"}']},title:'Title',author:'Author',pages:['{"text":"Page"}'],value:'Protected'}
            """;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "minecraft:written_book",
            functions = new[] { new { function = "set_nbt", tag } } } } } } });
        var result = PackTextExtractor.Extract(Source("loot_tables"), bytes, new(PackSourceKind.Datapack, 26, new(26, 0), new(26, int.MaxValue)));
        var expected = new[] { ("Name雪", "item.name", "'{\"text\":\"Name雪\"}'", "/text"), ("Lore", "item.lore", "'{\"text\":\"Lore\"}'", "/text"),
            ("Title", "book.title", "'Title'", (string?)null), ("Author", "book.author", "'Author'", null), ("Page", "book.page", "'{\"text\":\"Page\"}'", "/text") };
        Require(result.Complete && result.Occurrences.Length == expected.Length, "known tagged loot item supplies tag schema and complete exact text set");
        foreach (var (text, role, token, component) in expected)
        {
            var occurrence = result.Occurrences.Single(value => value.Text == text); var address = occurrence.Address; var syntax = address.Syntax;
            Require(address.NodePath.ToJsonPointer() == "/pools/0/entries/0/functions/0/tag" && address.ComponentPath?.ToJsonPointer() == component && address.SemanticRole == role
                && syntax is not null && syntax.GrammarProfile == "java-snbt-through-1.21.4" && syntax.Encoding == "utf8-java-string" && syntax.PreimageHash == Hash256.Compute(Encoding.UTF8.GetBytes(tag)).Hex
                && syntax.StartByte == Encoding.UTF8.GetByteCount(tag[..tag.IndexOf(token, StringComparison.Ordinal)]) && syntax.LengthBytes == Encoding.UTF8.GetByteCount(token)
                && SourceAddressJson.Read(SourceAddressJson.Write(address)) == address, "SNBT leaf span is byte-exact inside decoded outer String; embedded component path stays separate");
        }
    }
    public static void CustomDataSnbtStringAlternative()
    {
        foreach (var (format, modern) in new[] { (48, false), (71, true), (99, true) })
        {
            var token = modern ? "'Line雪'" : "'{\"text\":\"Line雪\"}'";
            var tag = modern ? "{id:'minecraft:sign',front_text:{messages:[{text:'Line雪'}]}}"
                : "{id:'minecraft:sign',front_text:{messages:['{\"text\":\"Line雪\"}']}}";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { [":block_entity_data"] = tag } });
            var result = PackTextExtractor.Extract(Source("item_modifier"), bytes, new(PackSourceKind.Datapack, format, new(format, 0), new(format, int.MaxValue)));
            Require(result.Complete && result.Occurrences.Length == 1, "CustomData accepts its official SNBT String alternative");
            var address = result.Occurrences.Single().Address;
            Require(result.Occurrences.Single().Text == "Line雪" && address.NodePath.ToJsonPointer() == "/components/:block_entity_data"
                && address.ComponentPath?.ToJsonPointer() == (modern ? null : "/text") && address.Syntax!.StartByte == Encoding.UTF8.GetByteCount(tag[..tag.IndexOf(token, StringComparison.Ordinal)])
                && address.Syntax.LengthBytes == Encoding.UTF8.GetByteCount(token), "SNBT/embedded-JSON layers retain exact real outer location and leaf span");
        }
    }
    public static void ModernWrapperSpansAndUnknownItemType()
    {
        const string tag = "{id:'minecraft:text_display',text:['Same',{text:'Same'}]}";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = tag } });
        var result = PackTextExtractor.Extract(Source("item_modifier"), bytes, new(PackSourceKind.Datapack, 71, new(71, 0), new(71, int.MaxValue)));
        var token = "'Same'"; var first = tag.IndexOf(token, StringComparison.Ordinal); var second = tag.IndexOf(token, first + token.Length, StringComparison.Ordinal);
        Require(result.Complete && result.Occurrences.Length == 2 && result.Occurrences.All(text => text.Text == "Same" && text.Address.ComponentPath is null)
            && result.Occurrences.Select(text => text.Address.Syntax!.StartByte).SequenceEqual(new long[] { first, second })
            && result.Occurrences.Select(text => SourceIdentity.Occurrence(text.Address)).Distinct().Count() == 2,
            "logical SNBT paths remove only adapter-created physical wrappers and retain distinct same-text spans");
        Require(result.Occurrences.All(text => SourceAddressJson.Read(SourceAddressJson.Write(text.Address)) == text.Address && SourceIdentity.Occurrence(text.Address).IsValid), "syntax paths roundtrip through address and canonical identity validation");
        var original = result.Occurrences[0].Address; var oldSyntax = original.Syntax!;
        var revised = new SourceAddress(original.Artifact, original.NodePath, original.SemanticRole, componentPath: original.ComponentPath,
            syntax: new(oldSyntax.GrammarProfile, oldSyntax.GrammarVersion, oldSyntax.StartByte + 9, oldSyntax.LengthBytes + 3, new string('1', 64), oldSyntax.Encoding, oldSyntax.Newline, oldSyntax.NodePath));
        Require(SourceIdentity.Occurrence(original) == SourceIdentity.Occurrence(revised), "syntax logical path, not mutable byte spans/preimage, identifies occurrence");
        var unknown = PackTextExtractor.Extract(Source("item_modifiers"), JsonSerializer.SerializeToUtf8Bytes(new { function = "set_nbt", tag = "{title:'Unknown',pages:['?']}" }),
            new(PackSourceKind.Datapack, 26, new(26, 0), new(26, int.MaxValue)));
        Require(!unknown.Complete && unknown.Occurrences.IsEmpty && unknown.Diagnostics.Single().Code == "UnknownTaggedItemType", "loot modifier with no item identity cannot guess written versus writable book schema");
        var oversized = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = "{id:'minecraft:text_display',text:[" + string.Join(',', Enumerable.Repeat("'X'", 20)) + "]}" } });
        try { PackTextExtractor.Extract(Source("item_modifier"), oversized, new(PackSourceKind.Datapack, 71, new(71, 0), new(71, int.MaxValue)), new(MaxNodes: 12)); throw new InvalidOperationException("Expected SNBT node budget rejection"); }
        catch (SnbtException error) when (error.Code == SnbtError.LimitExceeded) { }
    }
    public static void InvalidSyntaxBecomesSourceFailure()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(Format48Pack));
            const string path = "datapacks/p/data/test/item_modifier/bad.json";
            Put(root, path, JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { ["entity_data"] = "{id:" } }));
            var row = new WorldTextExtraction(new WorldInventory(root, World), new(new([], [], [], [path]))).ReadSources().Single();
            Require(row.Outcome == SourceOutcome.Failed && row.DiagnosticCode == "InvalidSnbt" && row.Text is null, "invalid embedded syntax is a locatable source failure, not an escaping iterator exception or empty success");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void SnbtDefaultNamespaceIdentities()
    {
        foreach (var (format, modern) in new[] { (48, false), (71, true), (99, true) })
            foreach (var prefix in new[] { "", ":", "minecraft:" })
                foreach (var (id, field, role) in new[] { ("sign", "block_entity_data", "sign.front.line"), ("text_display", "entity_data", "text_display.text"), ("armor_stand", "entity_data", "armor_stand.name") })
                {
                    var text = modern ? "{text:'Name'}" : "'{\"text\":\"Name\"}'";
                    var content = id == "sign" ? "front_text:{messages:[" + text + "]}" : (id == "text_display" ? "text:" : "CustomName:") + text;
                    var tag = "{id:'" + prefix + id + "'," + content + "}";
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_components", components = new Dictionary<string, string> { [field] = tag } });
                    var result = PackTextExtractor.Extract(Source("item_modifier"), bytes, new(PackSourceKind.Datapack, format, new(format, 0), new(format, int.MaxValue)));
                    Require(result.Complete && result.Occurrences.Length == 1 && result.Occurrences.Single().Text == "Name" && result.Occurrences.Single().Address.SemanticRole == role,
                        "SNBT default namespace IDs select identical exact roles without changing literal source: " + prefix + id);
                }
    }
    public static void UnknownTaggedItemIdentityRespectsScope()
    {
        var metadata = new PackMetadata(PackSourceKind.Datapack, 26, new(26, 0), new(26, int.MaxValue));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_nbt", tag = "{display:{Name:'{\"text\":\"Name\"}'},pages:['?'],title:'Title'}" });
        var selected = PackTextExtractor.Extract(Source("item_modifiers"), bytes, metadata, scope: new([], [], ["item.name"], []));
        Require(selected.Complete && selected.Occurrences.Single().Text == "Name", "unknown book identity does not pollute excluded book roles");
        foreach (var role in new[] { "book", "book.page", "book.title" })
        {
            var book = PackTextExtractor.Extract(Source("item_modifiers"), bytes, metadata, scope: new([], [], [role], []));
            Require(!book.Complete && book.Diagnostics.Single().Code == "UnknownTaggedItemType", "selected ambiguous book fields remain protected: " + role);
        }
        var bundle = JsonSerializer.SerializeToUtf8Bytes(new { function = "set_nbt", tag = "{Items:[{id:'paper',tag:{display:{Name:'{\"text\":\"Nested\"}'}}}]}" });
        Require(!PackTextExtractor.Extract(Source("item_modifiers"), bundle, metadata, scope: new([], [], ["item.name"], [])).Complete,
            "unknown bundle identity still blocks possibly selected nested item text");
    }
}

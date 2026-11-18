using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class NameProtectionTests
{
    public static void NameHoverTextDoesNotChangeIdentifier()
    {
        foreach (var id in new[] { "zombie", "armor_stand" })
        foreach (var fallback in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                var tooltip = fallback ? "{\"translate\":\"tooltip.key\",\"fallback\":\"Help\"}" : "{\"text\":\"Help\"}";
                var component = "{\"text\":\"Guard\",\"color\":\"red\",\"hoverEvent\":{\"action\":\"show_text\",\"contents\":" + tooltip + "}}";
                var entity = C(E("id", S(id)), E("CustomName", S(component)));
                const string path = "entities/r.0.0.mca";
                var original = RegionFile.Read(new byte[8192]).WithChunk(0, new("", C(E("DataVersion", new NbtInt(3953)), E("Entities", L(entity)))), NbtCompression.Zlib, 1);
                Put(root, path, original); Put(root, WorldQueryTests.Function(0), "tellraw @e[name=Guard] \"Display\"\n"u8.ToArray());
                var snapshot = WorldQueryTests.Query(root, cache); var rows = snapshot.ReadSources().ToArray();
                var slot = rows.SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Help").ToSlot("en_us", "zh_tw");
                Require(slot.Role == (id == "armor_stand" ? "armor_stand.name.hover" : "entity.name.hover") + (fallback ? ".fallback" : "") && rows.SelectMany(row => row.Commands?.References ?? []).Any(reference => reference.Name == "Guard"), "real name component hover is a distinct permitted role alongside a static name reference");
                var inventory = new WorldInventory(root, World);
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("tooltip", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "提示")))]);
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var doc = RegionFile.Read(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!)).ReadChunk(0).Document;
                    var value = (NbtString)NbtLocator.Resolve(doc.Root, slot.Address.NodePath).Value!;
                    using var json = BoundedJson.ReadDecoded(NativeJsonStrings.EncodeJavaDocument(value.Value));
                    Require(NativeJsonStrings.Read(json.RootElement.GetProperty("text")) == "Guard" && NativeJsonStrings.Read(json.RootElement.GetProperty("color")) == "red"
                        && NativeJsonStrings.Read(json.RootElement.GetProperty("hoverEvent").GetProperty("contents").GetProperty(fallback ? "fallback" : "text")) == "提示", "tooltip translation preserves the actual identifying name, color and selector binding");
                }
                Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "hover preparation never commits original region");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
    public static void LiteralNamesCannotBypassReferenceSynchronization()
    {
        foreach (var isDynamic in new[] { false, true })
        foreach (var (id, role, component) in new[] {
            ("zombie", "entity.name", "{\"text\":\"Guard\"}"),
            ("armor_stand", "armor_stand.name", "{\"text\":\"Guard\"}"),
            ("armor_stand", "armor_stand.name.fallback", "{\"translate\":\"name.key\",\"fallback\":\"Guard\"}") })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                var entity = C(E("id", S(id)), E("CustomName", S(component)), E("UUID", new NbtIntArray([1, 2, 3, 4])));
                const string path = "entities/r.0.0.mca";
                var original = RegionFile.Read(new byte[8192]).WithChunk(0, new("", C(E("DataVersion", new NbtInt(3953)), E("Entities", L(entity)))), NbtCompression.Zlib, 1);
                Put(root, path, original);
                Put(root, WorldQueryTests.Function(0), Encoding.UTF8.GetBytes(isDynamic ? "$tellraw @e[name=$(who)] \"Display\"\n" : "tellraw @e[name=Guard] \"Display\"\n"));
                var snapshot = WorldQueryTests.Query(root, cache);
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Guard").ToSlot("en_us", "zh_tw");
                Require(slot.Role == role, "real entity/armorstand component and fallback name carry identifying roles alongside real static/dynamic command sources");
                var inventory = new WorldInventory(root, World); var builder = new WorldPlanBuilder(inventory);
                var unchanged = builder.Build(snapshot, [new LiteralPatch("no-op", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Guard")))]);
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, unchanged, true))
                    Require(File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!).SequenceEqual(original), "no-op names retain exact region bytes without requiring a rename group");
                Reject<EditRejectedException>(() => builder.Build(snapshot, [new LiteralPatch("unsafe-rename", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Visitor")))]));
                Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "literal name translation cannot silently invalidate selector references or bypass unresolved coverage");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
}

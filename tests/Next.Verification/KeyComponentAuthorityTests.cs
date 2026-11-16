using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using Next.Minecraft;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.CandidateIoTests;

namespace Next.Verification;

internal static class KeyComponentAuthorityTests
{
    public static void PlainTextFieldsCannotGrantComponentAuthority()
    {
        foreach (var representation in new[] { "nbt", "json", "encoded" })
        foreach (var fallback in new[] { false, true }) Check(representation, fallback, nested: false);
    }
    public static void NestedComponentsRemainAuthorized()
    {
        foreach (var representation in new[] { "nbt", "json", "encoded" }) Check(representation, fallback: false, nested: true);
    }
    private static void Check(string representation, bool fallback, bool nested)
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            var component = fallback ? "{\"translate\":\"old.key\",\"fallback\":\"Hello\"}"
                : "{\"text\":\"Hello\",\"extra\":[\"Child\"]}";
            string path;
            if (representation == "nbt")
            {
                path = "level.dat";
                var data = (NbtCompound)((NbtCompound)WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions).Root).Entries.Single().Value;
                var value = fallback ? new NbtCompound([new("translate", new NbtString("old.key")), new("fallback", new NbtString("Hello"))])
                    : new NbtCompound([new("text", new NbtString("Hello")), new("extra", new NbtList(NbtType.Compound, [new NbtCompound([new("text", new NbtString("Child"))])]))]);
                var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", value)]))]);
                Put(root, path, CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
            }
            else if (representation == "json")
            {
                path = "datapacks/p/data/test/advancement/a.json";
                Put(root, path, Encoding.UTF8.GetBytes("{\"display\":{\"title\":" + component + "}}"));
            }
            else { path = "level.dat"; LegacyImportMappingTests.BossWorld(root, componentJson: component); }
            var format = representation == "nbt" ? 55 : 34;
            Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":" + format + ",\"description\":\"Resources\"}}"));
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            var snapshot = LegacyImportMappingTests.Query(root, cache); var expectedText = nested ? "Child" : "Hello";
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == expectedText).ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(representation == "nbt" ? 4325 : 3953)!;
            NbtValue expected = representation == "nbt" ? nested ? new NbtCompound([new("text", new NbtString(expectedText))]) : new NbtString(expectedText) : representation == "json" ? new NbtString("\"" + expectedText + "\"") : new NbtString(component);
            var address = representation == "nbt" && nested ? new SourceAddress(slot.Address.Artifact, new(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1)), slot.Role) : slot.Address;
            var operation = new KeyConversionPatch("authority", new(address, expected,
                representation == "nbt" ? nested ? KeyConversionRule.StructuredTextToTranslate : KeyConversionRule.LiteralToTranslateComponent : KeyConversionRule.EncodedTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.authority", "Translated")));
            var inventory = new WorldInventory(root, World); var original = File.ReadAllBytes(Path.Combine(root, path));
            if (!nested) Reject<EditRejectedException>(() => new WorldPlanBuilder(inventory).Build(snapshot, [operation]));
            else
            {
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
                using var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
                Require(prepared.Manifest.Artifacts.Length == 2, "genuine nested components retain exact key-conversion authority without changing NBT list element types");
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "authority rejection and successful preparation both preserve original bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}

using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class KeyFieldAuthorityTests
{
    public static void PlainBookMetadataDoesNotBecomeAComponent()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":55,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            var contents = new NbtCompound([new("title", new NbtString("Title")), new("author", new NbtString("Author"))]);
            var item = new NbtCompound([new("id", new NbtString("written_book")), new("components", new NbtCompound([new("written_book_content", contents)]))]);
            var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(4325)), new("Inventory", new NbtList(NbtType.Compound, [item]))]));
            const string path = "playerdata/p.dat"; var original = CompressedNbt.WriteDocument(document, NbtCompression.GZip); Put(root, path, original);
            var snapshot = WorldQueryTests.Query(root, cache); var profile = MinecraftProfiles.ForDataVersion(4325)!;
            var occurrences = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.Artifact.RelativePath == path).ToArray();
            Require(occurrences.Length == 2, "real written book supplies both plain title and author slots");
            foreach (var occurrence in occurrences)
            {
                var slot = occurrence.ToSlot("en_us", "zh_tw"); Require(slot.Role is "book.title" or "book.author", "real book schema distinguishes plain metadata from text components");
                var operation = new KeyConversionPatch("invalid-component", new(slot.Address, new NbtString(slot.SourceText), KeyConversionRule.LiteralToTranslateComponent, profile.Id, profile.Version,
                    slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.metadata", "Translated")));
                Reject<EditRejectedException>(() => new WorldPlanBuilder(new WorldInventory(root, World)).Build(snapshot, [operation]));
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "a literal slot cannot grant structured component write authority to plain metadata");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}

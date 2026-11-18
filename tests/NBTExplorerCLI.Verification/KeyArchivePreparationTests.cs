using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class KeyArchivePreparationTests
{
    public static void ArchivedJsonUsesContainerEntryAndExactPackProfile()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string component = "{\"text\":\"Hello\",\"color\":\"red\"}";
            const string entry = "data/test/advancement/a.json"; const string packPath = "datapacks/data.zip";
            var original = Zip(("pack.mcmeta", "{\"pack\":{\"pack_format\":48,\"description\":\"Data\"}}"), (entry, "{\"display\":{\"title\":" + component + "},\"n\":1e+02}"), ("opaque.bin", "Keep")); Put(root, packPath, original);
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json";
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}"u8.ToArray()); Put(root, language, "{}"u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == packPath && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(3953)!; var parent = new TypedPath(slot.Address.NodePath.Segments.Take(slot.Address.NodePath.Segments.Length - 1));
            var operation = new KeyConversionPatch("archive-source", new(new(slot.Address.Artifact, parent, slot.Role), new NbtString(component), KeyConversionRule.EncodedTextToTranslate,
                profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translated"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.archive", "Translated")));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var output = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == packPath).CandidatePath!);
                using var archive = ZipArchiveValidation.OpenRead(output); using var stream = archive.GetEntry(entry)!.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                Require(Encoding.UTF8.GetString(buffer.ToArray()) == "{\"display\":{\"title\":{\"translate\":\"test.archive\",\"color\":\"red\"}},\"n\":1e+02}", "ZIP component uses exact native JSON adapter and preserves outer framing");
                using var opaque = archive.GetEntry("opaque.bin")!.Open(); Require(opaque.ReadByte() == 'K', "unselected ZIP entry is preserved");
            }
            Require(File.ReadAllBytes(Path.Combine(root, packPath)).SequenceEqual(original), "archive source remains unchanged after preparation");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}

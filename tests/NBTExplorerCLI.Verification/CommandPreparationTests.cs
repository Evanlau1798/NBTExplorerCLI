using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class CommandPreparationTests
{
    public static void ExactCommandLayersPreserveFramingAndReferences()
    {
        foreach (var modern in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                if (modern) Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":71,\"description\":\"Pack\"}}"u8.ToArray());
                var script = modern
                    ? "# Head\r\ntellraw @a[name=Guard] {text:'One',extra:['Two'],click_event:{action:'run_command',command:'say One'}}\r\ngive @s written_book[written_book_content={title:'Book',author:'Author',pages:[{raw:'Page'}]}]\n"
                    : "# Head\r\ntellraw @a[name=Guard] {\"text\":\"One\",\"extra\":[\"Two\"],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"say One\"}}\r\ngive @s written_book[written_book_content={title:'Book',author:'Author',pages:['{\"text\":\"Page\",\"extra\":[\"Tail\"]}']}]\n";
                byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(script)]; Put(root, WorldQueryTests.Function(0), bytes);
                var snapshot = WorldQueryTests.Query(root, cache);
                var rows = snapshot.ReadSources().Where(row => row.Source.Location.Artifact.RelativePath == WorldQueryTests.Function(0)).ToArray();
                var slots = rows.SelectMany(row => row.Text?.Occurrences ?? []).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
                Require(rows.All(row => row.Outcome is SourceOutcome.Success or SourceOutcome.Unsupported) && slots.Length == (modern ? 5 : 6), "real command premise modern=" + modern + "; slots=" + string.Join(',', slots.Select(slot => slot.SourceText)) + "; diagnostics=" + string.Join(',', rows.Select(row => row.DiagnosticCode)));
                var operations = slots.Select((slot, index) => new LiteralPatch("command-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", (modern ? "譯\"\n" : "譯\"\\") + slot.SourceText)))).ToArray();
                var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
                if (!modern)
                {
                    var unsupported = slots.Single(slot => slot.SourceText == "Book");
                    var invalid = new WorldPlanBuilder(inventory).Build(snapshot, [new LiteralPatch("line-break", new(unsupported, new(unsupported.Id, unsupported.SourceTextHash, "zh_tw", "Line\nBreak")))]);
                    CandidateIoTests.Reject<NotSupportedException>(() => new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, invalid, true));
                    Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "SNBT through Java 1.21.4 cannot carry literal command newlines and fails before staging");
                }
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var output = File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!);
                    Require(output.AsSpan(0, 3).SequenceEqual(bytes.AsSpan(0, 3)) && Encoding.UTF8.GetString(output.AsSpan(3)).StartsWith("# Head\r\ntellraw @a[name=Guard] ", StringComparison.Ordinal)
                        && Encoding.UTF8.GetString(output).Contains(modern ? "command:'say One'" : "\"value\":\"say One\"", StringComparison.Ordinal), "exact splices retain BOM, comments, mixed line framing, selector and protected event command");
                }
                Require(File.ReadAllBytes(Path.Combine(root, WorldQueryTests.Function(0))).SequenceEqual(bytes), "command preparation never commits original bytes");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
}

using System.Text;
using System.Text.Json;
using Next.Domain;
using Next.Minecraft;
using Next.Formats;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class PlanSyntaxBoundaryTests
{
    public static void CommandSiblingsHaveSeparateLogicalTargets()
    {
        const string script = "tellraw @s ['One','Two']";
        var artifact = new ArtifactAddress(World, "test.mcfunction", ArtifactKind.McFunction);
        var result = CommandTextExtractor.Extract(new(new(artifact, TypedPath.Root, "command.function"), script), MinecraftProfiles.ForDataVersion(4325)!);
        Require(result.Complete && result.Text.Occurrences.Length == 2, "real command grammar exposes two independently addressed sibling slots");
        Require(Plan(artifact, Encoding.UTF8.GetBytes(script), result.Text).Operations.Length == 2, "different command logical paths are not competing writes to the physical string root");
        Reject(() => Plan(artifact, Encoding.UTF8.GetBytes(script), result.Text with { Occurrences = [result.Text.Occurrences[0], result.Text.Occurrences[0]] }));
        var first = result.Text.Occurrences[0]; var syntax = first.Address.Syntax!;
        var parent = new SourceAddress(artifact, first.Address.NodePath, first.Address.SemanticRole,
            syntax: new(syntax.GrammarProfile, syntax.GrammarVersion, 0, Encoding.UTF8.GetByteCount(script), syntax.PreimageHash, syntax.Encoding, syntax.Newline, new(syntax.NodePath!.Segments.Take(2))));
        Reject(() => Plan(artifact, Encoding.UTF8.GetBytes(script), result.Text with { Occurrences = [first, first with { Address = parent }] }));
        var archived = new ArtifactAddress(World, "datapacks/p.zip", ArtifactKind.Archive, "data/test/function/a.mcfunction");
        var entry = CommandTextExtractor.Extract(new(new(archived, TypedPath.Root, "command.function"), script), MinecraftProfiles.ForDataVersion(4325)!);
        var zip = WorldDiscoveryTests.Zip(("data/test/function/a.mcfunction", script));
        Require(entry.Complete && Plan(archived, zip, entry.Text).Operations.Length == 2, "archive-entry command hash stays separate from its physical ZIP preimage");
    }
    public static void DecodedSyntaxAndOuterArtifactHashesRemainSeparate()
    {
        var artifact = new ArtifactAddress(World, "datapacks/p/data/test/loot_tables/a.json", ArtifactKind.Json);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { pools = new[] { new { entries = new[] { new { type = "item", name = "written_book", functions = new[] {
            new { function = "set_nbt", tag = """
                {pages:['{"text":"One","extra":[{"text":"Tail"}]}','{"text":"Two"}']}
                """ } } } } } } });
        var result = PackTextExtractor.Extract(new(artifact, WorldSourceRole.PackJson, PackRoot: "datapacks/p", PackKind: PackSourceKind.Datapack), bytes,
            new(PackSourceKind.Datapack, 26, new(26, 0), new(26, int.MaxValue)));
        Require(result.Complete && result.Occurrences.Length == 3 && result.Occurrences.All(item => item.Address.Syntax!.PreimageHash != Hash256.Compute(bytes).Hex), "decoded SNBT has its own binding hash within one outer JSON artifact");
        Require(Plan(artifact, bytes, result).Operations.Length == 3, "outer raw preimage and decoded syntax binding preserve independent nested JSON fragments within one SNBT leaf");
        var second = result.Occurrences[^1]; var syntax = second.Address.Syntax!;
        var differentInput = new SourceAddress(artifact, second.Address.NodePath, second.Address.SemanticRole, componentPath: second.Address.ComponentPath,
            syntax: new(syntax.GrammarProfile, syntax.GrammarVersion, syntax.StartByte, syntax.LengthBytes, Hash256.Compute([99]).Hex, syntax.Encoding, syntax.Newline, syntax.NodePath));
        Reject(() => Plan(artifact, bytes, result with { Occurrences = [result.Occurrences[0], second with { Address = differentInput }] }));
        var firstSyntax = result.Occurrences[0].Address.Syntax!;
        var overlap = new SourceAddress(artifact, second.Address.NodePath, second.Address.SemanticRole, componentPath: second.Address.ComponentPath,
            syntax: new(syntax.GrammarProfile, syntax.GrammarVersion, firstSyntax.StartByte, firstSyntax.LengthBytes, syntax.PreimageHash, syntax.Encoding, syntax.Newline, syntax.NodePath));
        Reject(() => Plan(artifact, bytes, result with { Occurrences = [result.Occurrences[0], second with { Address = overlap }] }));
    }
    private static PatchPlan Plan(ArtifactAddress artifact, byte[] bytes, TextExtractionResult extracted)
    {
        var physical = artifact.ContainerEntry is null ? artifact : new ArtifactAddress(artifact.WorldId, artifact.RelativePath, artifact.Kind);
        var hash = Hash256.Compute(bytes); var snapshot = SourceIdentity.Snapshot(World, [new(physical, hash, SourceOutcome.Success)], SnapshotScope.All, new("1", "1", "1", "1"));
        var operations = extracted.Occurrences.Select((item, index) =>
        {
            var slot = item.ToSlot("en_us", "zh_tw");
            return new LiteralPatch("literal-" + index, new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Translation" + index)));
        });
        var plan = new PatchPlan(World, snapshot, "minecraft-known-2", "1", operations, [new(physical, ArtifactImage.Present(hash))], [new(physical, OutputIntent.Write, ["projection"])], []);
        var restored = PatchPlanJson.Read(PatchPlanJson.Write(plan));
        Require(restored.PlanHash == plan.PlanHash && restored.Operations.SelectMany(item => item.Targets).SequenceEqual(plan.Operations.SelectMany(item => item.Targets)), "serialized plan preserves complete physical/syntax/component bindings and its semantic hash");
        return plan;
    }
}

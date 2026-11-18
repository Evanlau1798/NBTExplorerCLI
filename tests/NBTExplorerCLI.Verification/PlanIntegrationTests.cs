using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class PlanIntegrationTests
{
    public static void RestoreBindsExpectedTarget()
    {
        var restore = new ArtifactRestore(Hash256.Compute([8]),
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Postimage))],
            new Dictionary<ArtifactAddress, ArtifactImage> { [PlanTests.Artifact] = ArtifactImage.Present(PlanTests.Preimage) });
        var plan = PlanTests.Plan([new RestorePatch("restore", restore)]);
        _ = new PreparedManifest(plan, [PlanTests.Candidate()]);
        Reject(() => new PreparedManifest(plan, [PlanTests.Candidate(postimage: Hash256.Compute([9]))]));
        Reject(() => PlanTests.Plan([new RestorePatch("restore", restore), PlanTests.Edit()]));
        var deleteRestore = new ArtifactRestore(Hash256.Compute([8]), [new(PlanTests.Artifact, ArtifactImage.Absent)], restore.CurrentImages);
        var deletion = new PatchPlan(World, PlanTests.Snapshot, "1", "1", [new RestorePatch("delete", deleteRestore)],
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage))], [new(PlanTests.Artifact, OutputIntent.Delete, ["scope"])], []);
        _ = new PreparedManifest(deletion, [new(PlanTests.Artifact, null, ArtifactImage.Absent, [new("scope", true, PlanTests.Preimage)])]);
    }

    public static void SyntaxPreimageMustMatch()
    {
        var artifact = new ArtifactAddress(World, "x.mcfunction", ArtifactKind.McFunction);
        var address = new SourceAddress(artifact, new([PathSegment.ForIndex(0)]), "command.text",
            syntax: new("synthetic.command", 1, 0, 4, PlanTests.Preimage.Hex, "utf-8", "lf"));
        var slot = new TextSlot(address, "same", "command", "en_us", "zh_tw", false, PlaceholderPolicy.None);
        var patch = new LiteralPatch("literal", new(slot, Reply(slot, "different")));
        PatchPlan Make(Hash256 input) => new(World, PlanTests.Snapshot, "1", "1", [patch],
            [new(artifact, ArtifactImage.Present(input))], [new(artifact, OutputIntent.Write, ["scope"])], []);
        _ = Make(PlanTests.Preimage);
        Reject(() => Make(PlanTests.Postimage));
        var index = new TypedPath([PathSegment.ForIndex(0)]);
        Reject(() => new TypedPatch("rename", Address(index), TypedEdit.Rename(index, new NbtInt(1), "key")));
    }

    public static void KeyDependencyAndMixedOperations()
    {
        var textPath = new TypedPath([PathSegment.ForKey("name")]);
        var slot = new TextSlot(Address(textPath), "Hello", "item.name", "en_us", "zh_tw", false, PlaceholderPolicy.None);
        var language = new ArtifactAddress(World, "assets/test/lang/zh_tw.json", ArtifactKind.Json);
        var key = new KeyConversionPatch("key", new(slot.Address, new NbtString("Hello"), KeyConversionRule.LiteralToTranslateComponent,
            "synthetic", 1, slot, Reply(slot, "你好"), new(language, null, "zh_tw", "test.hello", "你好")));
        var operations = new PatchOperation[] { key, PlanTests.Edit("typed", "count") };
        var inputs = new[] { new ArtifactPrecondition(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage)), new(language, ArtifactImage.Absent) };
        var outputs = new[] { new OutputDeclaration(PlanTests.Artifact, OutputIntent.Write, ["scope", "parse"]), new(language, OutputIntent.Write, ["parse"]) };
        var one = new PatchPlan(World, PlanTests.Snapshot, "1", "1", operations, inputs, outputs, []);
        var two = new PatchPlan(World, PlanTests.Snapshot, "1", "1", operations.Reverse(), inputs.Reverse(), outputs.Reverse(), []);
        Require(one.PlanHash == two.PlanHash && one.Dependencies.Length == 1
            && one.Dependencies[0] == new ArtifactDependency(PlanTests.Artifact, language), "key conversion必需輸出依賴不能被省略，集合列舉不改hash");
        Reject(() => new PatchPlan(World, PlanTests.Snapshot, "1", "1", operations, inputs, [outputs[0]], []));
        var prepared = new PreparedManifest(one, [PlanTests.Candidate(),
            new(language, "stage/lang.json", ArtifactImage.Present(Hash256.Compute([7])), [new("parse", true, PlanTests.Preimage)])]);
        var reversed = new PreparedManifest(two, prepared.Artifacts.Reverse());
        Require(prepared.ManifestHash == reversed.ManifestHash, "prepared artifact/check集合列舉不影響hash");
        Reject(() => new PreparedManifest(one, [PlanTests.Candidate(),
            new(language, "STAGE/A.DAT", ArtifactImage.Present(Hash256.Compute([7])), [new("parse", true, PlanTests.Preimage)])]));
    }

    public static void AllPayloadsAffectHash()
    {
        var slot = Slot("Hello %s");
        var literalA = PlanTests.Plan([new LiteralPatch("literal", new(slot, Reply(slot, "你好 %s")))]);
        var literalB = PlanTests.Plan([new LiteralPatch("literal", new(slot, Reply(slot, "再見 %s")))]);
        Require(literalA.PlanHash != literalB.PlanHash, "literal替換值必須影響hash");
        var group = SourceIdentity.Group("inferred.name", slot.Address, 1);
        var memberA = new ReferenceMember(slot.Address, "Bob", "Alice", "synthetic.name", 1);
        var memberB = new ReferenceMember(slot.Address, "Bob", "Carol", "synthetic.name", 1);
        var digest = SourceIdentity.Members(group, [memberA.Occurrence], 1).Digest;
        var referenceA = PlanTests.Plan([new ReferencePatch("references", new(group, 1, digest, ReferenceCompleteness.Complete, [memberA]))]);
        var referenceB = PlanTests.Plan([new ReferencePatch("references", new(group, 1, digest, ReferenceCompleteness.Complete, [memberB]))]);
        Require(referenceA.PlanHash != referenceB.PlanHash, "reference原值／新值宣告必須影響hash");
        Reject(() => new ReferencePatch("unknown", new(group, 1, digest, ReferenceCompleteness.Unresolved, [memberA])));
        var restoreA = new ArtifactRestore(PlanTests.Preimage, [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Postimage))],
            new Dictionary<ArtifactAddress, ArtifactImage> { [PlanTests.Artifact] = ArtifactImage.Present(PlanTests.Preimage) });
        var restoreB = new ArtifactRestore(PlanTests.Postimage, restoreA.AuthorizedArtifacts, restoreA.CurrentImages);
        Require(PlanTests.Plan([new RestorePatch("restore", restoreA)]).PlanHash != PlanTests.Plan([new RestorePatch("restore", restoreB)]).PlanHash,
            "restore的verified manifest provenance必須影響hash");
    }
}

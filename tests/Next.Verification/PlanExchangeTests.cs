using Next.Domain;
using Next.Formats;
using System.Text.Json.Nodes;
using static Next.Verification.TextSlotTests;
using static Next.Verification.ExchangeContractTests;

namespace Next.Verification;

internal static class PlanExchangeTests
{
    internal static IEnumerable<PatchPlan> Examples()
    {
        yield return PlanTests.Plan();
        var slot = Slot("Hello %s");
        yield return PlanTests.Plan([new LiteralPatch("literal", new(slot, Reply(slot, "你好 %s")))]);
        var keySlot = new TextSlot(Address(new([PathSegment.ForKey("name")])), "Hello", "name", "en_us", "zh_tw", false, PlaceholderPolicy.None);
        var language = new ArtifactAddress(World, "assets/test/lang/zh_tw.json", ArtifactKind.Json);
        var key = new KeyConversionPatch("key", new(keySlot.Address, new NbtString("Hello"), KeyConversionRule.LiteralToTranslateComponent,
            "synthetic", 1, keySlot, Reply(keySlot, "你好"), new(language, null, "zh_tw", "test.hello", "你好")));
        yield return new(World, PlanTests.Snapshot, "1", "1", [key],
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage)), new(language, ArtifactImage.Absent)],
            [new(PlanTests.Artifact, OutputIntent.Write, ["scope"]), new(language, OutputIntent.Write, ["parse"])], []);
        var member = new ReferenceMember(slot.Address, "Bob", "Alice", "synthetic.name", 1);
        var group = SourceIdentity.Group("inferred.name", slot.Address, 1);
        yield return PlanTests.Plan([new ReferencePatch("reference", new(group, 1,
            SourceIdentity.Members(group, [member.Occurrence], 1).Digest, ReferenceCompleteness.Complete, [member]))]);
        yield return PlanTests.Plan([new RestorePatch("restore", new(PlanTests.Preimage,
            [new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Postimage))],
            new Dictionary<ArtifactAddress, ArtifactImage> { [PlanTests.Artifact] = ArtifactImage.Present(PlanTests.Preimage) }))]);
    }
    public static void AllOperationRoundtrips()
    {
        foreach (var plan in Examples())
        {
            var json = PatchPlanJson.Write(plan);
            var decoded = PatchPlanJson.Read(json);
            Require(decoded.PlanHash == plan.PlanHash && decoded.Operations.Single().Kind == plan.Operations.Single().Kind
                && PatchPlanJson.Write(decoded) == json, "五種payload需完整保留，重編碼與hash不可變");
            var forged = JsonNode.Parse(json)!;
            forged["data"]!["planHash"] = new string('0', 64);
            RejectJson(() => PatchPlanJson.Read(forged.ToJsonString()));
        }
    }
    public static void PreparedAndReceiptRoundtrips()
    {
        foreach (var plan in Examples())
        {
            var prepared = new PreparedManifest(plan, plan.Outputs.Select(output => new PreparedArtifact(output.Artifact,
                "stage/" + output.Artifact.RelativePath, ArtifactImage.Present(PlanTests.Postimage),
                output.RequiredChecks.Select(check => new ValidationCheck(check, true, PlanTests.Preimage)))));
            var decoded = PreparedManifestJson.Read(PreparedManifestJson.Write(prepared), plan);
            Require(decoded.ManifestHash == prepared.ManifestHash, "prepared需綁原plan與完整validation evidence");
            var results = plan.Operations.Select(operation => new OperationReceipt(operation.Id, OperationOutcome.Applied, null,
                operation.RequiredOutputs.Distinct().Select(artifact => new ImageProof(ImageProofKind.Postimage, artifact,
                    ArtifactImage.Present(PlanTests.Postimage), ArtifactImage.Present(PlanTests.Postimage)))));
            var receipt = prepared.Receipt(plan, 1, results, true);
            var json = ApplyReceiptJson.Write(receipt);
            var loaded = ApplyReceiptJson.Read(json, plan, prepared);
            Require(loaded.Success && loaded.Results.SequenceEqual(receipt.Results) && loaded.Counts.Values.Sum() == loaded.Requested,
                "回執需保存九態與逐檔image claims並重驗plan/prepared");
            RejectJson(() => ApplyReceiptJson.Read(json, plan));
            var forged = JsonNode.Parse(json)!;
            forged["data"]!["requested"] = 9;
            RejectJson(() => ApplyReceiptJson.Read(forged.ToJsonString(), plan, prepared));
        }
        var failedPlan = PlanTests.Plan();
        var failed = failedPlan.Receipt(1, [new("edit", OperationOutcome.Failed, "synthetic failure")], false);
        Require(!ApplyReceiptJson.Read(ApplyReceiptJson.Write(failed), failedPlan).Success, "未prepared的失敗回執可交換，但不宣稱apply成功");
    }

    public static void RejectsNullRestoreMetadata()
    {
        var plan = Examples().Single(example => example.Operations[0] is RestorePatch);
        var node = JsonNode.Parse(PatchPlanJson.Write(plan))!;
        node["data"]!["operations"]![0]!["payload"]!["currentImages"]![0] = null;
        RejectJson(() => PatchPlanJson.Read(node.ToJsonString()));
    }

    public static void FrozenFixtures(string fixtureRoot)
    {
        string Read(string family, string sample) => File.ReadAllText(Path.Combine(fixtureRoot, "contracts", family + "." + sample + ".json"));
        var plan = PatchPlanJson.Read(Read("patch-plan", "minimal"));
        var prepared = PreparedManifestJson.Read(Read("prepared-manifest", "minimal"), plan);
        (string Family, Func<string, object> Decode)[] families =
        [
            ("patch-plan", json => PatchPlanJson.Read(json)),
            ("prepared-manifest", json => PreparedManifestJson.Read(json, plan)),
            ("apply-receipt", json => ApplyReceiptJson.Read(json, plan, prepared))
        ];
        foreach (var (family, decode) in families)
        {
            _ = decode(Read(family, "minimal"));
            _ = decode(Read(family, "boundary"));
            RejectJson(() => decode(Read(family, "missing-field")));
            RejectJson(() => decode(Read(family, "exclusive-state")));
            try { decode(Read(family, "unknown-version")); throw new InvalidOperationException("未知schema版本fixture未拒絕"); }
            catch (NotSupportedException) { }
            var unknown = JsonNode.Parse(Read(family, "minimal"))!;
            unknown["data"]!["unexpected"] = true;
            RejectJson(() => decode(unknown.ToJsonString()));
        }
        var nine = PatchPlanJson.Read(Read("patch-plan", "nine-outcomes"));
        var staged = new PreparedManifest(nine, nine.Outputs.Select(output => new PreparedArtifact(output.Artifact,
            "stage/" + output.Artifact.RelativePath, ArtifactImage.Present(PlanTests.Postimage),
            output.RequiredChecks.Select(check => new ValidationCheck(check, true, PlanTests.Preimage)))));
        var receipt = ApplyReceiptJson.Read(Read("apply-receipt", "nine-outcomes"), nine, staged);
        Require(receipt.Requested == 9 && receipt.Counts.Values.All(count => count == 1) && !receipt.Success,
            "同一組serialized fixtures需證明九態互斥守恆，不把partial當success");
        var conflicting = PatchPlanJson.Read(Read("patch-plan", "conflicting-images"));
        var conflictingStage = new PreparedManifest(conflicting, [PlanTests.Candidate()]);
        RejectJson(() => ApplyReceiptJson.Read(Read("apply-receipt", "conflicting-images"), conflicting, conflictingStage));
        var forged = JsonNode.Parse(Read("prepared-manifest", "minimal"))!;
        forged["data"]!["manifestHash"] = new string('0', 64);
        RejectJson(() => PreparedManifestJson.Read(forged.ToJsonString(), plan));
    }
}

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Next.Domain;
using Next.Formats;
using static Next.Verification.TextSlotTests;
using static Next.Verification.ExchangeContractTests;

namespace Next.Verification;

internal static class ResourceContractTests
{
    private static ResourceLanguage Language(string locale = "zh_tw", string text = "\0\ud800 ‖§‖ {{T0}} %s")
        => new(locale, [new("map.b", ""), new("map.a", text)]);
    private static ResourceMerge Merge() => new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 20), new("assets/test/textures/icon.png", PlanTests.Postimage, 2)]);
    private static ResourceGeneration Generation(ResourceOutputFormat format = ResourceOutputFormat.LanguageFiles, bool reverse = false, string text = "\0\ud800 ‖§‖ {{T0}} %s")
    {
        var languages = new[] { Language(text: text), Language("en_us", "Hello") };
        if (reverse) languages = languages.Reverse().Select(language => language with { Values = language.Values.Reverse().ToImmutableArray() }).ToArray();
        return new(World, format, format == ResourceOutputFormat.LanguageFiles ? "" : format == ResourceOutputFormat.ZipPack ? "resources.zip" : "Translations",
            languages, format == ResourceOutputFormat.LanguageFiles ? null : new(75, 1), format == ResourceOutputFormat.LanguageFiles ? null : Merge());
    }
    private static PatchPlan Plan(ResourceGeneration generation, string policy = ResourceGeneration.Policy)
    {
        var patch = new ResourcePatch("resources", generation);
        return new(World, PlanTests.Snapshot, ResourceGeneration.Profile, policy, [patch],
            patch.RequiredOutputs.Select(artifact => new ArtifactPrecondition(artifact, ArtifactImage.Absent)),
            patch.RequiredOutputs.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, ["resource-schema", "projection"])), []);
    }
    public static void PathValidationAllocationIsBounded(string? artifactRoot)
    {
        _ = Generation(ResourceOutputFormat.ZipPack);
        var prefix = "assets/" + string.Join('/', Enumerable.Repeat("a", 1900)) + "/file";
        var paths = Enumerable.Range(0, 128).Select(index => prefix + index + ".bin").ToArray();
        var merge = new ResourceMerge(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 1), .. paths.Select(path => new ResourceMergeEntry(path, PlanTests.Postimage, 0))]);
        var languages = new[] { Language() }; var format = new PackFormatVersion(34, 0);
        var pathBytes = paths.Sum(path => (long)path.Length * 2); var budget = 1_048_576 + pathBytes * 8;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var generation = new ResourceGeneration(World, ResourceOutputFormat.ZipPack, "resources.zip", languages, format, merge);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(generation.Outputs.Length == 1, "allocation witness is an accepted bounded ZIP recipe, not an early-rejection shortcut");
        if (artifactRoot is not null)
        {
            Directory.CreateDirectory(artifactRoot);
            File.WriteAllText(Path.Combine(artifactRoot, "resource-path-allocation.json"), JsonSerializer.Serialize(new { kind = "current-thread-cumulative-allocation", pathCount = paths.Length, shortComponents = 1900, pathBytes, allocatedBytes = allocated, budgetBytes = budget, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription }));
        }
        Require(allocated <= budget, $"path validation temporary allocation exceeds the fixed witness envelope: allocated={allocated}, budget={budget}, inputPathUtf16Bytes={pathBytes}; this is cumulative allocation, not peak RSS");
    }
    public static void SharedPathValidationKeepsItsExistingAddressSemantics()
    {
        (string Path, bool Physical, bool Entry)[] cases =
        [
            ("a", true, true), ("雪/\ud83d\ude00.json", true, true), ("assets/\ud800", true, true), ("CON.txt", true, true),
            ("a ", false, true), ("a.", false, true), ("folder/1:name", false, true), ("folder/a:b", false, true),
            ("<file>", false, true), ("\u0001", false, true), ("", false, false), ("/x", false, false),
            ("a//b", false, false), ("a/", false, false), ("a/../b", false, false), ("a/./b", false, false),
            ("a\\b", false, false), ("a\0b", false, false), ("C:/x", false, false), ("c:x", false, false)
        ];
        foreach (var item in cases)
            foreach (var entry in new[] { false, true })
            {
                var accepted = true; try { ArtifactAddress.ValidatePath(item.Path, entry); } catch (ArgumentException) { accepted = false; }
                Require(accepted == (entry ? item.Entry : item.Physical), "shared path predicate preserves original physical versus container semantics");
            }
    }
    public static void DerivedOutputsStayInsideTheFullPathAndRetentionBudgets()
    {
        var destination = new string('p', 240);
        ResourceMerge Input(int count) => new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 1), .. Enumerable.Range(0, count).Select(index => new ResourceMergeEntry("assets/test/file" + index + ".bin", PlanTests.Postimage, 0))]);
        var small = new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, destination, [Language()], new(34, 0), Input(10));
        Require(small.Outputs.Length == 12, "positive control permits bounded expansion with long but legal directory names");
        try { _ = new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, destination, [Language()], new(34, 0), Input(90_000)); throw new InvalidOperationException("expanded retained output paths escaped the aggregate recipe budget"); } catch (ArgumentException) { }
    }
    public static void ComposedOutputLengthIsCheckedInAdditionToInputLengths()
    {
        var destination = new string('p', 240);
        var longEntry = "assets/" + string.Join('/', Enumerable.Repeat(new string('a', 200), 20)) + "/x.png";
        Require(longEntry.Length <= 4096 && destination.Length <= 255 && destination.Length + longEntry.Length + 1 > 4096, "path-length witness has individually legal inputs but an oversized combined output");
        var merge = new ResourceMerge(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 1), new(longEntry, PlanTests.Postimage, 0)]);
        Require(new ResourceGeneration(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), merge).Outputs.Length == 1, "the same manifest is valid when it does not expand into prefixed physical filenames");
        try { _ = new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, destination, [Language()], new(34, 0), merge); throw new InvalidOperationException("combined output path escaped the relative path limit"); } catch (ArgumentException) { }
    }
    public static void WindowsPhysicalNamesAreRejectedBeforeARecipeIsAccepted()
    {
        foreach (var locale in new[] { "con", "prn", "aux", "nul", "com1", "com9", "lpt1", "lpt9" })
            try { _ = new ResourceGeneration(World, ResourceOutputFormat.LanguageFiles, "", [Language(locale)]); throw new InvalidOperationException("device locale accepted: " + locale); } catch (ArgumentException) { }
        foreach (var component in new[] { "NUL", "CON.txt", "COM¹.bin", "LPT²", "CONIN$", "CONOUT$.json", "CLOCK$", "cOn .json", ".nbtcli-writer.lock", new string('a', 256) })
        {
            try { _ = new ResourceGeneration(World, ResourceOutputFormat.DirectoryPack, component, [Language()], new(34, 0)); throw new InvalidOperationException("unsafe directory component accepted: " + component); } catch (ArgumentException) { }
            try { _ = new ResourceGeneration(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 1), new("assets/test/" + component, PlanTests.Postimage, 0)])); throw new InvalidOperationException("unsafe merge component accepted: " + component); } catch (ArgumentException) { }
        }
        var allowed = new ResourceGeneration(World, ResourceOutputFormat.LanguageFiles, "", [Language("console"), Language("com0"), Language("lpt0")]);
        Require(allowed.Outputs.Length == 3, "physical validation does not reject ordinary names merely sharing a device prefix");
    }
    public static void SemanticOutputsAndCanonicalOrder()
    {
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var generation = Generation(format); var plan = Plan(generation);
            Require(plan.PlanHash == Plan(Generation(format, true)).PlanHash, "resource semantics are independent of language/key enumeration order");
            Require(plan.Operations.Single() is ResourcePatch { Targets.IsEmpty: true } && plan.Outputs.Length == (format == ResourceOutputFormat.LanguageFiles ? 2 : format == ResourceOutputFormat.ZipPack ? 1 : 4), "resource generation declares the full physical set without borrowing component mutation targets");
            Require(generation.Languages[0].Locale == "en_us" && generation.Languages[0].Values[0].Key == "map.a", "the immutable recipe has canonical locale and key order");
            Require(generation.Outputs.All(artifact => artifact.WorldId == World && artifact.ContainerEntry is null), "output declarations bind one physical access scope");
        }
        var inputs = new[] { Language() }; var frozen = new ResourceGeneration(World, ResourceOutputFormat.LanguageFiles, "", inputs);
        inputs[0] = Language("other"); Require(frozen.Languages.Single().Locale == "zh_tw", "caller collection mutation cannot retarget a resource recipe");
        var changed = new ResourceGeneration(World, ResourceOutputFormat.LanguageFiles, "", [Language(text: "Changed")]);
        Require(Plan(frozen).PlanHash != Plan(changed).PlanHash, "every decoded resource value participates in plan identity");
    }
    public static void RejectsAuthorityEscapesAndBoundedInputs()
    {
        void Refuse(Func<ResourceGeneration> create) { try { _ = create(); throw new InvalidOperationException("unsafe resource recipe was accepted"); } catch (ArgumentException) { } }
        Refuse(() => new(Guid.Empty, ResourceOutputFormat.LanguageFiles, "", [Language()]));
        Refuse(() => new(World, (ResourceOutputFormat)99, "", [Language()]));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "../level.dat", [Language()]));
        Refuse(() => new(World, ResourceOutputFormat.ZipPack, "level.dat", [Language()], new(34, 0)));
        Refuse(() => new(World, ResourceOutputFormat.DirectoryPack, "../data", [Language()], new(34, 0)));
        Refuse(() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()]));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [Language()], new(34, 0)));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [Language()], merge: Merge()));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", []));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [Language("../en_us")]));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [Language(), Language()]));
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [new("zh_tw", [new("key", "one"), new("key", "two")])]));
        foreach (var path in new[] { "../escape.png", "data/test/function/start.mcfunction", "assets/a/../escape.png", "assets/test/a:b", "assets/test/\ud800" })
            Refuse(() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 1), new(path, PlanTests.Postimage, 1)])));
        Refuse(() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), new(PlanTests.Preimage, [new("opaque.bin", PlanTests.Preimage, 1)])));
        Refuse(() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, -1)])));
        Refuse(() => new(World, ResourceOutputFormat.ZipPack, "resources.zip", [Language()], new(34, 0), new(PlanTests.Preimage, [new("pack.mcmeta", PlanTests.Preimage, 67_108_864), new("opaque.bin", PlanTests.Postimage, 1)])));
        var largeText = new string('x', 1000);
        Refuse(() => new(World, ResourceOutputFormat.LanguageFiles, "", [new("en_us", Enumerable.Range(0, 40_000).Select(index => new ResourceLanguageEntry("key." + index, largeText)).ToImmutableArray())]));
        var plan = Plan(Generation());
        try { _ = new PatchPlan(World, plan.SnapshotId, plan.ProfileVersion, plan.PolicyVersion, [.. plan.Operations, PlanTests.Edit()], [.. plan.Preconditions, new(PlanTests.Artifact, ArtifactImage.Present(PlanTests.Preimage))], [.. plan.Outputs, new(PlanTests.Artifact, OutputIntent.Write, ["scope"])], []); throw new InvalidOperationException("resource generation mixed with gameplay typed edit"); } catch (ArgumentException) { }
        try { _ = Plan(Generation(), "world-prepare-v1"); throw new InvalidOperationException("old semantic policy authorized resource generation"); } catch (ArgumentException) { }
    }
    public static void StrictExchangeAndReceipts()
    {
        foreach (var format in Enum.GetValues<ResourceOutputFormat>())
        {
            var plan = Plan(Generation(format)); var json = PatchPlanJson.Write(plan); var roundtrip = PatchPlanJson.Read(json);
            Require(roundtrip.PlanHash == plan.PlanHash && PatchPlanJson.Write(roundtrip) == json, "new resource payload preserves UTF16 values, complete outputs and canonical wire identity");
            var staged = new PreparedManifest(plan, plan.Outputs.Select(output => new PreparedArtifact(output.Artifact, "stage/" + output.Artifact.RelativePath, ArtifactImage.Present(PlanTests.Postimage), output.RequiredChecks.Select(check => new ValidationCheck(check, true, PlanTests.Preimage)))));
            var receipt = staged.Receipt(plan, 1, [new("resources", OperationOutcome.Applied, null, plan.Outputs.Select(output => new ImageProof(ImageProofKind.Postimage, output.Artifact, ArtifactImage.Present(PlanTests.Postimage), ArtifactImage.Present(PlanTests.Postimage))))], true);
            Require(ApplyReceiptJson.Read(ApplyReceiptJson.Write(receipt), plan, staged).Requested == 1 && receipt.Results.Single().Proofs.Length == plan.Outputs.Length, "one resource operation counts once while proving every promised physical output");
            var strictPlan = Plan(Generation(format, text: "Safe")); var strictJson = PatchPlanJson.Write(strictPlan);
            Require(PatchPlanJson.Read(JsonNode.Parse(strictJson)!.ToJsonString()).PlanHash == strictPlan.PlanHash, "negative JSON edits start from a roundtripping scalar-safe positive control");
            foreach (var field in new[] { "format", "languages", "destination", "version" })
            {
                var node = JsonNode.Parse(strictJson)!; ((JsonObject)node["data"]!["operations"]![0]!["payload"]!).Remove(field); RejectJson(() => PatchPlanJson.Read(node.ToJsonString()));
            }
            var unknown = JsonNode.Parse(strictJson)!; unknown["data"]!["operations"]![0]!["payload"]!["rawBytes"] = "arbitrary"; RejectJson(() => PatchPlanJson.Read(unknown.ToJsonString()));
            var version = JsonNode.Parse(strictJson)!; version["data"]!["operations"]![0]!["payload"]!["version"] = 2;
            try { _ = PatchPlanJson.Read(version.ToJsonString()); throw new InvalidOperationException("unknown generation version accepted"); } catch (NotSupportedException) { }
        }
    }
    public static void AcceptedFiveOperationsKeepExactHashAndWire(string fixtureRoot)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "contracts", "pre-resource-operations.json")));
        var expected = baseline.RootElement.GetProperty("operations").EnumerateArray().ToDictionary(item => item.GetProperty("kind").GetString()!, StringComparer.Ordinal);
        foreach (var plan in PlanExchangeTests.Examples())
        {
            var kind = plan.Operations.Single().Kind; var record = expected[kind.ToString()];
            Require((int)kind < 5 && record.GetProperty("planHash").GetString() == plan.PlanHash.Hex
                && record.GetProperty("wireSha256").GetString() == Hash256.Compute(Encoding.UTF8.GetBytes(PatchPlanJson.Write(plan))).Hex, "accepted five kinds keep their exact canonical hashes and complete JSON bytes");
        }
    }
}

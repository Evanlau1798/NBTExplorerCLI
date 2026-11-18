using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static NBTExplorerCLI.Verification.CliScanTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class CliAgentTests
{
    private static Invocation Call(string work, string world, string action, params string[] args)
        => Run(work, ["agent", action, world, "--state-root", Path.Combine(work, "state"), .. args]);
    private static JsonNode Answer(JsonDocument setup)
    {
        var node = JsonNode.Parse(setup.RootElement.GetProperty("data").GetProperty("questionnaire").GetRawText())!;
        var configuration = node["data"]!["configuration"]!; configuration["agentName"] = "外部 agent🙂";
        configuration["sourceLanguage"] = "en_us"; configuration["targetLanguage"] = "ja_jp"; configuration["preferredItemsPerPage"] = 2;
        return node;
    }
    private static string FileInput(string work, string json)
    { var path = Path.Combine(work, "answers.json"); File.WriteAllText(path, json, new UTF8Encoding(false)); return path; }
    public static void QuestionnaireSubmissionPersistsRawInputAndDefaultsWithoutStartingASession()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var questionnaire = Path.Combine(work, "questionnaire.json"); using var setup = Success(Call(work, world, "setup", "--output", questionnaire));
            Require(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(questionnaire)), JsonNode.Parse(setup.RootElement.GetProperty("data").GetProperty("questionnaire").GetRawText())), "setup exports the complete versioned questionnaire");
            var raw = Answer(setup).ToJsonString(); var file = FileInput(work, raw); var original = File.ReadAllBytes(file);
            using var accepted = Success(Call(work, world, "submit", "--request", file));
            using var replay = Success(Call(work, world, "submit", "--request", file));
            using var status = Success(Call(work, world, "status")); var data = status.RootElement.GetProperty("data");
            Require(data.GetProperty("revision").GetInt64() == 0 && data.GetProperty("configuration").GetProperty("agentName").GetString() == "外部 agent🙂"
                && replay.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("revision").GetInt64() == 0, "restart and exact resend retain one accepted configuration revision");
            Require(!Directory.Exists(Path.Combine(work, "state", "sessions")) && File.ReadAllBytes(file).SequenceEqual(original) && File.Exists(questionnaire), "submit retains input files and does not silently start translation");
            using var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(work, "state", "agent", "configuration.json")));
            var retained = stored.RootElement.GetProperty("data").GetProperty("submissions")[0].GetProperty("inputBase64").GetString()!;
            Require(Convert.FromBase64String(retained).SequenceEqual(original), "accepted questionnaire bytes are retained independently of the caller's file");
            var id = Guid.NewGuid().ToString();
            using var started = Success(Run(work, "translate", "start", world, "--session-id", id, "--types", "McFunction", "--state-root", Path.Combine(work, "state"), "--ui-locale", "zh-TW"));
            using var page = Success(Run(work, "translate", "export", world, "--session-id", id, "--state-root", Path.Combine(work, "state")));
            var exchange = page.RootElement.GetProperty("data").GetProperty("page").GetProperty("data");
            Require(exchange.GetProperty("sourceLocale").GetString() == "en_us" && exchange.GetProperty("targetLocale").GetString() == "ja_jp" && exchange.GetProperty("slots").GetArrayLength() == 2,
                "new sessions use configured languages and preferred page size independently of UI locale");
            using var updatedSetup = Success(Call(work, world, "setup")); var updated = Answer(updatedSetup); updated["data"]!["configuration"]!["targetLanguage"] = "zh_cn";
            using var update = Success(Call(work, world, "submit", "--request", FileInput(work, updated.ToJsonString())));
            using var oldReplay = Success(Call(work, world, "submit", "--request", FileInput(work, raw)));
            Require(oldReplay.RootElement.GetProperty("data").GetProperty("receipt").GetProperty("revision").GetInt64() == 0
                && oldReplay.RootElement.GetProperty("data").GetProperty("revision").GetInt64() == 1
                && oldReplay.RootElement.GetProperty("data").GetProperty("configuration").GetProperty("targetLanguage").GetString() == "zh_cn", "historical replay returns its old receipt without reverting newer preferences");
            using var unchangedPage = Success(Run(work, "translate", "export", world, "--session-id", id, "--state-root", Path.Combine(work, "state")));
            Require(unchangedPage.RootElement.GetProperty("data").GetProperty("page").GetProperty("data").GetProperty("targetLocale").GetString() == "ja_jp", "configuration updates do not rewrite existing sessions");
            var overrideId = Guid.NewGuid().ToString();
            using var overridden = Success(Run(work, "translate", "start", world, "--session-id", overrideId, "--types", "McFunction", "--source-language", "fr_fr", "--target-language", "ko_kr", "--page-size", "1", "--state-root", Path.Combine(work, "state")));
            using var overridePage = Success(Run(work, "translate", "export", world, "--session-id", overrideId, "--state-root", Path.Combine(work, "state")));
            var explicitPage = overridePage.RootElement.GetProperty("data").GetProperty("page").GetProperty("data");
            Require(explicitPage.GetProperty("sourceLocale").GetString() == "fr_fr" && explicitPage.GetProperty("targetLocale").GetString() == "ko_kr" && explicitPage.GetProperty("slots").GetArrayLength() == 1, "explicit new-session options override configured defaults");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void InvalidAnswersAndRevisionConflictsPreserveTheAcceptedConfiguration()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var setup = Success(Call(work, world, "setup")); var answer = Answer(setup); var file = FileInput(work, answer.ToJsonString());
            using var accepted = Success(Call(work, world, "submit", "--request", file));
            var stale = answer.DeepClone(); stale["data"]!["operationId"] = Guid.NewGuid().ToString();
            Require(Call(work, world, "submit", "--request", FileInput(work, stale.ToJsonString())).Exit == 3, "a new operation with an old expected revision conflicts");
            var changedReplay = answer.DeepClone(); changedReplay["data"]!["configuration"]!["targetLanguage"] = "zh_cn";
            Require(Call(work, world, "submit", "--request", FileInput(work, changedReplay.ToJsonString())).Exit == 3, "same operation identity cannot change the submitted bytes");
            foreach (var (field, value) in new[] { ("maxLinesPerPage", 0), ("maxLinesPerPage", 501), ("lowConfidenceThreshold", 11), ("preferredItemsPerPage", 100001) })
            {
                using var current = Success(Call(work, world, "setup")); var invalid = Answer(current); invalid["data"]!["configuration"]![field] = value;
                Require(Call(work, world, "submit", "--request", FileInput(work, invalid.ToJsonString())).Exit == 2, "configuration bounds reject " + field);
            }
            var unknown = answer.DeepClone(); unknown["data"]!["configuration"]!["unexpected"] = true;
            Require(Call(work, world, "submit", "--request", FileInput(work, unknown.ToJsonString())).Exit == 5, "unknown settings fields are malformed instead of silently discarded");
            var version = answer.DeepClone(); version["schemaVersion"] = 2;
            Require(Call(work, world, "submit", "--request", FileInput(work, version.ToJsonString())).Exit == 4, "unknown questionnaire versions are unsupported");
            using var status = Success(Call(work, world, "status")); Require(status.RootElement.GetProperty("data").GetProperty("revision").GetInt64() == 0, "all rejections retain the accepted revision");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ExistingExportsUnownedStateAndForeignWorldAnswersAreProtected()
    {
        var world = WorldQueryTests.CreateWorld(); var other = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var file = FileInput(work, "keep"); Require(Call(work, world, "setup", "--output", file).Exit == 3 && File.ReadAllText(file) == "keep" && !Directory.Exists(Path.Combine(work, "state")), "existing questionnaire output is checked before creating tool state");
            using var setup = Success(Call(work, world, "setup")); var answer = FileInput(work, Answer(setup).ToJsonString());
            var foreign = Run(work, "agent", "submit", other, "--request", answer, "--state-root", Path.Combine(work, "foreign-state"));
            Require(foreign.Exit == 3 && !File.Exists(Path.Combine(work, "foreign-state", "agent", "configuration.json")), "answers bind the world that created their questionnaire");
            var unowned = Path.Combine(work, "unowned"); Directory.CreateDirectory(Path.Combine(unowned, "agent")); var marker = Path.Combine(unowned, "agent", "user.txt"); File.WriteAllText(marker, "keep");
            Require(Run(work, "agent", "setup", world, "--state-root", unowned).Exit == 5 && File.ReadAllText(marker) == "keep", "setup does not adopt or overwrite unowned settings contents");
        }
        finally { Directory.Delete(world, true); Directory.Delete(other, true); Directory.Delete(work, true); }
    }
    public static void HelpAndMissingConfigurationAreExplicitAndReadOnlyForTheWorld()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var help = Success(Run(work, "agent", "help"));
            Require(help.RootElement.GetProperty("data").GetProperty("subcommands").EnumerateArray().Any(item => item.GetString() == "submit"), "machine help exposes actual agent actions");
            Require(Call(work, world, "status").Exit == 5 && Call(work, world, "unknown").Exit == 2, "missing configuration differs from an unknown action");
            using var english = Success(Call(work, world, "setup", "--ui-locale", "en-US", "--source-language", "en_us", "--target-language", "zh_tw"));
            var questionnaire = english.RootElement.GetProperty("data").GetProperty("questionnaire").GetProperty("data");
            Require(questionnaire.GetProperty("instructions")[0].GetString()!.StartsWith("Fill in", StringComparison.Ordinal)
                && questionnaire.GetProperty("configuration").GetProperty("targetLanguage").GetString() == "zh_tw", "UI locale changes instructions while retaining the requested translation language");
            Require(!Directory.Exists(Path.Combine(work, "state", "sessions")) && !Directory.Exists(Path.Combine(work, "state", "journals")), "agent management never stages or applies world edits");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void ABlockedAtomicUpdateOffersAnExactRetryAndPreservesConfiguration()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var setup = Success(Call(work, world, "setup")); using var first = Success(Call(work, world, "submit", "--request", FileInput(work, Answer(setup).ToJsonString())));
            using var followup = Success(Call(work, world, "setup")); var answer = Answer(followup); answer["data"]!["configuration"]!["targetLanguage"] = "zh_cn";
            var file = FileInput(work, answer.ToJsonString()); var config = Path.Combine(work, "state", "agent", "configuration.json"); var before = File.ReadAllBytes(config);
            Invocation rejected;
            using (var locked = File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read)) rejected = Call(work, world, "submit", "--request", file);
            using var error = JsonDocument.Parse(rejected.Output); var data = error.RootElement.GetProperty("data");
            Require(rejected.Exit != 0 && File.ReadAllBytes(config).SequenceEqual(before), "a real blocked replacement preserves the prior configuration bytes");
            Require(error.RootElement.GetProperty("error").GetProperty("code").GetString() == "AGENT_STATE_IO" && data.ValueKind == JsonValueKind.Object && data.GetProperty("repair").GetString() == "retry-same-questionnaire"
                && data.GetProperty("operationId").GetGuid().ToString() == answer["data"]!["operationId"]!.GetValue<string>(), "publication errors retain the identity and exact-retry instruction: " + rejected.Output);
            using var accepted = Success(Call(work, world, "submit", "--request", file));
            Require(accepted.RootElement.GetProperty("data").GetProperty("revision").GetInt64() == 1, "exact retry after releasing the lock commits only once");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
    public static void OversizedQuestionnairesAreRejectedBeforePublication()
    {
        var world = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            using var setup = Success(Call(work, world, "setup")); var answer = Answer(setup);
            answer["data"]!["instructions"] = new JsonArray(Enumerable.Range(0, 20).Select(_ => (JsonNode?)JsonValue.Create(new string('x', 1000))).ToArray());
            var result = Call(work, world, "submit", "--request", FileInput(work, answer.ToJsonString()));
            Require(result.Exit == 6 && !File.Exists(Path.Combine(work, "state", "agent", "configuration.json")), "aggregate questionnaire input budget returns ResourceLimit and publishes no configuration");
        }
        finally { Directory.Delete(world, true); Directory.Delete(work, true); }
    }
}

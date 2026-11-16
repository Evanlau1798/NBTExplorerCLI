using System.Collections.Immutable;
using System.Text;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.SessionExchangeTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.WorldQueryTests;

namespace Next.Verification;

internal static class SessionScannerTests
{
    public static void RealQuerySlotsKeepLocalBindingsAndProtectedSource() => Scan().GetAwaiter().GetResult();
    private static async Task Scan()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild(); var sessionRoot = ScratchDirectory.CreateChild();
        try
        {
            var command = "tellraw @s {\"text\":\"literal {{T0}} | ‖§‖\",\"color\":\"red\",\"extra\":[{\"text\":\"Tail\"}],\"clickEvent\":{\"action\":\"run_command\",\"value\":\"{{T0}}\"}}";
            Put(root, Function(0), Encoding.UTF8.GetBytes(command));
            var inventory = new WorldInventory(root, World); var before = Hashes(inventory);
            var query = await new WorldQueryService(inventory, new FileSystemScanCacheStore(cache)).QueryAsync(new(new(new([], [], [], [Function(0)])), WorldQueryPurpose.Session));
            var sources = query.Snapshot!.ReadSources().ToArray(); var expected = sources.SelectMany(source => source.Text?.Occurrences ?? []).ToArray();
            Require(expected.Select(slot => slot.Text).SequenceEqual(new[] { "literal {{T0}} | ‖§‖", "Tail" }) && !query.Snapshot.Manifest.CoverageComplete,
                "real command adapter extracts exact text while protected clickEvent preserves unresolved reference coverage");
            var service = new TranslationSessionService(new FileSystemTranslationSessionStore(sessionRoot));
            service.Start(query.Snapshot, SessionId, "en_us", "zh_tw", Guid.NewGuid());
            var state = service.Read(SessionId);
            Require(state.Source.SnapshotId == query.Snapshot.Manifest.SnapshotId && !state.Source.CoverageComplete
                && state.Slots.Select(item => item.Slot.Address).SequenceEqual(expected.Select(item => item.Address))
                && state.Slots.Select(item => item.Slot.SourceTextHash).SequenceEqual(expected.Select(item => TextRevision.Compute(item.Text))), "verified query snapshot, exact syntax addresses and source revisions remain bound in local session state");
            var page = service.Export(SessionId, 0); var json = SessionExchangeJson.WritePage(page);
            Require(!json.Contains("clickEvent", StringComparison.Ordinal) && !json.Contains("run_command", StringComparison.Ordinal) && !json.Contains("nodePath", StringComparison.Ordinal), "protected source structure and writable addresses never become reply fields");
            var template = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            var reply = template with { Slots = template.Slots.Select((item, index) => item with { State = SessionReplyState.Translated, Text = index == 0 ? "字面 {{T1}} | ‖§‖" : "尾端" }).ToImmutableArray() };
            service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(reply)));
            var validated = service.ValidatedPage(SessionId, 0);
            Require(validated.Valid && validated.Value!.Translations.Select(item => item.Reply.Text).SequenceEqual(new[] { "字面 {{T1}} | ‖§‖", "尾端" })
                && validated.Value.Translations.Select(item => item.Slot.Address).SequenceEqual(expected.Select(item => item.Address)), "shared validator returns distinct decoded slots with stable original syntax positions, without marker substitution");
            var after = Hashes(inventory);
            Require(before.Count == after.Count && before.All(pair => after[pair.Key] == pair.Value) && Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, Function(0)))) == command,
                "scan/session/export/import keeps the entire world file set and raw protected event/style bytes unchanged");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(sessionRoot, true); }
    }
    private static Dictionary<string, Hash256> Hashes(IWorldInventory inventory) => inventory.Enumerate().Entries.ToDictionary(entry => entry.RelativePath, entry => Hash256.Compute(inventory.Read(new(World, entry.RelativePath, ArtifactKind.Text))));
    public static void LanguageArgumentsUseProfileContract() => Language().GetAwaiter().GetResult();
    private static async Task Language()
    {
        var root = CreateWorld(); var cache = ScratchDirectory.CreateChild(); var stateRoot = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "packs/resource/pack.mcmeta", Encoding.UTF8.GetBytes(ResourcePack));
            var path = "packs/resource/assets/test/lang/en_us.json";
            Put(root, path, Encoding.UTF8.GetBytes("{\"number\":\"%d and %.2f\",\"literal\":\"literal {{T0}}\"}"));
            var snapshot = (await new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)).QueryAsync(new(new(new([], [], [], [path])), WorldQueryPurpose.Session))).Snapshot!;
            var store = new FileSystemTranslationSessionStore(stateRoot); var service = new TranslationSessionService(store); service.Start(snapshot, SessionId, "en_us", "zh_tw", Guid.NewGuid());
            var state = service.Read(SessionId);
            Require(state.Slots.All(item => item.Format == PlaceholderFormat.NormalizedPercent), "language value source roles carry their verified numeric-percent grammar into local session state");
            var page = service.Export(SessionId, 0); var template = SessionExchangeJson.ReadReply(SessionExchangeJson.ReplyTemplate(page, Guid.NewGuid()));
            var number = page.Slots.Single(slot => slot.SourceText == "%d and %.2f").SlotId;
            var translated = template with { Slots = template.Slots.Select(slot => slot.SlotId == number ? slot with { State = SessionReplyState.Translated, Text = "%2$s 與 %1$s" } : slot).ToImmutableArray() };
            var invalid = translated with { Slots = translated.Slots.Select(slot => slot.SlotId == number ? slot with { Text = "%1$s" } : slot).ToImmutableArray() };
            var head = store.Read(SessionId)!;
            CandidateIoTests.Reject<SessionReplyRejectedException>(() => service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(invalid))));
            Require(store.Read(SessionId)!.SequenceEqual(head), "missing formatting arguments cannot advance a session revision");
            service.Submit(SessionId, Encoding.UTF8.GetBytes(SessionExchangeJson.WriteReply(translated)));
            Require(service.ValidatedPage(SessionId, 0).Valid, "indexed reordering of the same normalized arguments passes import and stored-input validation");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(stateRoot, true); }
    }
}

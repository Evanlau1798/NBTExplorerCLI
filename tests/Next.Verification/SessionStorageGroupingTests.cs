using System.Collections.Immutable;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;
using static Next.Verification.NbtItemTextTests;

namespace Next.Verification;

internal static class SessionStorageGroupingTests
{
    public static void ExplicitRulesSurviveQueryCacheAndSessionRestart()
    {
        var root = ScratchDirectory.CreateChild(); var cache = ScratchDirectory.CreateChild(); var stateRoot = ScratchDirectory.CreateChild();
        try
        {
            Put(root, "level.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Level(4325, WorldMetadataTests.Dimensions), NbtCompression.GZip));
            var artifact = new ArtifactAddress(World, "data/command_storage_golden.dat", ArtifactKind.Nbt);
            var content = C(E("golden:test", C(E("text", S("LiteralOne")), E("extra", S("LiteralTwo")),
                E("message", new NbtList(NbtType.Compound, [C(E("text", S("One"))), C(E("text", S("Two")))])),
                E("other", new NbtList(NbtType.Compound, [C(E("text", S("OtherOne"))), C(E("text", S("OtherTwo")))])))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(4325)), E("data", C(E("contents", content)))));
            Put(root, artifact.RelativePath, CompressedNbt.WriteDocument(document, NbtCompression.GZip));
            TypedPath Path(string key) => new(new[] { "data", "contents", "golden:test", key }.Select(PathSegment.ForKey));
            var rules = new Dictionary<ArtifactAddress, IReadOnlyList<StorageTextRule>> { [artifact] = [new(Path("text"), StorageTextKind.Literal), new(Path("extra"), StorageTextKind.Literal), new(Path("message"), StorageTextKind.TextComponent), new(Path("other"), StorageTextKind.TextComponent)] };
            var query = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache));
            var request = new WorldQueryRequest(new(SnapshotScope.All), StorageRules: rules);
            var first = query.QueryAsync(request).GetAwaiter().GetResult(); var warm = query.QueryAsync(request).GetAwaiter().GetResult();
            Require(first.Snapshot!.Manifest.CoverageComplete && warm.Snapshot!.Manifest.CoverageComplete && warm.Telemetry.ScannedArtifacts == 0, "explicit component roots accompany a complete real query and a verified warm cache hit");
            var id = Guid.NewGuid(); var service = new TranslationSessionService(new FileSystemTranslationSessionStore(stateRoot)); service.Start(warm.Snapshot!, id, "en_us", "zh_tw", Guid.NewGuid());
            service = new(new FileSystemTranslationSessionStore(stateRoot)); var slots = service.Read(id).Slots.ToDictionary(item => item.Slot.Id.Digest, item => item.Slot.SourceText); var groups = service.Groups(id);
            var texts = groups.Select(group => string.Join(',', group.Slots.Select(slot => slots[slot]))).Order(StringComparer.Ordinal).ToArray();
            Require(texts.SequenceEqual(new[] { "LiteralOne", "LiteralTwo", "One,Two", "OtherOne,OtherTwo" }.Order(StringComparer.Ordinal)), "literal text/extra rules stay independent while each declared arbitrary component root retains its ordered array fragments after restart");
            var state = service.Read(id); var binding = state.Source.ContextRoots[0];
            var foreign = new SourceAddress(new(Guid.NewGuid(), artifact.RelativePath, artifact.Kind), binding.Container.NodePath, "command_storage.component");
            var invalid = new SessionStartRequest(Guid.NewGuid(), state.Source with { ContextRoots = [binding with { Container = foreign }] }, "en_us", "zh_tw", state.Policy, state.Slots.Select(item => item.Slot).ToImmutableArray());
            CandidateIoTests.Reject<System.Text.Json.JsonException>(() => service.Start(invalid, Guid.NewGuid()));
            CandidateIoTests.Reject<System.Text.Json.JsonException>(() => service.Start(invalid with { Source = state.Source with { ContextRoots = [binding, binding] } }, Guid.NewGuid()));
            var old = invalid with { SessionId = Guid.NewGuid(), Source = state.Source with { ContextRoots = default } };
            service.Start(old, Guid.NewGuid());
            CandidateIoTests.Reject<InvalidOperationException>(() => service.Groups(old.SessionId));
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); Directory.Delete(stateRoot, true); }
    }
}

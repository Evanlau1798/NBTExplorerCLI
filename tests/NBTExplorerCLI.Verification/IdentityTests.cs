using NBTExplorerCLI.Domain;

namespace NBTExplorerCLI.Verification;

internal static class IdentityTests
{
    private static readonly Guid World = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static SourceAddress Address(string file = "level.dat", string key = "text")
        => new(new(World, file, ArtifactKind.Nbt), new([PathSegment.ForKey(key)]), "literal");

    public static void ExactLocationsAndRevisions()
    {
        var one = SourceIdentity.Occurrence(Address());
        var two = SourceIdentity.Occurrence(Address("other.dat"));
        Require(one.IsValid && one.Digest != two.Digest, "不同來源的同文位置必須有不同完整 digest");
        Require(SourceIdentity.Occurrence(Address(key: "\ud800")).Digest != SourceIdentity.Occurrence(Address(key: "\ufffd")).Digest,
            "canonical encoding 不可用 replacement character 取代 surrogate code unit");
        var firstPack = new ArtifactAddress(World, "a.zip", ArtifactKind.Archive, "assets/x/lang/en_us.json");
        var secondPack = new ArtifactAddress(World, "b.zip", ArtifactKind.Archive, "assets/x/lang/en_us.json");
        Require(SourceIdentity.Artifact(firstPack).Digest != SourceIdentity.Artifact(secondPack).Digest, "同 entry 不同包不能合併");
        var first = new SourceAddress(new(World, "x.mcfunction", ArtifactKind.McFunction), new([PathSegment.ForIndex(0)]),
            "command.text", syntax: new("java", 1, 10, 4, new string('0', 64), "utf-8", "lf"));
        var revised = new SourceAddress(first.Artifact, first.NodePath, first.SemanticRole,
            syntax: new("java", 1, 20, 8, new string('1', 64), "utf-8", "lf"));
        Require(SourceIdentity.Occurrence(first).Digest == SourceIdentity.Occurrence(revised).Digest,
            "原文 hash／span 提示改變不應改掉相同語法節點的位置 ID");
    }

    public static void GroupOrderAndWorldIdentity()
    {
        var address = Address(key: "book");
        var group = SourceIdentity.Group("structural.book", address, 1);
        var fork = new SourceAddress(new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "level.dat", ArtifactKind.Nbt),
            address.NodePath, address.SemanticRole);
        Require(group.Digest != SourceIdentity.Group("structural.book", fork, 1).Digest, "GroupId 必須含 world 身分");
        var a = SourceIdentity.Occurrence(Address(key: "page-a"));
        var b = SourceIdentity.Occurrence(Address(key: "page-b"));
        Require(group.Digest == SourceIdentity.Group("structural.book", address, 1).Digest, "成員增加不改容器 GroupId");
        Require(SourceIdentity.Members(group, [a], 1).Digest != SourceIdentity.Members(group, [a, b], 1).Digest
            && SourceIdentity.Members(group, [a, b], 1).Digest != SourceIdentity.Members(group, [b, a], 1).Digest,
            "memberDigest 必須包含有語義順序的成員集合");
        var chunkA = new SourceAddress(new(World, "region/r.0.0.mca", ArtifactKind.Region), address.NodePath,
            "book", "minecraft:overworld", new(0, 0));
        var chunkB = new SourceAddress(chunkA.Artifact, address.NodePath, "book", chunkA.Dimension, new(1, 0));
        Require(SourceIdentity.Group("structural.book", chunkA, 1).Digest != SourceIdentity.Group("structural.book", chunkB, 1).Digest,
            "同 region 內不同 chunk 的容器不可碰撞");
    }

    public static void SnapshotAndMemorySemantics()
    {
        var hash = Hash256.Compute([1, 2, 3]);
        var sources = new[] { new ManifestSource(Address("a.dat").Artifact, hash, SourceOutcome.Success),
            new ManifestSource(Address("b.dat").Artifact, hash, SourceOutcome.Success) };
        var versions = new InterpretationVersions("1", "1", "1", "1");
        var one = SourceIdentity.Snapshot(World, sources, new(["b:dim", "a:dim"], [], ["book", "sign"], []), versions);
        var two = SourceIdentity.Snapshot(World, sources.Reverse(), new(["a:dim", "b:dim"], [], ["sign", "book"], []), versions);
        Require(one.Digest == two.Digest && one.IsValid, "snapshot 不可依列舉順序或 filter set 順序改變");
        Require(one.Digest != SourceIdentity.Snapshot(World, sources, new(["a:dim", "b:dim"], [], ["sign", "book"], []),
            versions with { Rules = "2" }).Digest, "規則版本必須使 snapshot ID 失效");
        Require(SourceIdentity.Memory("same", "en_us", "zh_tw", "name", "", "1", "1").Digest
            != SourceIdentity.Memory("same", "en_us", "zh_tw", "dialogue", "", "1", "1").Digest,
            "翻譯記憶不能把不同語境的同文強制同譯");
    }

    public static void CollisionGuard()
    {
        var one = SourceIdentity.Artifact(Address().Artifact);
        var registry = new IdentityRegistry();
        Require(registry.Register(one) == IdentityRegistration.Added && registry.Register(one) == IdentityRegistration.Existing,
            "完整 canonical identity 重送應可辨識");
        var conflictingClaim = new StableIdentity(one.Kind, one.Digest, [1, 2, 3]);
        Require(registry.Register(conflictingClaim) == IdentityRegistration.Collision, "相同 digest 不同 canonical 內容必須停止，不能覆寫");
        Require(new IdentityRegistry().Register(conflictingClaim) == IdentityRegistration.Invalid, "未知的假 digest 不得登錄");
    }

    public static void ParentAndRevisionBoundaries()
    {
        var address = Address();
        var occurrence = SourceIdentity.Occurrence(address);
        var slot = SourceIdentity.Slot(occurrence, TypedPath.Root, "fragment");
        Require(slot.IsValid && slot.Digest != SourceIdentity.Slot(occurrence, TypedPath.Root, "other").Digest
            && slot.Digest != SourceIdentity.Slot(occurrence, new([PathSegment.ForKey("")]), "fragment").Digest,
            "slot component path 與 role 都是持久身分");
        Reject(() => SourceIdentity.Slot(SourceIdentity.Artifact(address.Artifact), TypedPath.Root, "fragment"));
        Reject(() => SourceIdentity.Slot(new(occurrence.Kind, new(new string('0', 64)), occurrence.Canonical), TypedPath.Root, "fragment"));
        var group = SourceIdentity.Group("structural.book", address, 1);
        Require(SourceIdentity.Members(group, [slot], 1).Digest != SourceIdentity.Members(group, [slot], 2).Digest,
            "成員不變但 revision 改變仍須產生新 memberDigest");
        Reject(() => SourceIdentity.Members(group, [group], 1));
        var source = new ManifestSource(address.Artifact, Hash256.Compute([1]), SourceOutcome.Success);
        var versions = new InterpretationVersions("1", "1", "1", "1");
        var snapshot = SourceIdentity.Snapshot(World, [source], SnapshotScope.All, versions);
        Require(snapshot.Digest != SourceIdentity.Snapshot(World, [source with { ContentHash = Hash256.Compute([2]) }],
            SnapshotScope.All, versions).Digest, "來源內容改變使 snapshot 過期，位置 ID 仍獨立");
        foreach (var changed in new[] { versions with { Codec = "2" }, versions with { Profile = "2" },
            versions with { Rules = "2" }, versions with { Policy = "2" } })
            Require(snapshot.Digest != SourceIdentity.Snapshot(World, [source], SnapshotScope.All, changed).Digest,
                "每一解釋版本都必須綁定 snapshot");
        Reject(() => SourceIdentity.Snapshot(World, [source, source], SnapshotScope.All, versions));
        Reject(() => SourceIdentity.Snapshot(World, [source with { ContentHash = null }], SnapshotScope.All, versions));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("非法身分 parent／manifest 必須拒絕");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

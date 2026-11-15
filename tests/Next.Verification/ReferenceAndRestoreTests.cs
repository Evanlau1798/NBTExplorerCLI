using Next.Domain;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class ReferenceAndRestoreTests
{
    public static void CompleteReferenceAuthorization()
    {
        var address = Address(new([PathSegment.ForKey("CustomName")]));
        var group = SourceIdentity.Group("inferred.name-references", address, 1);
        var name = new ReferenceMember(address, "Bob", "Alice", "synthetic.nbt-name", 1);
        var commandAddress = new SourceAddress(new(World, "data/test.mcfunction", ArtifactKind.McFunction),
            new([PathSegment.ForIndex(0)]), "command.name-reference", syntax: new("synthetic.command", 1, 0, 12,
                new string('0', 64), "utf-8", "lf"));
        var command = new ReferenceMember(commandAddress, "@e[name=Bob]", "@e[name=Alice]", "synthetic.command", 1);
        var members = new[] { name, command };
        var digest = SourceIdentity.Members(group, members.Select(member => member.Occurrence), 2).Digest;
        var synchronization = new ReferenceSynchronization(group, 2, digest, ReferenceCompleteness.Complete, members);
        Require(synchronization.Authorizes(members), "完整明列群組應可授權，跨artifact位置保留");
        Require(!synchronization.Authorizes([name]), "缺一引用不能宣告完整授權");
        Require(!synchronization.Authorizes([name, command, command]), "重複／額外引用不可授權");
        Require(!synchronization.Authorizes([name, new(commandAddress, command.ExpectedText, "@e[name=evil]", command.GrammarProfile, 1)]),
            "授權內也不能額外改成不同引用");
        Require(!new ReferenceSynchronization(group, 2, digest, ReferenceCompleteness.Unresolved, members).Authorizes(members),
            "未知macro引用完整性阻擋自動同步");
        Reject(() => new ReferenceSynchronization(group, 2, Hash256.Compute([0]), ReferenceCompleteness.Complete, members));
        Reject(() => new ReferenceSynchronization(group, 2, digest, ReferenceCompleteness.Complete, [name, name]));
        Reject(() => new ReferenceMember(commandAddress, "old", "new", "different-grammar", 1));
        var foreignAddress = new SourceAddress(new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "level.dat", ArtifactKind.Nbt),
            address.NodePath, address.SemanticRole);
        var foreignGroup = SourceIdentity.Group("inferred.name-references", foreignAddress, 1);
        var foreignDigest = SourceIdentity.Members(foreignGroup, members.Select(member => member.Occurrence), 2).Digest;
        Reject(() => new ReferenceSynchronization(foreignGroup, 2, foreignDigest, ReferenceCompleteness.Complete, members));
    }

    public static void RawRestorePreconditions()
    {
        var artifact = new ArtifactAddress(World, "level.dat", ArtifactKind.Nbt);
        var created = new ArtifactAddress(World, "data/new.dat", ArtifactKind.Nbt);
        var corrupt = new byte[] { 255, 0, 1 };
        var backup = new byte[] { 10, 0, 0, 0 };
        var restore = new ArtifactRestore(Hash256.Compute([9]),
            [new(artifact, ArtifactImage.Present(Hash256.Compute(backup))), new(created, ArtifactImage.Absent)],
            new Dictionary<ArtifactAddress, ArtifactImage>
            {
                [artifact] = ArtifactImage.Present(Hash256.Compute(corrupt)),
                [created] = ArtifactImage.Present(Hash256.Compute([1]))
            });
        Require(restore.VerifyPreimage(artifact, corrupt) && restore.VerifyRestoredImage(artifact, backup),
            "restore按raw hash驗證，不要求損壞現檔可parse");
        Require(!restore.VerifyPreimage(artifact, backup) && !restore.VerifyRestoredImage(artifact, corrupt), "不同pre/postimage必須拒絕");
        Require(restore.VerifyRestoredImage(created, null) && !restore.VerifyRestoredImage(created, []), "備份expectedAbsent明示刪除，空檔不等於不存在");
        Require(!restore.VerifyPreimage(new(World, "other.dat", ArtifactKind.Nbt), corrupt), "manifest外artifact不得還原");
        var absent = new ArtifactRestore(Hash256.Compute([9]), [new(artifact, ArtifactImage.Present(Hash256.Compute(backup)))],
            new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = ArtifactImage.Absent });
        Require(absent.VerifyPreimage(artifact, null) && !absent.VerifyPreimage(artifact, []), "缺檔還原須明示expectedAbsent");
    }

    public static void ExactReferenceCandidate()
    {
        var path = new TypedPath([PathSegment.ForKey("name")]);
        var address = Address(path);
        var member = new ReferenceMember(address, "Bob", "Alice", "synthetic.name", 1);
        var other = new ReferenceMember(Address(path, "other.dat"), "Bob", "Alice", "synthetic.name", 1);
        var group = SourceIdentity.Group("inferred.name", address, 1);
        var members = new[] { member, other };
        var digest = SourceIdentity.Members(group, members.Select(item => item.Occurrence), 1).Digest;
        var sync = new ReferenceSynchronization(group, 1, digest, ReferenceCompleteness.Complete, members);
        var source = new NbtDocument("root", new NbtCompound([new("name", new NbtString("Bob")), new("event", new NbtString("Bob"))]));
        var binding = Address(TypedPath.Root);
        var candidate = ReferenceSynchronizations.Apply(binding, source, sync, members);
        Require(NbtLocator.Resolve(candidate.Root, path).Value == new NbtString("Alice")
            && NbtLocator.Resolve(candidate.Root, new([PathSegment.ForKey("event")])).Value == new NbtString("Bob"), "reference同步只能改明列位置，不做全文same-string替換");
        Require(ReferenceSynchronizations.Verify(binding, source, candidate, sync, members), "合法精確引用候選應通過");
        var malicious = new NbtDocument("root", new NbtCompound([new("name", new NbtString("Alice")), new("event", new NbtString("evil"))]));
        Require(!ReferenceSynchronizations.Verify(binding, source, malicious, sync, members), "reference同步完整比對scope外protected projection");
        try { ReferenceSynchronizations.Apply(binding, source, sync, [member]); }
        catch (EditRejectedException) { return; }
        throw new InvalidOperationException("不能只提供群組其中一個member就套用");
    }

    public static void RestoreScopeValidation()
    {
        var artifact = new ArtifactAddress(World, "level.dat", ArtifactKind.Nbt);
        var image = ArtifactImage.Present(Hash256.Compute([1]));
        var manifestHash = Hash256.Compute([9]);
        Reject(() => new ArtifactRestore(manifestHash, [new(artifact, image)], new Dictionary<ArtifactAddress, ArtifactImage>()));
        Reject(() => new ArtifactRestore(manifestHash, [new(artifact, image), new(artifact, image)],
            new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = image }));
        var entry = new ArtifactAddress(World, "a.zip", ArtifactKind.Archive, "entry");
        Reject(() => new ArtifactRestore(manifestHash, [new(entry, image)], new Dictionary<ArtifactAddress, ArtifactImage> { [entry] = image }));
        var sameFile = new ArtifactAddress(World, artifact.RelativePath, ArtifactKind.Json);
        Reject(() => new ArtifactRestore(manifestHash, [new(artifact, image), new(sameFile, image)],
            new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = image, [sameFile] = image }));
    }
}

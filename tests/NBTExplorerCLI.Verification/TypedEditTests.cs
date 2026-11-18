using NBTExplorerCLI.Domain;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class TypedEditTests
{
    private static TypedPath Path(params PathSegment[] segments) => new(segments);
    private static TypedPath Key(string name) => Path(PathSegment.ForKey(name));
    private static NbtDocument Document => new("root", new NbtCompound([
        new("text", new NbtString("Hello %s")), new("UUID", new NbtIntArray([1, 2, 3, 4])),
        new("clickEvent", new NbtString("/say protected")), new("number", new NbtInt(42)),
        new("list", new NbtList(NbtType.String, [new NbtString("one"), new NbtString("two")]))]));
    private static SourceAddress Binding => Address(TypedPath.Root);

    public static void ExactTypedReplacement()
    {
        var source = Document;
        var edits = new[] { TypedEdit.Replace(Key("number"), new NbtInt(42), new NbtLong(long.MaxValue)),
            TypedEdit.Replace(Path(PathSegment.ForKey("UUID"), PathSegment.ForIndex(2)), new NbtInt(3), new NbtInt(-1)) };
        var candidate = TypedTreeEdits.Apply(source, edits);
        Require(NbtLocator.Resolve(candidate.Root, Key("number")).Value is NbtLong { Value: long.MaxValue }, "通用TypedEdit可明示修改非文字與型別");
        Require(NbtLocator.Resolve(candidate.Root, Key("UUID")).Value == new NbtIntArray([1, 2, -1, 4]), "array元素修改必須保留array kind");
        Require(NbtLocator.Resolve(source.Root, Key("number")).Value == new NbtInt(42), "純建構不得改原樹");
        Require(TypedTreeEdits.Verify(source, candidate, edits), "完整正確候選應通過");
        var malicious = TypedTreeEdits.Apply(candidate, [TypedEdit.Replace(Key("clickEvent"), new NbtString("/say protected"), new NbtString("/say evil"))]);
        Require(!TypedTreeEdits.Verify(source, malicious, edits), "scope外event改動必須拒絕");
        Require(!TypedTreeEdits.Verify(source, new("renamed", candidate.Root), edits), "未授權root name不得變更");
    }

    public static void StructuralOperations()
    {
        var source = Document;
        var root = (NbtCompound)source.Root;
        var added = TypedTreeEdits.Apply(source, [TypedEdit.Add(TypedPath.Root, root, PathSegment.ForKey(""), new NbtByte(-1))]);
        Require(NbtLocator.Resolve(added.Root, Key("")).Value == new NbtByte(-1), "新增合法空key需顯式授權");
        var renamed = TypedTreeEdits.Apply(added, [TypedEdit.Rename(Key(""), new NbtByte(-1), "new")]);
        Require(NbtLocator.Resolve(renamed.Root, Key("")).Status == ResolutionStatus.Missing
            && NbtLocator.Resolve(renamed.Root, Key("new")).Value == new NbtByte(-1), "rename保留原typed value");
        var removed = TypedTreeEdits.Apply(renamed, [TypedEdit.Remove(Key("new"), new NbtByte(-1))]);
        Require(removed == source, "add/rename/remove應還原完整原樹");
        var list = (NbtList)NbtLocator.Resolve(source.Root, Key("list")).Value!;
        var inserted = TypedTreeEdits.Apply(source, [TypedEdit.Add(Key("list"), list, PathSegment.ForIndex(1), new NbtString("middle"))]);
        Require(NbtLocator.Resolve(inserted.Root, Key("list")).Value == new NbtList(NbtType.String,
            [new NbtString("one"), new NbtString("middle"), new NbtString("two")]), "明示插入維持list順序與型別");
        var deleted = TypedTreeEdits.Apply(inserted,
            [TypedEdit.Remove(Path(PathSegment.ForKey("list"), PathSegment.ForIndex(1)), new NbtString("middle"))]);
        Require(deleted == source, "list刪除僅移除指定元素");
    }

    public static void RejectsConflictsAndInvalidTrees()
    {
        var source = Document;
        RejectEdit(EditFailure.Precondition, () => TypedTreeEdits.Apply(source, [TypedEdit.Replace(Key("number"), new NbtInt(41), new NbtInt(1))]));
        RejectEdit(EditFailure.Missing, () => TypedTreeEdits.Apply(source, [TypedEdit.Remove(Key("absent"), new NbtInt(1))]));
        RejectEdit(EditFailure.Overlap, () => TypedTreeEdits.Apply(source,
            [TypedEdit.Replace(Key("number"), new NbtInt(42), new NbtInt(1)), TypedEdit.Remove(Key("number"), new NbtInt(42))]));
        RejectEdit(EditFailure.Overlap, () => TypedTreeEdits.Apply(source,
            [TypedEdit.Replace(TypedPath.Root, source.Root, source.Root), TypedEdit.Remove(Key("number"), new NbtInt(42))]));
        RejectEdit(EditFailure.InvalidStructure, () => TypedTreeEdits.Apply(source,
            [TypedEdit.Replace(Path(PathSegment.ForKey("list"), PathSegment.ForIndex(0)), new NbtString("one"), new NbtInt(1))]));
        RejectEdit(EditFailure.InvalidStructure, () => TypedTreeEdits.Apply(source,
            [TypedEdit.Replace(Path(PathSegment.ForKey("UUID"), PathSegment.ForIndex(0)), new NbtInt(1), new NbtLong(1))]));
        RejectEdit(EditFailure.InvalidStructure, () => TypedTreeEdits.Apply(source, [TypedEdit.Rename(Key("number"), new NbtInt(42), "text")]));
        var duplicate = new NbtDocument("root", new NbtCompound([new("x", new NbtInt(1)), new("x", new NbtInt(2))]));
        RejectEdit(EditFailure.Ambiguous, () => TypedTreeEdits.Apply(duplicate, [TypedEdit.Replace(Key("x"), new NbtInt(1), new NbtInt(9))]));
    }

    public static void LiteralScope()
    {
        var source = Document;
        var slot = Slot();
        var translations = new[] { new LiteralTranslation(slot, Reply(slot, "你好 %s")) };
        var candidate = LiteralTranslations.Apply(Binding, source, translations);
        Require(NbtLocator.Resolve(candidate.Root, Key("text")).Value == new NbtString("你好 %s"), "LiteralTranslation僅改指定槽");
        Require(LiteralTranslations.Verify(Binding, source, candidate, translations), "精確翻譯候選應通過");
        Require(!LiteralTranslations.Verify(Binding, source, TypedTreeEdits.Apply(candidate,
            [TypedEdit.Replace(Key("clickEvent"), new NbtString("/say protected"), new NbtString("evil"))]), translations), "翻譯授權不能遮罩整個component");
        RejectEdit(EditFailure.Precondition, () => LiteralTranslations.Apply(Binding, source,
            [new(slot, Reply(slot, "你好 %s") with { SourceTextHash = TextRevision.Compute("new") })]));
        RejectEdit(EditFailure.InvalidPath, () => LiteralTranslations.Apply(Address(TypedPath.Root, "other.dat"), source, translations));
        var numberSlot = new TextSlot(Address(Key("number")), "42", "number", "en_us", "zh_tw", false, PlaceholderPolicy.None);
        RejectEdit(EditFailure.Precondition, () => LiteralTranslations.Apply(Binding, source, [new(numberSlot, Reply(numberSlot, "9"))]));
    }

    public static void BatchSnapshotIndicesAndRenameCollisions()
    {
        var source = Document;
        var edits = new[] { TypedEdit.Remove(Path(PathSegment.ForKey("list"), PathSegment.ForIndex(0)), new NbtString("one")),
            TypedEdit.Remove(Path(PathSegment.ForKey("list"), PathSegment.ForIndex(1)), new NbtString("two")) };
        var forward = TypedTreeEdits.Apply(source, edits);
        var reverse = TypedTreeEdits.Apply(source, edits.Reverse());
        Require(forward == reverse && NbtLocator.Resolve(forward.Root, Key("list")).Value == new NbtList(NbtType.String, []),
            "批次索引綁定同一snapshot，不能因刪除位移或列舉順序而錯目標");
        var array = new NbtDocument("array", new NbtByteArray([-1, 0, 1]));
        var added = TypedTreeEdits.Apply(array,
            [TypedEdit.Add(TypedPath.Root, array.Root, PathSegment.ForIndex(1), new NbtByte(9))]);
        Require(added.Root == new NbtByteArray([-1, 9, 0, 1]), "byte array新增保留型別與順序");
        Require(TypedTreeEdits.Apply(added, [TypedEdit.Remove(Path(PathSegment.ForIndex(1)), new NbtByte(9))]) == array,
            "byte array刪除保留型別");
        RejectEdit(EditFailure.InvalidStructure, () => TypedTreeEdits.Apply(source,
            [TypedEdit.Rename(Key("number"), new NbtInt(42), "new"), TypedEdit.Rename(Key("text"), new NbtString("Hello %s"), "new")]));
    }

    private static void RejectEdit(EditFailure expected, Action action)
    {
        try { action(); }
        catch (EditRejectedException error) when (error.Failure == expected) { return; }
        throw new InvalidOperationException("Expected rejection " + expected);
    }
}

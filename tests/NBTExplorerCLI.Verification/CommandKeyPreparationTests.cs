using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.NbtItemTextTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class CommandKeyPreparationTests
{
    internal static readonly VerificationCase[] Cases =
    [
        new("prepare-world-command-key-components", "preparation", _ => CommandComponentsKeepFramingAndReferences()),
        new("prepare-world-command-key-encoded-batch", "preparation", _ => EncodedCommandKeysAndLiteralsShareOneLeaf()),
        new("prepare-world-command-effective-tag-text", "preparation", _ => DuplicateCommandTagUsesEffectiveText())
    ];

    public static void OlderItemTagsUseVanillaItemIdentity()
    {
        foreach (var (item, field, data) in new[]
        {
            ("spawner", "BlockEntityTag", """{SpawnData:{entity:{id:"minecraft:pig",CustomName:'{"text":"Example"}'}}}"""),
            ("painting", "EntityTag", """{CustomName:'{"text":"Example"}'}""")
        })
        {
            CommandExtractionResult Read(string identity, string payload)
            {
                var address = new SourceAddress(new(World, "test.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
                return CommandTextExtractor.Extract(new(address, $"give @s {identity}{{{field}:{payload}}}"), MinecraftProfiles.ForDataVersion(3465)!);
            }
            var result = Read(item, data);
            Require(result.Complete && result.Text.Occurrences.Single().Text == "Example", "vanilla item IDs identify their corresponding nested tag when its id is absent");
            Require(!Read("custom:" + item, data).Complete && !Read(item, data.Insert(1, "id:1b,id:2b,")).Complete,
                "custom items and ambiguous nested identities cannot inherit a vanilla schema");
        }
    }

    public static void DuplicateCommandTagUsesEffectiveText()
    {
        var profile = MinecraftProfiles.ForDataVersion(3465)!;
        var address = new SourceAddress(new(World, "items.mcfunction", ArtifactKind.McFunction), TypedPath.Root, "command.function");
        const string painting = """painting{EntityTag:{CustomName:'{"selector":"@e[name=Shadowed]"}',CustomName:'{"text":"Visible"}'}}""";
        foreach (var itemCommand in new[] { "give @s " + painting, "item replace block ~ ~ ~ container.0 with " + painting,
            """data modify block ~ ~ ~ Items append value {id:"minecraft:paper",Count:1b,tag:{display:{Name:'{"selector":"@e[name=Shadowed]"}',Name:'{"text":"Visible"}'}}}""" })
        {
            var result = CommandTextExtractor.Extract(new(address, itemCommand), profile);
            Require(result.Complete && result.References.IsEmpty && result.Text.Occurrences.Single().Text == "Visible",
                "command item tags and stacks read only effective text and references: " + itemCommand);
        }

        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string command = """setblock ~ ~ ~ chest{CustomName:'{"text":"Shadowed"}',Lock:"keep",CustomName:'{"text":"Visible"}'}""";
            var path = WorldQueryTests.Function(0);
            Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":15,\"description\":\"Test\"}}"u8.ToArray());
            Put(root, path, Encoding.UTF8.GetBytes(command));
            var snapshot = WorldQueryTests.Query(root, cache);
            var row = snapshot.ReadSources().Single(item => item.Source.Location.Artifact.RelativePath == path);
            Require(row is { Outcome: SourceOutcome.Success, Text.Complete: true } && row.Text.Occurrences.Select(item => item.Text).SequenceEqual(new[] { "Visible" }),
                "command compounds expose only the final effective value for each duplicated field");
            var slot = row.Text!.Occurrences.Single().ToSlot("en_us", "zh_tw");
            var operation = new LiteralPatch("visible", new(slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "顯示")));
            var inventory = new WorldInventory(root, World);
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, [operation]);
            using var candidate = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true);
            var output = File.ReadAllText(candidate.Manifest.Artifacts.Single().CandidatePath!);
            var span = slot.Address.Syntax!;
            var actual = CommandTextExtractor.Extract(new(new(slot.Address.Artifact, TypedPath.Root, "command.function"), output), profile);
            Require(output.StartsWith(command[..(int)span.StartByte], StringComparison.Ordinal)
                && output.EndsWith(command[(int)(span.StartByte + span.LengthBytes)..], StringComparison.Ordinal)
                && actual.Text.Occurrences.Single().Text == "顯示" && File.ReadAllText(Path.Combine(root, path)) == command,
                "command preparation rewrites only the effective token while preserving shadowed bytes and the live source");

            const string summon = """summon pig ~ ~ ~ {CustomName:'{"text":"Shadowed"}',CustomName:'{"text":"Visible"}'}""";
            var templates = new List<CommandNameTemplate>();
            var summonResult = CommandTextExtractor.Extract(new(new(slot.Address.Artifact, TypedPath.Root, "command.function"), summon),
                profile, nameTemplate: templates.Add);
            Require(summonResult.Complete && templates.Single().Name == "Visible", "reference synchronization receives only the active summon name");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    public static void EncodedCommandKeysAndLiteralsShareOneLeaf()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string encoded = "'[{\"text\":\"Hello\"},\"Tail\",{\"text\":\"Other\"}]'";
            var command = "give @p written_book{pages:[" + encoded + "]}\n"; var path = WorldQueryTests.Function(0);
            Put(root, "datapacks/p/pack.mcmeta", "{\"pack\":{\"pack_format\":26,\"description\":\"Old\"}}"u8.ToArray()); Put(root, path, Encoding.UTF8.GetBytes(command));
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":22,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache); var profile = MinecraftProfiles.ForDataVersion(3700)!;
            var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.Artifact.RelativePath == path).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
            Require(slots.Length == 3 && slots.Select(item => item.Address.Syntax).Distinct().Count() == 1, "real give/book extraction binds three component slots to one encoded SNBT leaf");
            var operations = new List<PatchOperation>();
            foreach (var (text, key) in new[] { ("Hello", "test.one"), ("Other", "test.two") })
            {
                var slot = slots.Single(item => item.SourceText == text); var componentPath = new TypedPath(slot.Address.ComponentPath!.Segments.Take(slot.Address.ComponentPath.Segments.Length - 1));
                var address = new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: componentPath, syntax: slot.Address.Syntax);
                operations.Add(new KeyConversionPatch(key, new(address, new NbtString(encoded), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot,
                    new(slot.Id, slot.SourceTextHash, "zh_tw", "譯:" + text), new(new(World, language, ArtifactKind.Json), null, "zh_tw", key, "譯:" + text))));
            }
            var tail = slots.Single(item => item.SourceText == "Tail"); operations.Add(new LiteralPatch("tail", new(tail, new(tail.Id, tail.SourceTextHash, "zh_tw", "尾"))));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var output = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!); var body = Encoding.UTF8.GetString(output);
                var actual = CommandTextExtractor.Extract(new(new(new(World, path, ArtifactKind.McFunction), TypedPath.Root, "command.function"), body), profile);
                Require(actual.Text.TranslationKeys.Select(item => item.Key).Order(StringComparer.Ordinal).SequenceEqual(new[] { "test.one", "test.two" })
                    && actual.Text.Occurrences.Single().Text == "尾", "two independent keys and literal sibling mutate one decoded leaf without competing raw edits");
            }
            Require(File.ReadAllText(Path.Combine(root, path)) == command, "encoded command preparation preserves original file");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void NbtCommandKeysAndLiteralSiblingsShareOneSnapshot()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string command = "tellraw @a[name=Guard] [\"Hello\",\"Tail\"]";
            var block = C(E("id", S("command_block")), E("Command", S(command)));
            var item = C(E("id", S("command_block")), E("components", C(E("block_entity_data", block))));
            var document = new NbtDocument("", C(E("DataVersion", new NbtInt(3953)), E("Inventory", L(item)), E("Counter", new NbtInt(7))));
            const string path = "playerdata/commands.dat"; var original = CompressedNbt.WriteDocument(document, NbtCompression.GZip); Put(root, path, original);
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var slots = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Where(item => item.Address.Artifact.RelativePath == path).Select(item => item.ToSlot("en_us", "zh_tw")).ToArray();
            var first = slots.Single(item => item.SourceText == "Hello"); var second = slots.Single(item => item.SourceText == "Tail"); var profile = MinecraftProfiles.ForDataVersion(3953)!;
            var key = new KeyConversionPatch("key", new(first.Address, new NbtString("\"Hello\""), KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version,
                first, new(first.Id, first.SourceTextHash, "zh_tw", "你好"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.block", "你好")));
            var literal = new LiteralPatch("sibling", new(second, new(second.Id, second.SourceTextHash, "zh_tw", "尾")));
            var inventory = new WorldInventory(root, World); var plan = new WorldPlanBuilder(inventory).Build(snapshot, [key, literal]);
            using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
            {
                var actual = CompressedNbt.ReadDocument(File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!), NbtCompression.GZip).Document;
                Require(NbtLocator.Resolve(actual.Root, first.Address.NodePath).Value == new NbtString("tellraw @a[name=Guard] [{\"translate\":\"test.block\"},\"尾\"]")
                    && NbtLocator.Resolve(actual.Root, new([PathSegment.ForKey("Counter")])).Value == new NbtInt(7), "NBT command keys and literal siblings splice one source snapshot while preserving selector and unrelated typed fields");
            }
            Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(original), "mixed command candidate does not commit the NBT artifact");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void PlainFieldsAndStaleRawComponentsCannotGrantAuthority()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string component = "{\"text\":\"Hello\",\"color\":\"red\"}"; const string prefix = "tellraw @s "; var path = WorldQueryTests.Function(0);
            Put(root, path, Encoding.UTF8.GetBytes(prefix + component + "\n"));
            Put(root, "resourcepacks/client/pack.mcmeta", "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}"u8.ToArray());
            const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var syntax = slot.Address.Syntax!; var profile = MinecraftProfiles.ForDataVersion(3953)!;
            foreach (var field in new[] { true, false })
            {
                var rootSyntax = field ? syntax : new SyntaxLocator(syntax.GrammarProfile, syntax.GrammarVersion, Encoding.UTF8.GetByteCount(prefix), Encoding.UTF8.GetByteCount(component),
                    syntax.PreimageHash, syntax.Encoding, syntax.Newline, new(syntax.NodePath!.Segments.Take(syntax.NodePath.Segments.Length - 1)));
                var operation = new KeyConversionPatch("invalid", new(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, syntax: rootSyntax), new NbtString(field ? "\"Hello\"" : "{\"text\":\"Stale\"}"),
                    KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.invalid", "你好")));
                Reject<EditRejectedException>(() => new WorldPlanBuilder(new WorldInventory(root, World)).Build(snapshot, [operation]));
            }
            Require(File.ReadAllText(Path.Combine(root, path)) == prefix + component + "\n" && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "rejected command recipes neither guess a nearby root nor write candidates");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    public static void CommandComponentsKeepFramingAndReferences()
    {
        foreach (var modern in new[] { false, true })
        foreach (var archived in new[] { false, true })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                var format = modern ? 71 : 48;
                var metadata = "{\"pack\":{\"pack_format\":" + format + ",\"description\":\"Data\"}}";
                const string prefix = "tellraw @a[name=Guard] "; const string suffix = " \r\n# untouched\r\n";
                var component = modern ? "{text:'Hello',extra:['Tail'],color:'red'}" : "{\"text\":\"Hello\",\"extra\":[\"Tail\"],\"color\":\"red\"}";
                byte[] original = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(prefix + component + suffix)];
                var path = archived ? "datapacks/commands.zip" : WorldQueryTests.Function(0);
                if (archived) Put(root, path, Zip(("pack.mcmeta", metadata), ("data/test/function/a.mcfunction", Encoding.UTF8.GetString(original)), ("opaque.bin", "Keep")));
                else { Put(root, "datapacks/p/pack.mcmeta", Encoding.UTF8.GetBytes(metadata)); Put(root, path, original); }
                Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes("{\"pack\":{\"pack_format\":" + (modern ? 55 : 34) + ",\"description\":\"Resources\"}}"));
                const string language = "resourcepacks/client/assets/test/lang/zh_tw.json"; Put(root, language, "{}"u8.ToArray());
                var snapshot = WorldQueryTests.Query(root, cache);
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == path && item.Text == "Hello").ToSlot("en_us", "zh_tw");
                var syntax = slot.Address.Syntax!; var logicalRoot = new TypedPath(syntax.NodePath!.Segments.Take(syntax.NodePath.Segments.Length - 1));
                var rootSyntax = new SyntaxLocator(syntax.GrammarProfile, syntax.GrammarVersion, 3 + Encoding.UTF8.GetByteCount(prefix), Encoding.UTF8.GetByteCount(component), syntax.PreimageHash, syntax.Encoding, syntax.Newline, logicalRoot);
                var profile = MinecraftProfiles.ForDataVersion(modern ? 4325 : 3953)!;
                var conversion = new TranslationKeyConversion(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, syntax: rootSyntax), new NbtString(component), KeyConversionRule.EncodedTextToTranslate,
                    profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "你好%"), new(new(World, language, ArtifactKind.Json), null, "zh_tw", "test.command", "你好%"));
                var inventory = new WorldInventory(root, World); var originalArtifact = File.ReadAllBytes(Path.Combine(root, path));
                var plan = new WorldPlanBuilder(inventory).Build(snapshot, [new KeyConversionPatch("command-key", conversion)]);
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var bytes = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == path).CandidatePath!);
                    if (archived)
                    {
                        using var zip = ZipArchiveValidation.OpenRead(bytes); using var stream = zip.GetEntry(slot.Address.Artifact.ContainerEntry!)!.Open(); using var content = new MemoryStream(); stream.CopyTo(content); bytes = content.ToArray();
                    }
                    Require(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "command key conversion preserves BOM");
                    var body = Encoding.UTF8.GetString(bytes.AsSpan(3));
                    Require(body.StartsWith(prefix, StringComparison.Ordinal) && body.EndsWith(suffix, StringComparison.Ordinal), "command key conversion preserves selector/prefix/trailing whitespace/newlines/comments");
                    var grammar = profile.Features[ProfileFacet.CommandGrammar];
                    var actual = CommandTextExtractor.Extract(new(new(slot.Address.Artifact, TypedPath.Root, "command.function", slot.Address.Dimension,
                        syntax: new(grammar.Schema, grammar.Version, 3, bytes.Length - 3, Hash256.Compute(bytes).Hex, "utf8-bom", SyntaxLocator.DetectNewline(body))), body), profile);
                    Require(actual.Text.TranslationKeys.Single().Key == "test.command" && actual.Text.Occurrences.Select(item => item.Text).Order(StringComparer.Ordinal).SequenceEqual(new[] { "%", "Tail" })
                        && actual.References.Single().Name == "Guard", "only requested literal component becomes a key while sibling text and static selector remain");
                    using var values = BoundedJson.ReadDecoded(File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact.RelativePath == language).CandidatePath!));
                    Require(NativeJsonStrings.Read(values.RootElement.GetProperty("test.command")) == "你好%1$s", "command key dependency preserves literal percent semantics");
                }
                Require(File.ReadAllBytes(Path.Combine(root, path)).SequenceEqual(originalArtifact), "standalone and archived command preparation preserve original physical bytes");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
}

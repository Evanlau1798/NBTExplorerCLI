using System.Text;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Infrastructure;
using NBTExplorerCLI.Minecraft;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class NewLanguagePreparationTests
{
    private sealed class FailSecondStage(IArtifactStorage inner) : IArtifactStorage
    {
        public Hash256? AccessBinding => inner.AccessBinding;
        private int stages;
        public byte[] Read(ArtifactAddress artifact) => inner.Read(artifact);
        public IArtifactCandidate Stage(ArtifactAddress artifact, ArtifactImage preimage, ReadOnlyMemory<byte> bytes)
            => ++stages == 2 ? throw new IOException("Injected new output stage failure") : inner.Stage(artifact, preimage, bytes);
    }
    public static void AbsentLanguageIsDeclaredBeforeStage()
    {
        PrepareAbsent(archived: false);
        PrepareAbsent(archived: false, version: 4189);
    }
    public static void GeneratedPackIsDeclaredBeforeStage()
    {
        PrepareAbsent(archived: true);
        PrepareAbsent(archived: true, version: 4189);
    }
    public static void GeneratedPackProfilesAndSizeLimits()
    {
        PrepareAbsent(archived: true, version: 4325);
        PrepareAbsent(archived: true, version: 4774);
        PrepareAbsent(archived: true, compressedLimit: 4096, translation: new string('X', 1000));
        PrepareAbsent(archived: true, compressedLimit: 512, translation: new string('X', 1000), oversized: true);
    }
    public static void InvalidLocalesCannotBecomeLanguageDependencies()
    {
        foreach (var archived in new[] { false, true })
        foreach (var existing in new[] { false, true })
        foreach (var locale in new[] { "zh tw", "ZH_TW" })
        {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                const string component = "{\"text\":\"Hello\"}"; WorldQueryTests.BossWorld(root, componentJson: component);
                const string metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"Resources\"}}";
                var entry = "assets/test/lang/" + locale + ".json";
                var language = archived ? new ArtifactAddress(World, "resources.zip", ArtifactKind.Archive, entry)
                    : new ArtifactAddress(World, "resourcepacks/client/" + entry, ArtifactKind.Json);
                var outer = archived ? new ArtifactAddress(World, "resources.zip", ArtifactKind.Archive) : null;
                if (archived && existing) Put(root, "resources.zip", Zip(("pack.mcmeta", metadata), (entry, "{}")));
                if (!archived)
                {
                    Put(root, "resourcepacks/client/pack.mcmeta", System.Text.Encoding.UTF8.GetBytes(metadata));
                    Put(root, "resourcepacks/client/assets/test/lang/en_us.json", "{}"u8.ToArray());
                    if (existing) Put(root, language.RelativePath, "{}"u8.ToArray());
                }
                var snapshot = WorldQueryTests.Query(root, cache);
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", locale);
                var profile = MinecraftProfiles.ForDataVersion(3953)!;
                var operation = new KeyConversionPatch("invalid-locale", new(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root), new NbtString(component),
                    KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, locale, "Translated"), new(language, outer, locale, "test.locale", "Translated")));
                var original = File.ReadAllBytes(Path.Combine(root, "level.dat"));
                Reject<EditRejectedException>(() => new WorldPlanBuilder(new WorldInventory(root, World)).Build(snapshot, [operation]));
                Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(original) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "invalid locale binding cannot publish candidates or change world bytes");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
        }
    }
    public static void NewOutputDeclarationsRequireExactResourceLanguagePaths()
    {
        var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
        try
        {
            const string component = "{\"text\":\"Hello\"}"; WorldQueryTests.BossWorld(root, componentJson: component);
            var snapshot = WorldQueryTests.Query(root, cache);
            var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
            var profile = MinecraftProfiles.ForDataVersion(3953)!;
            foreach (var language in new[] { new ArtifactAddress(World, "zh_tw.json", ArtifactKind.Json),
                new(World, "datapacks/new.zip", ArtifactKind.Archive, "assets/test/lang/zh_tw.json"),
                new(World, "new.zip", ArtifactKind.Archive, "assets/test/lang/en_us.json"),
                new(World, "new.zip", ArtifactKind.Archive, "assets/Bad/lang/zh_tw.json") })
            {
                var pack = language.ContainerEntry is null ? null : new ArtifactAddress(World, language.RelativePath, ArtifactKind.Archive);
                var operation = new KeyConversionPatch("invalid-output", new(new(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root), new NbtString(component),
                    KeyConversionRule.EncodedTextToTranslate, profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", "Text"), new(language, pack, "zh_tw", "test.key", "Text")));
                Reject<EditRejectedException>(() => new WorldPlanBuilder(new WorldInventory(root, World)).Build(snapshot, [operation]));
            }
            Require(!File.Exists(Path.Combine(root, "new.zip")) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "invalid declarations never create files");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
    private static void PrepareAbsent(bool archived, int version = 3953, long? compressedLimit = null, string translation = "你好", bool oversized = false)
    {
            var root = WorldQueryTests.CreateWorld(); var cache = ScratchDirectory.CreateChild();
            try
            {
                const string original = "{\"text\":\"Hello\",\"color\":\"red\"}";
                var encoded = version is 3953 or 4189;
                if (version == 3953) WorldQueryTests.BossWorld(root, componentJson: original);
                else
                {
                    var level = WorldMetadataTests.Level(version, version == 4774 ? null : WorldMetadataTests.Dimensions);
                    var data = (NbtCompound)((NbtCompound)level.Root).Entries.Single().Value;
                    var bosses = new NbtCompound([new("test:event", new NbtCompound([new("Name", new NbtString(encoded ? original : "Hello"))]))]);
                    Put(root, "level.dat", CompressedNbt.WriteDocument(new("", new NbtCompound([new("Data", new NbtCompound(data.Entries.Append(new("CustomBossEvents", bosses))))])), NbtCompression.GZip));
                    if (version == 4774) Put(root, "data/minecraft/world_gen_settings.dat", CompressedNbt.WriteDocument(WorldMetadataTests.Separate(version, WorldMetadataTests.Dimensions), NbtCompression.GZip));
                }
                const string entry = "assets/test/lang/zh_tw.json";
                var language = archived ? new ArtifactAddress(World, "z-generated.zip", ArtifactKind.Archive, entry)
                    : new ArtifactAddress(World, "resourcepacks/client/" + entry, ArtifactKind.Json);
                var physical = archived ? new ArtifactAddress(World, "z-generated.zip", ArtifactKind.Archive) : language;
                if (!archived)
                {
                    var packFormat = version == 4189 ? 46 : 34;
                    Put(root, "resourcepacks/client/pack.mcmeta", Encoding.UTF8.GetBytes($$$"""{"pack":{"pack_format":{{{packFormat}}},"description":"Resources"}}"""));
                    Put(root, "resourcepacks/client/assets/test/lang/en_us.json", "{\"old\":\"Keep\"}"u8.ToArray());
                }
                var limits = new DecodeLimits(MaxCompressedBytes: compressedLimit ?? DecodeLimits.Default.MaxCompressedBytes);
                var snapshot = new WorldQueryService(new WorldInventory(root, World), new FileSystemScanCacheStore(cache)).QueryAsync(new(new(SnapshotScope.All, limits))).GetAwaiter().GetResult().Snapshot!;
                var slot = snapshot.ReadSources().SelectMany(row => row.Text?.Occurrences ?? []).Single(item => item.Address.Artifact.RelativePath == "level.dat" && item.Text == "Hello").ToSlot("en_us", "zh_tw");
                var profile = MinecraftProfiles.ForDataVersion(version)!;
                var address = encoded ? new SourceAddress(slot.Address.Artifact, slot.Address.NodePath, slot.Role, componentPath: TypedPath.Root) : slot.Address;
                var conversion = new TranslationKeyConversion(address, new NbtString(encoded ? original : "Hello"), encoded ? KeyConversionRule.EncodedTextToTranslate : KeyConversionRule.LiteralToTranslateComponent,
                    profile.Id, profile.Version, slot, new(slot.Id, slot.SourceTextHash, "zh_tw", translation), new(language, archived ? physical : null, "zh_tw", "test.created", translation));
                var operation = new KeyConversionPatch("new-language", conversion);
                var inventory = new WorldInventory(root, World); var builder = new WorldPlanBuilder(inventory);
                var levelBefore = File.ReadAllBytes(Path.Combine(root, "level.dat"));
                var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray();
                var plan = builder.Build(snapshot, [operation]);
                Require(plan.Preconditions.Single(item => item.Artifact == physical).Image.ExpectedAbsent && plan.Outputs.Length == 2
                    && plan.Dependencies.Single().Required == physical, "preview declares absent language/pack, dependent component and full output set");
                Require(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().SequenceEqual(files), "preview never creates missing output files");
                var storage = new ArtifactStorage(root, World);
                if (oversized)
                {
                    Reject<CodecException>(() => { using var rejected = new WorldPlanPreparation(inventory, storage).Prepare(snapshot, plan, true); });
                    Require(!File.Exists(Path.Combine(root, physical.RelativePath)) && !Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any()
                        && File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(levelBefore), "oversized generated pack is rejected and prior staged output cleaned without world changes");
                    return;
                }
                Reject<IOException>(() => { using var rejected = new WorldPlanPreparation(inventory, new FailSecondStage(storage)).Prepare(snapshot, plan, true); });
                Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any() && !File.Exists(Path.Combine(root, physical.RelativePath)), "failure staging a new dependency cleans the earlier NBT candidate");
                using (var prepared = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true))
                {
                    var output = File.ReadAllBytes(prepared.Manifest.Artifacts.Single(item => item.Artifact == physical).CandidatePath!);
                    byte[] jsonBytes;
                    if (archived)
                    {
                        using var zip = ZipArchiveValidation.OpenRead(output);
                        Require(zip.Entries.Select(item => item.FullName).Order().SequenceEqual(new[] { entry, "pack.mcmeta" }.Order()), "generated pack contains exactly declared language and metadata entries");
                        using var metadata = new MemoryStream(); using (var stream = zip.GetEntry("pack.mcmeta")!.Open()) stream.CopyTo(metadata);
                        Require(PackMetadata.Read(metadata.ToArray(), PackSourceKind.ResourcePack).DeclaresSupport(MinecraftProfiles.RequiredResourcePackFormat(version)), "generated pack metadata supports the actual target world version");
                        using var bytes = new MemoryStream(); using (var stream = zip.GetEntry(entry)!.Open()) stream.CopyTo(bytes); jsonBytes = bytes.ToArray();
                    }
                    else jsonBytes = output;
                    using var json = BoundedJson.ReadDecoded(jsonBytes);
                    Require(NativeJsonStrings.Read(json.RootElement.GetProperty("test.created")) == translation, "new language candidate contains declared translation");
                    Require(!File.Exists(Path.Combine(root, physical.RelativePath)), "preparation exposes candidates without publishing new output");
                }
                Require(!Directory.EnumerateFiles(root, "*.candidate", SearchOption.AllDirectories).Any(), "disposal removes new output candidates");
                Put(root, physical.RelativePath, "external"u8.ToArray());
                Reject<ArtifactConflictException>(() => { using var rejected = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World)).Prepare(snapshot, plan, true); });
                Require(File.ReadAllText(Path.Combine(root, physical.RelativePath)) == "external", "expectedAbsent never overwrites a file created after preview");
                Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(levelBefore), "all successful and failed preparation paths preserve the original NBT source");
            }
            finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }
}

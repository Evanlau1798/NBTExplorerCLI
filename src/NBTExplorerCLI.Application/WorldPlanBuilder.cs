using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Minecraft;
using System.Collections.Immutable;

namespace NBTExplorerCLI.Application;

public sealed class WorldPlanBuilder(IWorldInventory inventory)
{
    internal const string Policy = "world-prepare-v1";
    private const string MultipleReferencePolicy = "world-prepare-name-groups-v2";
    internal static void RequireReferencePolicy(PatchPlan plan)
    {
        RequireReferenceComposition(plan.Operations);
        if (plan.Operations.OfType<ReferencePatch>().Skip(1).Any() && plan.PolicyVersion != MultipleReferencePolicy) throw new ReferencePolicyException();
    }
    private static void RequireReferenceComposition(IEnumerable<PatchOperation> operations)
    {
        if (operations.OfType<ReferencePatch>().Any() && operations.OfType<TypedPatch>().Any())
            throw new ReferencePolicyException("Raw typed edits cannot join semantic reference synchronization; use separate plans and a fresh scan, or rollback old combined journals before replanning.");
    }
    internal static readonly string[] Checks = ["parse", "preimage", "profile", "projection"];
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    public PatchPlan Build(WorldQuerySnapshot snapshot, IEnumerable<PatchOperation> operations)
        => Bind(snapshot, operations).Plan;
    public PatchPlan Build(VerifiedBackup backup, IEnumerable<RestorePatch> operations)
        => WorldRestorePlans.Build(inventory, backup, operations);
    internal BoundWorldPlan Bind(WorldQuerySnapshot snapshot, IEnumerable<PatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(operations);
        var classicVersion = MinecraftProfiles.ClassicVersion(snapshot.Manifest.Versions.Profile);
        if (MinecraftProfiles.IsReadOnlyAnvil(snapshot.Discovery.Metadata?.Profile))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Early versioned Anvil supports inspection and extraction only");
        if (snapshot.Discovery.Metadata?.Profile == MinecraftProfiles.ClassicAnvil && classicVersion is null)
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Classic Anvil supports inspection and extraction only");
        using var bindingTrace = NBTExplorerCLI.Formats.WorkDiagnostics.Start("plan.bind", phase: "bind", worldId: inventory.WorldId);
        if (snapshot.Manifest.WorldId != inventory.WorldId) throw new ArgumentException("Snapshot belongs to another world", nameof(snapshot));
        var requested = operations.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(operation => operation is null)) throw new ArgumentException("Invalid or oversized operation set", nameof(operations));
        if (classicVersion is not null && requested.Any(operation => operation is not LiteralPatch literalOperation
            || literalOperation.Translation.Slot.Address.Artifact.Kind is not (ArtifactKind.Nbt or ArtifactKind.Region)
            || WorldReferenceService.IsIdentifyingName(literalOperation.Translation.Slot.Role) && literalOperation.Translation.Slot.SourceText != literalOperation.Translation.Reply.Text))
            throw new CodecException(CodecError.UnsupportedProfile, 0, "Classic translation supports only literal world text and preserves entity reference names");
        if (requested.Any(operation => operation is not (LiteralPatch or TypedPatch or KeyConversionPatch or ReferencePatch))) throw new NotSupportedException("This preparation path requires explicit semantic operations");
        RequireReferenceComposition(requested);
        var referenceInputs = WorldReferenceService.Validate(inventory, snapshot, requested.OfType<ReferencePatch>());
        var storageDisplays = StorageDisplayPolicy.Empty;
        if (StorageDisplayPolicy.TargetsStorage(requested))
        {
            if (!snapshot.StorageDisplays.CanBindWrites || requested.Any(operation => operation is not LiteralPatch)
                || !snapshot.StorageDisplays.ProjectedCoverage(snapshot.ReadSources()))
                throw new EditRejectedException(EditFailure.Precondition, "Storage edits require exclusively interpreted uses and a complete storage source proof");
            WorldReferenceScan.Match(snapshot.Discovery, WorldDiscovery.Discover(inventory, new(SnapshotScope.All, snapshot.Limits)));
            storageDisplays = snapshot.StorageDisplays;
        }
        if (requested.OfType<TypedPatch>().Any(operation => operation.Address.Artifact.Kind is not (ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities))) throw new NotSupportedException("Typed edits require an NBT document");
        var outputs = requested.SelectMany(operation => operation.RequiredOutputs).Distinct().ToArray();
        if (outputs.Any(artifact => artifact.WorldId != inventory.WorldId || artifact.Kind is not (ArtifactKind.Nbt or ArtifactKind.Json or ArtifactKind.McFunction or ArtifactKind.Region or ArtifactKind.Entities or ArtifactKind.Archive))) throw new NotSupportedException("Physical artifact preparation profile is not implemented");
        static ArtifactAddress Outer(ArtifactAddress artifact) => artifact.ContainerEntry is null ? artifact : new(artifact.WorldId, artifact.RelativePath, artifact.Kind);
        var sources = snapshot.Discovery.Sources.Where(source => outputs.Contains(Outer(source.Location.Artifact))).ToDictionary(source => (source.Location.Artifact, source.Chunk));
        WorldLanguageOutputs.BindExisting(inventory, snapshot, requested, sources);
        var addedLanguages = WorldLanguageOutputs.AddMissingLanguages(snapshot, requested, sources);
        var absent = addedLanguages.Select(Outer).Where(artifact => sources[(artifact, null)].ContentHash is null).ToHashSet();
        var required = outputs.Select(artifact => (artifact, (ChunkPosition?)null)).Concat(requested.SelectMany(operation => operation.Targets).Select(address => (address.Artifact, address.Chunk))).Distinct().ToArray();
        if (required.Any(key => !sources.TryGetValue(key, out var source) || source.ContentHash is null && !absent.Contains(key.Item1) && !addedLanguages.Contains(key.Item1) || source.Capability.Read != SupportLevel.Supported || source.Outcome != SourceOutcome.Success)) throw new EditRejectedException(EditFailure.Precondition, "Plan output has no supported exact source binding");
        var inputs = new Dictionary<ArtifactAddress, ArtifactPrecondition>();
        void Input(ArtifactAddress artifact, ArtifactImage image)
        {
            var value = new ArtifactPrecondition(artifact, image);
            if (inputs.TryGetValue(artifact, out var prior) && prior != value) throw new ArtifactConflictException("Source metadata has inconsistent preimages"); inputs[artifact] = value;
        }
        foreach (var source in outputs.Select(artifact => sources[(artifact, null)]))
        {
            Input(source.Location.Artifact, absent.Contains(source.Location.Artifact) ? ArtifactImage.Absent : ArtifactImage.Present(source.ContentHash!));
            if (source.Location.Artifact.Kind != ArtifactKind.Archive && source.Location.PackRoot is { } root)
                Input(new(inventory.WorldId, root + "/pack.mcmeta", ArtifactKind.Json), ArtifactImage.Present(source.PackMetadataHash ?? throw new ArtifactConflictException("Pack metadata binding is missing")));
        }
        foreach (var metadata in snapshot.Discovery.MetadataSources.IsDefault ? [] : snapshot.Discovery.MetadataSources) Input(metadata.Artifact, ArtifactImage.Present(metadata.ContentHash!));
        foreach (var input in referenceInputs) Input(input.Artifact, input.Image);
        if (storageDisplays.CanBindWrites)
            foreach (var source in WorldReferenceScan.Relevant(snapshot.Discovery).Where(source => source.Chunk is null && source.Location.Artifact.ContainerEntry is null))
                Input(source.Location.Artifact, ArtifactImage.Present(source.ContentHash!));
        var policy = requested.OfType<ReferencePatch>().Skip(1).Any() ? MultipleReferencePolicy : Policy;
        var plan = new PatchPlan(inventory.WorldId, snapshot.Manifest.SnapshotId, snapshot.Manifest.Versions.Profile, policy, requested,
            inputs.Values, outputs.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, Checks)), []);
        CheckPreimages(inventory, plan);
        var scope = new SnapshotScope(snapshot.Manifest.Scope.Dimensions, snapshot.Manifest.Scope.Regions, snapshot.Manifest.Scope.Types, outputs.Select(artifact => artifact.RelativePath));
        var rules = snapshot.StorageRules.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StorageTextRule>)pair.Value);
        var extraction = new WorldTextExtraction(inventory, new(scope, snapshot.Limits, ClassicVersion: classicVersion), rules)
        { BoundStorageDisplays = storageDisplays.CanBindWrites ? storageDisplays : null };
        var classicPlainText = MinecraftProfiles.UsesClassicPlainText(extraction.LayoutProfile);
        var literal = requested.OfType<LiteralPatch>().Select(operation => operation.Translation)
            .Concat(requested.OfType<KeyConversionPatch>().Select(operation => new LiteralTranslation(operation.Conversion.Slot, operation.Conversion.Reply)))
            .ToDictionary(translation => translation.Slot.Address); var found = new HashSet<SourceAddress>();
        bool CheckSource(DiscoveredSource source)
        {
            if (!sources.TryGetValue((source.Location.Artifact, source.Chunk), out var expected)) return false;
            using var sourceTrace = NBTExplorerCLI.Formats.WorkDiagnostics.Start("source.bind", source.Location.Artifact.RelativePath, worldId: source.Location.Artifact.WorldId);
            if (source.Location != expected.Location || source.ContentHash != expected.ContentHash || source.PackMetadataHash != expected.PackMetadataHash) throw new ArtifactConflictException("Source interpretation changed after scanning");
            sourceTrace?.SetTag("comparisons", 1L);
            return true;
        }
        if (requested.All(operation => operation is TypedPatch))
        {
            foreach (var source in extraction.Discovery.Sources) _ = CheckSource(source);
        }
        else
        {
            foreach (var row in extraction.ReadSources())
            {
                if (!CheckSource(row.Source)) continue;
                using var comparisonTrace = NBTExplorerCLI.Formats.WorkDiagnostics.Start("candidate.lookup", row.Source.Location.Artifact.RelativePath);
                foreach (var occurrence in row.Text?.Occurrences ?? [])
                    if (literal.TryGetValue(occurrence.Address, out var translation))
                    {
                        var slot = translation.Slot;
                        if (!found.Add(occurrence.Address) || slot.SourceText != occurrence.Text || slot.Context != occurrence.Context || slot.AllowedEmpty != occurrence.AllowedEmpty) throw new EditRejectedException(EditFailure.Precondition, "Literal operation differs from its actual source slot");
                        ValidateLiteral(translation, snapshot.Manifest.SnapshotId.Digest);
                        if (classicPlainText) ValidateClassicPlainText(translation, classicVersion!);
                    }
                comparisonTrace?.SetTag("comparisons", (long)(row.Text?.Occurrences.Length ?? 0));
            }
        }
        if (found.Count != literal.Count) throw new EditRejectedException(EditFailure.Precondition, "Literal target is not an extracted text slot");
        foreach (var operation in requested.OfType<TypedPatch>())
            if (operation.Address.Dimension != sources[(operation.Address.Artifact, operation.Address.Chunk)].Location.Dimension) throw new EditRejectedException(EditFailure.Precondition, "Typed target source binding differs");
        foreach (var artifactGroup in requested.OfType<TypedPatch>().GroupBy(operation => operation.Address.Artifact))
        {
            var artifact = artifactGroup.Key;
            using var artifactTrace = NBTExplorerCLI.Formats.WorkDiagnostics.Start("artifact.bind", artifact.RelativePath);
            var bytes = inventory.Read(artifact);
            if (Hash256.Compute(bytes) != sources[(artifact, null)].ContentHash)
                throw new ArtifactConflictException("Typed source payload changed during plan validation");
            var region = artifact.Kind is ArtifactKind.Region or ArtifactKind.Entities ? NBTExplorerCLI.Formats.RegionFile.Read(bytes) : null;
            foreach (var group in artifactGroup.GroupBy(operation => operation.Address.Chunk))
            {
                var document = group.Key is { } chunk
                    ? region!.ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document
                    : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
                _ = NbtTextExtractor.Profile(sources[(artifact, group.Key)].Location, document);
                using var typedTrace = NBTExplorerCLI.Formats.WorkDiagnostics.Start("typed.check", artifact.RelativePath);
                _ = TypedTreeEdits.Apply(document, group.Select(operation => operation.Edit));
                typedTrace?.SetTag("comparisons", (long)group.Count());
            }
        }
        foreach (var group in requested.OfType<KeyConversionPatch>()
            .GroupBy(operation => (operation.Conversion.Address.Artifact, operation.Conversion.Address.Chunk)))
        {
            var conversions = group.Select(operation => operation.Conversion).ToArray();
            if (conversions.Any(conversion => WorldReferenceService.IsIdentifyingName(conversion.Slot.Role)))
                throw new EditRejectedException(EditFailure.Precondition, "Identifying name conversion requires verified reference authority");
            var source = sources[group.Key];
            var bytes = ReadSource(source, snapshot.Limits);
            if (source.Location.Role == WorldSourceRole.Function && source.Pack is { } functionPack)
            {
                var profile = MinecraftProfiles.ForPackMetadata(functionPack) ?? throw new NotSupportedException("Command key conversion profile is unknown");
                var command = WorldCommandEdits.BindFunction(group.Key.Artifact, bytes, profile, source.Location.Dimension);
                var roots = new HashSet<SourceAddress>();
                _ = CommandTextExtractor.Extract(command, profile, snapshot.Limits, snapshot.Manifest.Scope, root => roots.Add(root));
                _ = WorldCommandEdits.Rewrite(command, [], profile, snapshot.Limits, conversions);
                foreach (var conversion in conversions) ValidateBinding(conversion, profile, roots);
                continue;
            }
            foreach (var conversion in conversions) ValidateNonFunctionKey(conversion, source, bytes);
        }
        CheckPreimages(inventory, plan);
        return new(plan, sources.ToImmutableDictionary(), storageDisplays);

        void ValidateNonFunctionKey(TranslationKeyConversion conversion, DiscoveredSource source, byte[] bytes)
        {
            var address = conversion.Address;
            CapabilityProfile profile;
            var roots = new HashSet<SourceAddress>();
            void Root(SourceAddress root) => roots.Add(root);
            if (source.Location.Role is WorldSourceRole.PackJson or WorldSourceRole.PackMetadata && source.Pack is { } pack)
            {
                profile = MinecraftProfiles.ForPackMetadata(pack) ?? throw new NotSupportedException("Key conversion pack profile is unknown");
                _ = PackTextExtractor.Extract(source.Location, bytes, pack, snapshot.Limits, snapshot.Manifest.Scope, Root);
                _ = WorldJsonKeyEdits.Convert(bytes, [conversion], snapshot.Limits);
            }
            else
            {
                var document = address.Chunk is { } chunk ? NBTExplorerCLI.Formats.RegionFile.Read(bytes).ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
                profile = NbtTextExtractor.Profile(source.Location, document);
                var extracted = NbtTextExtractor.Extract(source.Location, document, source.Chunk, snapshot.Limits, snapshot.StorageRules.GetValueOrDefault(address.Artifact, []), snapshot.Manifest.Scope, snapshot.Discovery.Metadata?.Profile, Root);
                if (address.Syntax is not null)
                {
                    var command = extracted.PendingCommands.SingleOrDefault(item => item.Address.NodePath.Equals(address.NodePath)) ?? throw new EditRejectedException(EditFailure.Precondition, "Command key target is not a real NBT command field");
                    _ = CommandTextExtractor.Extract(command, profile, snapshot.Limits, snapshot.Manifest.Scope, Root);
                    _ = WorldCommandEdits.Rewrite(command, [], profile, snapshot.Limits, [conversion]);
                }
                else if (conversion.Rule == KeyConversionRule.EncodedTextToTranslate && address.ComponentPath is not null && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-json")
                {
                    if (NbtLocator.Resolve(document.Root, address.NodePath).Value is not NbtString text) throw new EditRejectedException(EditFailure.Precondition, "Encoded component source is not a string");
                    _ = WorldKeyEdits.RewriteJson(text.Value, [conversion], snapshot.Limits);
                }
                else if (conversion.Rule != KeyConversionRule.EncodedTextToTranslate && profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt")
                    _ = TypedTreeEdits.Apply(document, [TypedEdit.Replace(address.NodePath, conversion.Expected, WorldKeyEdits.Replacement(conversion))]);
                else throw new NotSupportedException("Key conversion encoding profile is not implemented");
            }
            ValidateBinding(conversion, profile, roots);
        }
        void ValidateBinding(TranslationKeyConversion conversion, CapabilityProfile profile, HashSet<SourceAddress> roots)
        {
            if (profile.Id != conversion.Profile || profile.Version != conversion.ProfileVersion) throw new NotSupportedException("Key conversion requires its exact text profile");
            if (!roots.Contains(conversion.Address))
                throw new EditRejectedException(EditFailure.Precondition, "Key conversion target is not a profile-declared text component");
            var language = sources[(conversion.Dependency.LanguageArtifact, null)];
            if (language.Location.Role != WorldSourceRole.Language || language.Pack?.Kind != PackSourceKind.ResourcePack)
                throw new EditRejectedException(EditFailure.Precondition, "Key conversion language dependency is not an exact resource language source");
            if (!language.Pack.DeclaresSupport(MinecraftProfiles.RequiredResourcePackFormat(profile, snapshot.Discovery.Metadata)))
                throw new NotSupportedException("Resource language pack does not support the target component profile");
            var languagePath = language.Location.Artifact.ContainerEntry ?? language.Location.Artifact.RelativePath;
            if (!languagePath.EndsWith("/" + conversion.Dependency.Locale + ".json", StringComparison.Ordinal)) throw new EditRejectedException(EditFailure.Precondition, "Language dependency locale differs from its path");
        }
    }
    internal byte[] ReadSource(DiscoveredSource source, DecodeLimits limits)
    {
        var artifact = source.Location.Artifact;
        var outer = artifact.ContainerEntry is null ? artifact : new ArtifactAddress(artifact.WorldId, artifact.RelativePath, artifact.Kind);
        var bytes = inventory.Read(outer);
        if (artifact.ContainerEntry is { } name)
        {
            using var zip = NBTExplorerCLI.Formats.ZipArchiveValidation.OpenRead(bytes, limits);
            var entry = zip.GetEntry(name) ?? throw new ArtifactConflictException("Bound ZIP entry disappeared");
            bytes = new byte[checked((int)entry.Length)]; using var stream = entry.Open(); stream.ReadExactly(bytes);
        }
        if (Hash256.Compute(bytes) != source.ContentHash) throw new ArtifactConflictException("Bound source payload changed");
        return bytes;
    }
    internal static void CheckPreimages(IWorldInventory inventory, PatchPlan plan)
    {
        WorldReferenceMembers.Catalog(inventory, plan);
        foreach (var input in plan.Preconditions)
        {
            ArtifactImage actual;
            try { actual = ArtifactImage.Present(Hash256.Compute(inventory.Read(input.Artifact))); } catch (FileNotFoundException) { actual = ArtifactImage.Absent; }
            if (actual != input.Image) throw new ArtifactConflictException("A bound source changed");
        }
    }
    private static void ValidateClassicPlainText(LiteralTranslation translation, string version)
    {
        var slot = translation.Slot;
        var value = translation.Reply.Text;
        if (value == slot.SourceText) return;
        var limit = slot.Role switch
        {
            "sign.front.line" => 15,
            "book.title" or "scoreboard.team.prefix" or "scoreboard.team.suffix" => 16,
            "scoreboard.objective.name" or "scoreboard.team.name" => 32,
            "command.block.message" when slot.Address.Syntax?.NodePath?.Segments.LastOrDefault()?.Key == "scoreboard.message" => 32,
            "book.page" => 256,
            _ => int.MaxValue
        };
        if (value.Length > limit || slot.Role == "sign.front.line" && value.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new EditRejectedException(EditFailure.Precondition, $"Java {version} text exceeds the vanilla field limits for {slot.Role}");
    }

    private static void ValidateLiteral(LiteralTranslation translation, Hash256 snapshot)
    {
        var slot = translation.Slot;
        if (translation.Reply.Text != slot.SourceText && WorldReferenceService.IsIdentifyingName(slot.Role))
            throw new EditRejectedException(EditFailure.Precondition, "Identifying names require an explicitly verified complete reference synchronization");
        var id = Guid.NewGuid(); var pageId = Hash256.Compute([]); var format = MinecraftPlaceholderProfiles.ForRole(slot.Role);
        var page = new SessionPage(id, 0, snapshot, 0, pageId, slot.SourceLocale, slot.TargetLocale,
            SessionPage.PolicyFor(slot.Placeholders.ProtectPrivateUseGlyphs), false,
            [new(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens, format, slot.Placeholders.ProtectVisibleText)]);
        var reply = new SessionReply(Guid.NewGuid(), id, 0, snapshot, 0, pageId, slot.TargetLocale, [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, translation.Reply.Text)]);
        var validation = SessionReplyValidator.Validate(page, [slot], reply, new Dictionary<Hash256, PlaceholderFormat> { [slot.Id.Digest] = format });
        if (!validation.Valid) throw new SessionReplyRejectedException(validation.Issues);
    }
}

internal sealed record BoundWorldPlan(PatchPlan Plan, ImmutableDictionary<(ArtifactAddress Artifact, ChunkPosition? Chunk), DiscoveredSource> Sources, StorageDisplayPolicy StorageDisplays);

public sealed class ReferencePolicyException(string message = "Multiple reference groups require the verified group policy; roll back any pending journal before creating a fresh plan.") : NotSupportedException(message);

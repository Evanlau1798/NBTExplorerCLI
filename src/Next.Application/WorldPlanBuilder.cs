using Next.Domain;
using Next.Minecraft;
using System.Collections.Immutable;

namespace Next.Application;

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
        if (snapshot.Manifest.WorldId != inventory.WorldId) throw new ArgumentException("Snapshot belongs to another world", nameof(snapshot));
        var requested = operations.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(operation => operation is null)) throw new ArgumentException("Invalid or oversized operation set", nameof(operations));
        if (requested.Any(operation => operation is not (LiteralPatch or TypedPatch or KeyConversionPatch or ReferencePatch))) throw new NotSupportedException("This preparation path requires explicit semantic operations");
        RequireReferenceComposition(requested);
        var referenceInputs = WorldReferenceService.Validate(inventory, snapshot, requested.OfType<ReferencePatch>());
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
        var policy = requested.OfType<ReferencePatch>().Skip(1).Any() ? MultipleReferencePolicy : Policy;
        var plan = new PatchPlan(inventory.WorldId, snapshot.Manifest.SnapshotId, snapshot.Manifest.Versions.Profile, policy, requested,
            inputs.Values, outputs.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, Checks)), []);
        CheckPreimages(inventory, plan);
        var scope = new SnapshotScope(snapshot.Manifest.Scope.Dimensions, snapshot.Manifest.Scope.Regions, snapshot.Manifest.Scope.Types, outputs.Select(artifact => artifact.RelativePath));
        var rules = snapshot.StorageRules.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StorageTextRule>)pair.Value);
        var extraction = new WorldTextExtraction(inventory, new(scope, snapshot.Limits), rules);
        var literal = requested.OfType<LiteralPatch>().Select(operation => operation.Translation)
            .Concat(requested.OfType<KeyConversionPatch>().Select(operation => new LiteralTranslation(operation.Conversion.Slot, operation.Conversion.Reply)))
            .ToDictionary(translation => translation.Slot.Address); var found = new HashSet<SourceAddress>();
        foreach (var row in extraction.ReadSources())
        {
            if (!sources.TryGetValue((row.Source.Location.Artifact, row.Source.Chunk), out var expected)) continue;
            if (row.Source.Location != expected.Location || row.Source.ContentHash != expected.ContentHash || row.Source.PackMetadataHash != expected.PackMetadataHash) throw new ArtifactConflictException("Source interpretation changed after scanning");
            foreach (var occurrence in row.Text?.Occurrences ?? [])
                if (literal.TryGetValue(occurrence.Address, out var translation))
                {
                    var slot = translation.Slot;
                    if (!found.Add(occurrence.Address) || slot.SourceText != occurrence.Text || slot.Context != occurrence.Context || slot.AllowedEmpty != occurrence.AllowedEmpty) throw new EditRejectedException(EditFailure.Precondition, "Literal operation differs from its actual source slot");
                    ValidateLiteral(translation, snapshot.Manifest.SnapshotId.Digest);
                }
        }
        if (found.Count != literal.Count) throw new EditRejectedException(EditFailure.Precondition, "Literal target is not an extracted text slot");
        foreach (var operation in requested.OfType<TypedPatch>())
            if (operation.Address.Dimension != sources[(operation.Address.Artifact, operation.Address.Chunk)].Location.Dimension) throw new EditRejectedException(EditFailure.Precondition, "Typed target source binding differs");
        foreach (var artifactGroup in requested.OfType<TypedPatch>().GroupBy(operation => operation.Address.Artifact))
        {
            var artifact = artifactGroup.Key;
            var bytes = inventory.Read(artifact);
            if (Hash256.Compute(bytes) != sources[(artifact, null)].ContentHash)
                throw new ArtifactConflictException("Typed source payload changed during plan validation");
            var region = artifact.Kind is ArtifactKind.Region or ArtifactKind.Entities ? Next.Formats.RegionFile.Read(bytes) : null;
            foreach (var group in artifactGroup.GroupBy(operation => operation.Address.Chunk))
            {
                var document = group.Key is { } chunk
                    ? region!.ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document
                    : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
                _ = NbtTextExtractor.Profile(sources[(artifact, group.Key)].Location, document);
                _ = TypedTreeEdits.Apply(document, group.Select(operation => operation.Edit));
            }
        }
        foreach (var operation in requested.OfType<KeyConversionPatch>())
        {
            var conversion = operation.Conversion; var address = conversion.Address; var source = sources[(address.Artifact, address.Chunk)];
            if (WorldReferenceService.IsIdentifyingName(conversion.Slot.Role)) throw new EditRejectedException(EditFailure.Precondition, "Identifying name conversion requires verified reference authority");
            var bytes = ReadSource(source, snapshot.Limits);
            CapabilityProfile profile; var roots = new List<SourceAddress>();
            if (source.Location.Role == WorldSourceRole.Function && source.Pack is { } functionPack)
            {
                profile = MinecraftProfiles.ForPackMetadata(functionPack) ?? throw new NotSupportedException("Command key conversion profile is unknown");
                var command = WorldCommandEdits.BindFunction(address.Artifact, bytes, profile, source.Location.Dimension);
                _ = CommandTextExtractor.Extract(command, profile, snapshot.Limits, snapshot.Manifest.Scope, roots.Add);
                _ = WorldCommandEdits.Rewrite(command, [], profile, snapshot.Limits, [conversion]);
            }
            else if (source.Location.Role is WorldSourceRole.PackJson or WorldSourceRole.PackMetadata && source.Pack is { } pack)
            {
                profile = MinecraftProfiles.ForPackMetadata(pack) ?? throw new NotSupportedException("Key conversion pack profile is unknown");
                _ = PackTextExtractor.Extract(source.Location, bytes, pack, snapshot.Limits, snapshot.Manifest.Scope, roots.Add);
                _ = WorldJsonKeyEdits.Convert(bytes, [conversion], snapshot.Limits);
            }
            else
            {
                var document = address.Chunk is { } chunk ? Next.Formats.RegionFile.Read(bytes).ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
                profile = NbtTextExtractor.Profile(source.Location, document);
                var extracted = NbtTextExtractor.Extract(source.Location, document, source.Chunk, snapshot.Limits, snapshot.StorageRules.GetValueOrDefault(address.Artifact, []), snapshot.Manifest.Scope, snapshot.Discovery.Metadata?.Profile, roots.Add);
                if (address.Syntax is not null)
                {
                    var command = extracted.PendingCommands.SingleOrDefault(item => item.Address.NodePath.Equals(address.NodePath)) ?? throw new EditRejectedException(EditFailure.Precondition, "Command key target is not a real NBT command field");
                    _ = CommandTextExtractor.Extract(command, profile, snapshot.Limits, snapshot.Manifest.Scope, roots.Add);
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
            if (profile.Id != conversion.Profile || profile.Version != conversion.ProfileVersion) throw new NotSupportedException("Key conversion requires its exact text profile");
            if (!roots.Contains(address))
                throw new EditRejectedException(EditFailure.Precondition, "Key conversion target is not a profile-declared text component");
            var language = sources[(conversion.Dependency.LanguageArtifact, null)];
            if (language.Location.Role != WorldSourceRole.Language || language.Pack?.Kind != PackSourceKind.ResourcePack)
                throw new EditRejectedException(EditFailure.Precondition, "Key conversion language dependency is not an exact resource language source");
            if (!language.Pack.DeclaresSupport(MinecraftProfiles.RequiredResourcePackFormat(profile))) throw new NotSupportedException("Resource language pack does not support the target component profile");
            var languagePath = language.Location.Artifact.ContainerEntry ?? language.Location.Artifact.RelativePath;
            if (!languagePath.EndsWith("/" + conversion.Dependency.Locale + ".json", StringComparison.Ordinal)) throw new EditRejectedException(EditFailure.Precondition, "Language dependency locale differs from its path");
        }
        CheckPreimages(inventory, plan);
        return new(plan, sources.ToImmutableDictionary());
    }
    internal byte[] ReadSource(DiscoveredSource source, DecodeLimits limits)
    {
        var artifact = source.Location.Artifact;
        var outer = artifact.ContainerEntry is null ? artifact : new ArtifactAddress(artifact.WorldId, artifact.RelativePath, artifact.Kind);
        var bytes = inventory.Read(outer);
        if (artifact.ContainerEntry is { } name)
        {
            using var zip = Next.Formats.ZipArchiveValidation.OpenRead(bytes, limits);
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
    private static void ValidateLiteral(LiteralTranslation translation, Hash256 snapshot)
    {
        var slot = translation.Slot;
        if (translation.Reply.Text != slot.SourceText && WorldReferenceService.IsIdentifyingName(slot.Role))
            throw new EditRejectedException(EditFailure.Precondition, "Identifying names require an explicitly verified complete reference synchronization");
        var id = Guid.NewGuid(); var pageId = Hash256.Compute([]); var format = MinecraftPlaceholderProfiles.ForRole(slot.Role);
        var page = new SessionPage(id, 0, snapshot, 0, pageId, slot.SourceLocale, slot.TargetLocale, "literal-v1", false, [new(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens, format)]);
        var reply = new SessionReply(Guid.NewGuid(), id, 0, snapshot, 0, pageId, slot.TargetLocale, [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, translation.Reply.Text)]);
        var validation = SessionReplyValidator.Validate(page, [slot], reply, new Dictionary<Hash256, PlaceholderFormat> { [slot.Id.Digest] = format });
        if (!validation.Valid) throw new SessionReplyRejectedException(validation.Issues);
    }
}

internal sealed record BoundWorldPlan(PatchPlan Plan, ImmutableDictionary<(ArtifactAddress Artifact, ChunkPosition? Chunk), DiscoveredSource> Sources);

public sealed class ReferencePolicyException(string message = "Multiple reference groups require the current verified policy; create a fresh plan, or rollback legacy pending journals before replanning.") : NotSupportedException(message);

using Next.Domain;
using Next.Minecraft;
using System.Collections.Immutable;

namespace Next.Application;

public sealed class WorldPlanBuilder(IWorldInventory inventory)
{
    internal const string Policy = "world-prepare-v1";
    internal static readonly string[] Checks = ["parse", "preimage", "profile", "projection"];
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    public PatchPlan Build(WorldQuerySnapshot snapshot, IEnumerable<PatchOperation> operations)
        => Bind(snapshot, operations).Plan;
    internal BoundWorldPlan Bind(WorldQuerySnapshot snapshot, IEnumerable<PatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(operations);
        if (snapshot.Manifest.WorldId != inventory.WorldId) throw new ArgumentException("Snapshot belongs to another world", nameof(snapshot));
        var requested = operations.Take(100_001).ToArray();
        if (requested.Length > 100_000 || requested.Any(operation => operation is null)) throw new ArgumentException("Invalid or oversized operation set", nameof(operations));
        if (requested.Any(operation => operation is not (LiteralPatch or TypedPatch))) throw new NotSupportedException("This preparation path requires explicit literal or typed operations");
        if (requested.OfType<TypedPatch>().Any(operation => operation.Address.Artifact.Kind is not (ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities))) throw new NotSupportedException("Typed edits require an NBT document");
        var outputs = requested.SelectMany(operation => operation.RequiredOutputs).Distinct().ToArray();
        if (outputs.Any(artifact => artifact.WorldId != inventory.WorldId || artifact.Kind is not (ArtifactKind.Nbt or ArtifactKind.Json or ArtifactKind.McFunction or ArtifactKind.Region or ArtifactKind.Entities or ArtifactKind.Archive))) throw new NotSupportedException("Physical artifact preparation profile is not implemented");
        static ArtifactAddress Outer(ArtifactAddress artifact) => artifact.ContainerEntry is null ? artifact : new(artifact.WorldId, artifact.RelativePath, artifact.Kind);
        var sources = snapshot.Discovery.Sources.Where(source => outputs.Contains(Outer(source.Location.Artifact))).ToDictionary(source => (source.Location.Artifact, source.Chunk));
        var required = outputs.Select(artifact => (artifact, (ChunkPosition?)null)).Concat(requested.SelectMany(operation => operation.Targets).Select(address => (address.Artifact, address.Chunk))).Distinct().ToArray();
        if (required.Any(key => !sources.TryGetValue(key, out var source) || source.ContentHash is null || source.Capability.Read != SupportLevel.Supported || source.Outcome != SourceOutcome.Success)) throw new EditRejectedException(EditFailure.Precondition, "Plan output has no supported exact source binding");
        var inputs = new Dictionary<ArtifactAddress, ArtifactPrecondition>();
        void Input(ArtifactAddress artifact, Hash256 hash)
        {
            var value = new ArtifactPrecondition(artifact, ArtifactImage.Present(hash));
            if (inputs.TryGetValue(artifact, out var prior) && prior != value) throw new ArtifactConflictException("Source metadata has inconsistent preimages"); inputs[artifact] = value;
        }
        foreach (var source in outputs.Select(artifact => sources[(artifact, null)]))
        {
            Input(source.Location.Artifact, source.ContentHash!);
            if (source.Location.Artifact.Kind != ArtifactKind.Archive && source.Location.PackRoot is { } root)
                Input(new(inventory.WorldId, root + "/pack.mcmeta", ArtifactKind.Json), source.PackMetadataHash ?? throw new ArtifactConflictException("Pack metadata binding is missing"));
        }
        foreach (var metadata in snapshot.Discovery.MetadataSources.IsDefault ? [] : snapshot.Discovery.MetadataSources) Input(metadata.Artifact, metadata.ContentHash!);
        var plan = new PatchPlan(inventory.WorldId, snapshot.Manifest.SnapshotId, snapshot.Manifest.Versions.Profile, Policy, requested,
            inputs.Values, outputs.Select(artifact => new OutputDeclaration(artifact, OutputIntent.Write, Checks)), []);
        CheckPreimages(inventory, plan);
        var scope = new SnapshotScope(snapshot.Manifest.Scope.Dimensions, snapshot.Manifest.Scope.Regions, snapshot.Manifest.Scope.Types, outputs.Select(artifact => artifact.RelativePath));
        var rules = snapshot.StorageRules.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<StorageTextRule>)pair.Value);
        var extraction = new WorldTextExtraction(inventory, new(scope, snapshot.Limits), rules);
        var literal = requested.OfType<LiteralPatch>().ToDictionary(operation => operation.Translation.Slot.Address); var found = new HashSet<SourceAddress>();
        foreach (var row in extraction.ReadSources())
        {
            if (!sources.TryGetValue((row.Source.Location.Artifact, row.Source.Chunk), out var expected)) continue;
            if (row.Source.Location != expected.Location || row.Source.ContentHash != expected.ContentHash || row.Source.PackMetadataHash != expected.PackMetadataHash) throw new ArtifactConflictException("Source interpretation changed after scanning");
            foreach (var occurrence in row.Text?.Occurrences ?? [])
                if (literal.TryGetValue(occurrence.Address, out var operation))
                {
                    var slot = operation.Translation.Slot;
                    if (!found.Add(occurrence.Address) || slot.SourceText != occurrence.Text || slot.Context != occurrence.Context || slot.AllowedEmpty != occurrence.AllowedEmpty) throw new EditRejectedException(EditFailure.Precondition, "Literal operation differs from its actual source slot");
                    ValidateLiteral(operation.Translation, snapshot.Manifest.SnapshotId.Digest);
                }
        }
        if (found.Count != literal.Count) throw new EditRejectedException(EditFailure.Precondition, "Literal target is not an extracted text slot");
        foreach (var operation in requested.OfType<TypedPatch>())
            if (operation.Address.Dimension != sources[(operation.Address.Artifact, operation.Address.Chunk)].Location.Dimension) throw new EditRejectedException(EditFailure.Precondition, "Typed target source binding differs");
        foreach (var group in requested.OfType<TypedPatch>().GroupBy(operation => (operation.Address.Artifact, operation.Address.Chunk)))
        {
            var bytes = inventory.Read(group.Key.Artifact);
            var document = group.Key.Chunk is { } chunk ? Next.Formats.RegionFile.Read(bytes).ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
            _ = NbtTextExtractor.Profile(sources[group.Key].Location, document);
            _ = TypedTreeEdits.Apply(document, group.Select(operation => operation.Edit));
        }
        CheckPreimages(inventory, plan);
        return new(plan, sources.ToImmutableDictionary());
    }
    internal static void CheckPreimages(IWorldInventory inventory, PatchPlan plan)
    {
        foreach (var input in plan.Preconditions)
        {
            byte[] bytes; try { bytes = inventory.Read(input.Artifact); } catch (FileNotFoundException) { throw new ArtifactConflictException("A bound source disappeared"); }
            if (ArtifactImage.Present(Hash256.Compute(bytes)) != input.Image) throw new ArtifactConflictException("A bound source changed");
        }
    }
    private static void ValidateLiteral(LiteralTranslation translation, Hash256 snapshot)
    {
        var slot = translation.Slot;
        if (translation.Reply.Text != slot.SourceText && slot.Role is "entity.name" or "armor_stand.name" or "entity.name.fallback" or "armor_stand.name.fallback")
            throw new EditRejectedException(EditFailure.Precondition, "Identifying names require an explicitly verified complete reference synchronization");
        var id = Guid.NewGuid(); var pageId = Hash256.Compute([]); var format = MinecraftPlaceholderProfiles.ForRole(slot.Role);
        var page = new SessionPage(id, 0, snapshot, 0, pageId, slot.SourceLocale, slot.TargetLocale, "literal-v1", false, [new(slot.Id.Digest, slot.SourceTextHash, slot.SourceText, slot.Context, slot.AllowedEmpty, slot.Placeholders.Tokens, format)]);
        var reply = new SessionReply(Guid.NewGuid(), id, 0, snapshot, 0, pageId, slot.TargetLocale, [new(slot.Id.Digest, slot.SourceTextHash, SessionReplyState.Translated, translation.Reply.Text)]);
        var validation = SessionReplyValidator.Validate(page, [slot], reply, new Dictionary<Hash256, PlaceholderFormat> { [slot.Id.Digest] = format });
        if (!validation.Valid) throw new SessionReplyRejectedException(validation.Issues);
    }
}

internal sealed record BoundWorldPlan(PatchPlan Plan, ImmutableDictionary<(ArtifactAddress Artifact, ChunkPosition? Chunk), DiscoveredSource> Sources);

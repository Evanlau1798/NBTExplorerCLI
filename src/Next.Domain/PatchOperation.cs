using System.Collections.Immutable;

namespace Next.Domain;

public abstract record PatchOperation
{
    public string Id { get; }
    public PatchKind Kind { get; }
    private protected PatchOperation(string id, PatchKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        (Id, Kind) = (id, kind);
    }
    public abstract ImmutableArray<SourceAddress> Targets { get; }
    public abstract ImmutableArray<ArtifactAddress> RequiredOutputs { get; }
}

public sealed record TypedPatch : PatchOperation
{
    public SourceAddress Address { get; }
    public TypedEdit Edit { get; }
    public TypedPatch(string id, SourceAddress address, TypedEdit edit) : base(id, PatchKind.TypedEdit)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(edit);
        NbtDocumentScope.RequireTarget(address, address);
        if (!address.NodePath.Equals(edit.Path)) throw new ArgumentException("Typed operation address must match the edit path");
        if (edit.Action == TypedEditAction.Rename && edit.Path.Segments[^1].Kind != PathSegmentKind.Key)
            throw new ArgumentException("Only compound keys can be renamed");
        (Address, Edit) = (address, edit);
    }
    public override ImmutableArray<SourceAddress> Targets => [Address];
    public override ImmutableArray<ArtifactAddress> RequiredOutputs => [PlanArtifacts.Outer(Address.Artifact)];
}
public sealed record LiteralPatch : PatchOperation
{
    public LiteralTranslation Translation { get; }
    public LiteralPatch(string id, LiteralTranslation translation) : base(id, PatchKind.LiteralTranslation)
    {
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(translation.Slot);
        if (translation.Slot.Validate(translation.Reply) != SlotValidation.Valid) throw new ArgumentException("Invalid literal reply", nameof(translation));
        Translation = translation;
    }
    public override ImmutableArray<SourceAddress> Targets => [Translation.Slot.Address];
    public override ImmutableArray<ArtifactAddress> RequiredOutputs => [PlanArtifacts.Outer(Translation.Slot.Address.Artifact)];
}
public sealed record KeyConversionPatch : PatchOperation
{
    public TranslationKeyConversion Conversion { get; }
    public KeyConversionPatch(string id, TranslationKeyConversion conversion) : base(id, PatchKind.TranslationKeyConversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        conversion.Validate();
        Conversion = conversion;
    }
    public override ImmutableArray<SourceAddress> Targets => [Conversion.Address,
        new(Conversion.Dependency.LanguageArtifact, new([PathSegment.ForKey(Conversion.Dependency.Key)]), "lang.value")];
    public override ImmutableArray<ArtifactAddress> RequiredOutputs =>
        [PlanArtifacts.Outer(Conversion.Address.Artifact), PlanArtifacts.Outer(Conversion.Dependency.LanguageArtifact)];
}
public sealed record ReferencePatch : PatchOperation
{
    public ReferenceSynchronization Synchronization { get; }
    public ReferencePatch(string id, ReferenceSynchronization synchronization) : base(id, PatchKind.ReferenceSynchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (!synchronization.Authorizes(synchronization.Members)) throw new ArgumentException("Reference coverage must be Complete", nameof(synchronization));
        Synchronization = synchronization;
    }
    public override ImmutableArray<SourceAddress> Targets => Synchronization.Members.Select(member => member.Address).ToImmutableArray();
    public override ImmutableArray<ArtifactAddress> RequiredOutputs => Targets.Select(address => PlanArtifacts.Outer(address.Artifact)).Distinct().ToImmutableArray();
}
public sealed record RestorePatch : PatchOperation
{
    public ArtifactRestore Restore { get; }
    public RestorePatch(string id, ArtifactRestore restore) : base(id, PatchKind.ArtifactRestore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        Restore = restore;
    }
    public override ImmutableArray<SourceAddress> Targets => [];
    public override ImmutableArray<ArtifactAddress> RequiredOutputs => Restore.AuthorizedArtifacts.Select(item => item.Artifact).ToImmutableArray();
}

public sealed record ResourcePatch : PatchOperation
{
    public ResourceGeneration Generation { get; }
    public ResourcePatch(string id, ResourceGeneration generation) : base(id, PatchKind.ResourceGeneration)
    {
        ArgumentNullException.ThrowIfNull(generation);
        Generation = generation;
    }
    public override ImmutableArray<SourceAddress> Targets => [];
    public override ImmutableArray<ArtifactAddress> RequiredOutputs => Generation.Outputs;
}

internal static class PlanArtifacts
{
    public static ArtifactAddress Outer(ArtifactAddress artifact) => artifact.ContainerEntry is null ? artifact
        : new(artifact.WorldId, artifact.RelativePath, artifact.Kind);
}

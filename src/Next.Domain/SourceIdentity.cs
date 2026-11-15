namespace Next.Domain;

public static class SourceIdentity
{
    public static StableIdentity Artifact(ArtifactAddress artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var writer = new IdentityWriter(IdentityKind.Artifact);
        writer.Text(artifact.WorldId.ToString("D"));
        writer.Byte((byte)artifact.Kind);
        writer.Text(artifact.RelativePath);
        writer.Optional(artifact.ContainerEntry);
        return writer.Finish();
    }
    public static StableIdentity Occurrence(SourceAddress address)
    {
        var writer = new IdentityWriter(IdentityKind.Occurrence);
        Location(writer, address);
        writer.Text(address.SemanticRole);
        return writer.Finish();
    }
    public static StableIdentity Slot(StableIdentity occurrence, TypedPath componentPath, string role)
    {
        ArgumentNullException.ThrowIfNull(componentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var writer = new IdentityWriter(IdentityKind.Slot);
        writer.Parent(occurrence, IdentityKind.Occurrence);
        writer.Path(componentPath);
        writer.Text(role);
        return writer.Finish();
    }
    public static StableIdentity Group(string kind, SourceAddress container, int ruleVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfLessThan(ruleVersion, 1);
        var writer = new IdentityWriter(IdentityKind.Group);
        writer.Text(kind);
        Location(writer, container);
        writer.Int(ruleVersion);
        return writer.Finish();
    }
    public static StableIdentity Members(StableIdentity group, IEnumerable<StableIdentity> members, long revision)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        var writer = new IdentityWriter(IdentityKind.Members);
        writer.Parent(group, IdentityKind.Group);
        writer.Long(revision);
        var ordered = members.Take(100_001).ToArray();
        if (ordered.Length > 100_000) throw new ArgumentException("Group exceeds 100000 members", nameof(members));
        writer.Int(ordered.Length);
        foreach (var member in ordered)
        {
            if (member is null || member.Kind is not (IdentityKind.Occurrence or IdentityKind.Slot))
                throw new ArgumentException("Group members must be occurrence or slot references", nameof(members));
            writer.Parent(member, member.Kind);
        }
        return writer.Finish();
    }
    public static StableIdentity Memory(string source, string sourceLocale, string targetLocale, string contextClass,
        string placeholders, string glossaryVersion, string profileVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placeholders);
        var writer = new IdentityWriter(IdentityKind.Memory);
        writer.Text(source);
        foreach (var field in new[] { sourceLocale, targetLocale, contextClass })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            writer.Text(field);
        }
        writer.Text(placeholders);
        foreach (var field in new[] { glossaryVersion, profileVersion })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            writer.Text(field);
        }
        return writer.Finish();
    }
    public static StableIdentity Snapshot(Guid worldId, IEnumerable<ManifestSource> sources, SnapshotScope scope,
        InterpretationVersions versions)
    {
        if (worldId == Guid.Empty) throw new ArgumentException("World identity is required", nameof(worldId));
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(versions);
        var manifest = sources.Take(100_001).ToArray();
        if (manifest.Length > 100_000) throw new ArgumentException("Manifest exceeds 100000 sources", nameof(sources));
        var artifacts = new HashSet<ArtifactAddress>();
        foreach (var source in manifest)
        {
            if (source is null || source.Artifact is null || source.Artifact.WorldId != worldId || !Enum.IsDefined(source.Outcome)
                || (source.Outcome is SourceOutcome.Success or SourceOutcome.Irrelevant && source.ContentHash is null)
                || !artifacts.Add(source.Artifact)) throw new ArgumentException("Invalid or duplicate manifest source", nameof(sources));
        }
        var writer = new IdentityWriter(IdentityKind.Snapshot);
        writer.Text(worldId.ToString("D"));
        writer.Set(scope.Dimensions);
        writer.Set(scope.Regions);
        writer.Set(scope.Types);
        writer.Set(scope.ArtifactPaths);
        foreach (var version in new[] { versions.Codec, versions.Profile, versions.Rules, versions.Policy })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            writer.Text(version);
        }
        writer.Int(manifest.Length);
        foreach (var source in manifest.OrderBy(item => item.Artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.Artifact.ContainerEntry, StringComparer.Ordinal).ThenBy(item => item.Artifact.Kind))
        {
            writer.Parent(Artifact(source.Artifact), IdentityKind.Artifact);
            writer.Optional(source.ContentHash?.Hex);
            writer.Byte((byte)source.Outcome);
            writer.Optional(source.DiagnosticCode);
        }
        return writer.Finish();
    }

    private static void Location(IdentityWriter writer, SourceAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        writer.Parent(Artifact(address.Artifact), IdentityKind.Artifact);
        writer.Optional(address.Dimension);
        writer.Byte(address.Chunk is null ? (byte)0 : (byte)1);
        if (address.Chunk is not null) { writer.Int(address.Chunk.X); writer.Int(address.Chunk.Z); }
        writer.Path(address.NodePath);
        writer.Path(address.ComponentPath);
        writer.Byte((byte)address.JsonTarget);
        writer.Byte(address.Syntax is null ? (byte)0 : (byte)1);
        if (address.Syntax is not null) { writer.Text(address.Syntax.GrammarProfile); writer.Int(address.Syntax.GrammarVersion); }
    }
}

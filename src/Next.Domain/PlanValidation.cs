namespace Next.Domain;

internal static class PlanValidation
{
    public static void Validate(PatchPlan plan)
    {
        ValidateSnapshot(plan);
        if (plan.Operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count() != plan.Operations.Length)
            throw new ArgumentException("Plan operation IDs must be unique");
        foreach (var input in plan.Preconditions)
        {
            Physical(plan, input.Artifact);
            ArgumentNullException.ThrowIfNull(input.Image);
        }
        foreach (var output in plan.Outputs) Physical(plan, output.Artifact);
        var physicalPaths = plan.Preconditions.Select(input => input.Artifact.RelativePath)
            .Concat(plan.Outputs.Select(output => output.Artifact.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var orderedPaths = physicalPaths.Select(path => path + "/").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 1; index < orderedPaths.Length; index++)
            if (orderedPaths[index].StartsWith(orderedPaths[index - 1], StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A physical artifact cannot also be another artifact's parent directory");
        var inputs = plan.Preconditions.ToDictionary(item => item.Artifact.RelativePath, StringComparer.OrdinalIgnoreCase);
        var outputs = plan.Outputs.ToDictionary(item => item.Artifact.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var output in plan.Outputs)
            if (!inputs.TryGetValue(output.Artifact.RelativePath, out var input) || input.Artifact != output.Artifact)
                throw new ArgumentException("Every output requires an exact raw preimage or expectedAbsent precondition");
        var required = new HashSet<ArtifactAddress>();
        var restored = new HashSet<ArtifactAddress>();
        var edited = new HashSet<ArtifactAddress>();
        var targets = new List<SourceAddress>();
        var requiredCount = 0;
        foreach (var operation in plan.Operations)
        {
            foreach (var artifact in operation.RequiredOutputs)
            {
                if (++requiredCount > 100_000) throw new ArgumentException("Plan exceeds the aggregate artifact budget");
                Physical(plan, artifact);
                if (!outputs.TryGetValue(artifact.RelativePath, out var output) || output.Artifact != artifact)
                    throw new ArgumentException("Operation output/dependency is not declared");
                required.Add(artifact);
                if (operation is RestorePatch)
                {
                    if (!restored.Add(artifact)) throw new ArgumentException("Multiple restores target the same artifact");
                }
                else
                {
                    edited.Add(artifact);
                    if (output.Intent != OutputIntent.Write) throw new ArgumentException("Semantic edits cannot declare raw artifact deletion");
                }
            }
            foreach (var target in MutationTargets(operation))
            {
                if (targets.Count >= 100_000) throw new ArgumentException("Plan exceeds the aggregate target budget");
                targets.Add(target);
                var artifact = PlanArtifacts.Outer(target.Artifact);
                var input = inputs[artifact.RelativePath];
                var newLanguageOutput = operation is KeyConversionPatch key && target.Artifact == key.Conversion.Dependency.LanguageArtifact;
                if (!newLanguageOutput && input.Image.ExpectedAbsent) throw new ArgumentException("Semantic targets require an existing source image");
                if (target.Artifact.Kind == ArtifactKind.McFunction && target.Syntax is not null && target.Syntax.PreimageHash != input.Image.Hash?.Hex)
                    throw new ArgumentException("Syntax locator preimage differs from the plan's raw input");
            }
            if (operation is RestorePatch restore)
                foreach (var entry in restore.Restore.AuthorizedArtifacts)
                {
                    if (inputs[entry.Artifact.RelativePath].Image != restore.Restore.CurrentImages[entry.Artifact]
                        || outputs[entry.Artifact.RelativePath].Intent != (entry.Image.ExpectedAbsent ? OutputIntent.Delete : OutputIntent.Write))
                        throw new ArgumentException("Restore output and raw precondition must match its authorization");
                }
        }
        if (restored.Overlaps(edited) || !required.SetEquals(plan.Outputs.Select(output => output.Artifact)))
            throw new ArgumentException("Plan contains hidden outputs or competing raw restore/semantic edits");
        foreach (var dependency in plan.Dependencies)
        {
            Physical(plan, dependency.Consumer);
            Physical(plan, dependency.Required);
            if (dependency.Consumer == dependency.Required || !required.Contains(dependency.Consumer) || !required.Contains(dependency.Required))
                throw new ArgumentException("Output dependencies must reference distinct declared outputs");
        }
        ValidateTargets(targets);
    }

    private static void ValidateSnapshot(PatchPlan plan)
    {
        if (plan.WorldId == Guid.Empty || plan.SnapshotId.Kind != IdentityKind.Snapshot || !plan.SnapshotId.IsValid)
            throw new ArgumentException("Plan requires a valid snapshot identity");
        if (IdentityCanonical.ReadWorld(plan.SnapshotId) != plan.WorldId)
            throw new ArgumentException("Plan world does not match its snapshot");
    }
    private static void Physical(PatchPlan plan, ArtifactAddress artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.WorldId != plan.WorldId || artifact.ContainerEntry is not null) throw new ArgumentException("Expected a physical artifact in the plan world");
    }
    private static IEnumerable<SourceAddress> MutationTargets(PatchOperation operation)
    {
        foreach (var address in operation.Targets) yield return address;
        if (operation is TypedPatch { Edit.Action: TypedEditAction.Rename } typed && typed.Edit.NewName != typed.Address.NodePath.Segments[^1].Key)
        {
            var address = typed.Address;
            var renamed = new TypedPath(address.NodePath.Segments.SetItem(address.NodePath.Segments.Length - 1, PathSegment.ForKey(typed.Edit.NewName!)));
            yield return new(address.Artifact, renamed, address.SemanticRole, address.Dimension, address.Chunk);
        }
    }
    private static void ValidateTargets(IEnumerable<SourceAddress> addresses)
    {
        foreach (var group in addresses.GroupBy(address => (address.Artifact.WorldId, address.Artifact.RelativePath,
            address.Artifact.ContainerEntry, address.Chunk)))
        {
            ValidateSyntaxBindings(group);
            var paths = group.Select(address => address.NodePath.Segments.Concat(address.Syntax?.NodePath?.Segments ?? []).Concat(address.ComponentPath?.Segments ?? []).ToArray()).ToArray();
            Array.Sort(paths, ComparePaths);
            for (var index = 1; index < paths.Length; index++)
                if (paths[index - 1].Length <= paths[index].Length && paths[index - 1].SequenceEqual(paths[index].Take(paths[index - 1].Length)))
                    throw new ArgumentException("Plan has overlapping target authorizations");
        }
    }
    private static void ValidateSyntaxBindings(IEnumerable<SourceAddress> addresses)
    {
        foreach (var container in addresses.GroupBy(address => address.NodePath))
        {
            if (!container.Any(address => address.Syntax is not null)) continue;
            if (container.Any(address => address.Syntax is null) || container.Select(address =>
                (address.Syntax!.GrammarProfile, address.Syntax.GrammarVersion, address.Syntax.PreimageHash, address.Syntax.Encoding, address.Syntax.Newline)).Distinct().Count() != 1)
                throw new ArgumentException("Plan has inconsistent syntax container bindings");
            var spans = new List<SyntaxLocator>();
            foreach (var leaf in container.GroupBy(address => address.Syntax!.NodePath))
            {
                var syntax = leaf.First().Syntax!;
                if (leaf.Any(address => address.Syntax!.StartByte != syntax.StartByte || address.Syntax.LengthBytes != syntax.LengthBytes))
                    throw new ArgumentException("One syntax node has inconsistent spans");
                spans.Add(syntax);
            }
            var end = -1L;
            foreach (var span in spans.OrderBy(syntax => syntax.StartByte))
            {
                if (span.StartByte < end) throw new ArgumentException("Plan has overlapping syntax nodes");
                end = span.StartByte + span.LengthBytes;
            }
        }
    }
    private static int ComparePaths(PathSegment[] left, PathSegment[] right)
    {
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var order = left[index].Kind.CompareTo(right[index].Kind);
            if (order == 0) order = left[index].Kind == PathSegmentKind.Key ? StringComparer.Ordinal.Compare(left[index].Key, right[index].Key)
                : left[index].Index!.Value.CompareTo(right[index].Index!.Value);
            if (order != 0) return order;
        }
        return left.Length.CompareTo(right.Length);
    }
}

namespace Next.Verification;

internal sealed record VerificationContext(string FixtureRoot, string? ArtifactRoot);
internal sealed record VerificationCase(string Name, string Suite, Action<VerificationContext> Test);

internal static class CaseRegistry
{
    public static readonly VerificationCase[] Cases =
    [
        new("state-roots-are-product-isolated", "foundation", _ => BootstrapTests.StateRoots()),
        new("help-identifies-implemented-commands", "foundation", _ => BootstrapTests.Help()),
        new("version-identifies-new-product", "foundation", _ => BootstrapTests.Version()),
        new("unknown-command-is-machine-readable-failure", "foundation", _ => BootstrapTests.UnknownCommand()),
        new("architecture-detects-process-api", "architecture", _ => AssemblyPolicyTests.RejectsProcessApi()),
        new("architecture-detects-domain-io", "architecture", _ => AssemblyPolicyTests.RejectsDomainIO()),
        new("architecture-rejects-domain-cli-reference", "architecture", _ => AssemblyPolicyTests.RejectsDomainCliReference()),
        new("architecture-rejects-mismatched-pdb", "architecture", _ => AssemblyPolicyTests.RejectsMismatchedPdb()),
        new("architecture-detects-copied-source", "architecture", _ => AssemblyPolicyTests.RejectsCopiedSourceHash()),
        new("architecture-product-assemblies-independent", "architecture", context => AssemblyPolicyTests.ProductAssembliesAreIndependent(context.ArtifactRoot)),
        new("architecture-rejects-external-source", "architecture", _ => ArchitectureTests.BuildRejectsExternalSource()),
        new("architecture-rejects-old-project", "architecture", _ => ArchitectureTests.BuildRejectsOldProject()),
        new("architecture-rejects-late-source", "architecture", _ => ArchitectureTests.BuildRejectsLateSource()),
        new("architecture-rejects-direct-assembly", "architecture", _ => ArchitectureTests.BuildRejectsDirectAssembly()),
        new("architecture-rejects-assembly-hint-path", "architecture", _ => ArchitectureTests.BuildRejectsAssemblyHintPath()),
        new("architecture-rejects-domain-package", "architecture", _ => ArchitectureTests.BuildRejectsDomainPackage()),
        new("architecture-accepts-contained-source", "architecture", _ => ArchitectureTests.BuildAcceptsContainedSource()),
        new("corpus-manifest-is-complete", "corpus", context => CorpusTests.Manifest(context.FixtureRoot)),
        new("corpus-findings-have-independent-cases", "corpus", context => CorpusTests.Cases(context.FixtureRoot)),
        new("corpus-builder-small-contract", "corpus", _ => CorpusBuilderTests.SmallContract()),
        new("corpus-builder-deterministic", "corpus", _ => CorpusBuilderTests.Deterministic()),
        new("corpus-builder-protects-existing-output", "corpus", _ => CorpusBuilderTests.ProtectsExistingOutput()),
        new("runner-calibration-failure", "calibration", _ => throw new InvalidOperationException("刻意錯誤的 assertion")),
        new("runner-calibration-timeout", "calibration", _ =>
        {
            Console.Error.WriteLine("CALIBRATION_TIMEOUT_ENTERED");
            Thread.Sleep(30_000);
        }),
        new("runner-calibration-unsupported", "calibration", _ => throw new NotSupportedException("刻意未支援的校準案例")),
        new("runner-calibration-scratch-timeout", "calibration", _ =>
        {
            var root = ScratchDirectory.CreateChild();
            File.WriteAllText(Path.Combine(root, "cleanup-witness.txt"), "owned by worker");
            Console.Error.WriteLine("CALIBRATION_SCRATCH_CREATED");
            Thread.Sleep(30_000);
        })
    ];
}

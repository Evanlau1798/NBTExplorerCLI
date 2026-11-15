using System.Reflection.Metadata;
using System.Text.Json;

namespace Next.Verification;

internal static class AssemblyPolicyTests
{
    public static void RejectsDomainIO()
    {
        var errors = AssemblyPolicy.FindViolations(typeof(AssemblyPolicyTests).Assembly.Location, true, new HashSet<string>());
        Require(errors.Any(error => error.StartsWith("DomainBoundary:", StringComparison.Ordinal)),
            "具有 I/O／Console／serializer 的受控組件不能通過 Domain 邊界");
    }

    public static void RejectsDomainCliReference()
    {
        var errors = AssemblyPolicy.FindViolations(typeof(AssemblyPolicyTests).Assembly.Location, true, new HashSet<string>());
        Require(errors.Any(error => error == "AssemblyReference: nbtcli-next"), "Domain 不能反向引用 CLI 組件");
    }

    public static void RejectsMismatchedPdb()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var assembly = Path.Combine(root, "Next.Domain.dll");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Next.Domain.dll"), assembly);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "nbtcli-next.pdb"), Path.ChangeExtension(assembly, ".pdb"));
            var errors = AssemblyPolicy.FindViolations(assembly, true, new HashSet<string>());
            Require(errors.Any(error => error.StartsWith("SymbolsMismatch:", StringComparison.Ordinal)),
                "不得用其他組件的 PDB 隱藏真正編譯輸入");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void RejectsProcessApi()
    {
        var errors = AssemblyPolicy.FindViolations(typeof(AssemblyPolicyTests).Assembly.Location, false, new HashSet<string>());
        Require(errors.Any(error => error.StartsWith("ProcessApi:", StringComparison.Ordinal)),
            "具有 Process.Start 的受控 Verification 組件必須觸發 API 禁令");
    }

    public static void RejectsCopiedSourceHash()
    {
        var assembly = typeof(AssemblyPolicyTests).Assembly.Location;
        using var pdb = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb);
        var reader = provider.GetMetadataReader();
        var first = reader.GetDocument(reader.Documents.First());
        var knownCopiedHash = Convert.ToHexString(reader.GetBlobBytes(first.Hash));
        var errors = AssemblyPolicy.FindViolations(assembly, false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { knownCopiedHash });
        Require(errors.Any(error => error.StartsWith("CopiedSource:", StringComparison.Ordinal)),
            "編譯輸入 checksum 命中禁止來源時必須拒絕");
    }

    public static void ProductAssembliesAreIndependent(string? artifactRoot)
    {
        var root = artifactRoot ?? AppContext.BaseDirectory;
        var names = new[] { "Next.Domain", "Next.Formats", "Next.Minecraft", "Next.Application", "Next.Infrastructure", "nbtcli-next" };
        if (artifactRoot is not null)
        {
            var allowed = names.SelectMany(name => new[] { name + ".dll", name + ".pdb" })
                .Concat(["nbtcli-next.exe", "nbtcli-next.deps.json", "nbtcli-next.runtimeconfig.json"])
                .ToHashSet(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
            {
                if (Path.GetFileName(path) == "licenses" && Directory.Exists(path)) continue;
                Require(File.Exists(path) && allowed.Contains(Path.GetFileName(path)), $"UnexpectedArtifact: {Path.GetFileName(path)}");
            }
            using var deps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "nbtcli-next.deps.json")));
            foreach (var library in deps.RootElement.GetProperty("libraries").EnumerateObject())
                Require(names.Contains(library.Name.Split('/')[0], StringComparer.Ordinal)
                    && library.Value.GetProperty("type").GetString() == "project", $"UnexpectedDependency: {library.Name}");
        }
        VerifyLicenses(Path.Combine(root, "licenses"));
        var sourceManifest = Path.Combine(AppContext.BaseDirectory, "ArchitectureFixture", "legacy-source.json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(sourceManifest));
        var forbidden = json.RootElement.EnumerateArray().Select(item => item.GetProperty("sha256").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var errors = AssemblyPolicy.FindViolations(Path.Combine(root, name + ".dll"),
                name == "Next.Domain", forbidden);
            Require(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }
    }

    private static void VerifyLicenses(string directory)
    {
        Require(Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "Missing or linked license directory");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            Require(File.Exists(entry) && Path.GetFileName(entry) == "Unicode.md"
                && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0, $"UnexpectedLicenseArtifact: {Path.GetFileName(entry)}");
        var path = Path.Combine(directory, "Unicode.md");
        Require(File.Exists(path), "Missing Unicode license");
        using var stream = File.OpenRead(path);
        Require(stream.Length <= 65_536 && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))
            == "6F72F10D166B2C2E8A395E03E734C5AFC852B59AECA73CED124F6B9C96268D53", "Unicode license differs from the preserved notice");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

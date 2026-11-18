using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NBTExplorerCLI.Verification;

internal static class AssemblyPolicy
{
    private static readonly HashSet<string> ProductNames = new(StringComparer.Ordinal)
    {
        "NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats", "NBTExplorerCLI.Minecraft", "NBTExplorerCLI.Application", "NBTExplorerCLI.Infrastructure", "nbtcli"
    };

    private static readonly Dictionary<string, string[]> AllowedEdges = new(StringComparer.Ordinal)
    {
        ["NBTExplorerCLI.Domain"] = [],
        ["NBTExplorerCLI.Formats"] = ["NBTExplorerCLI.Domain"],
        ["NBTExplorerCLI.Minecraft"] = ["NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats"],
        ["NBTExplorerCLI.Application"] = ["NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats", "NBTExplorerCLI.Minecraft"],
        ["NBTExplorerCLI.Infrastructure"] = ["NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats", "NBTExplorerCLI.Minecraft", "NBTExplorerCLI.Application"],
        ["nbtcli"] = ["NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats", "NBTExplorerCLI.Minecraft", "NBTExplorerCLI.Application", "NBTExplorerCLI.Infrastructure"]
    };

    public static IReadOnlyList<string> FindViolations(string assemblyPath, bool domain, IReadOnlySet<string> baselineHashes)
    {
        var errors = new List<string>();
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var owner = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        domain |= owner == "NBTExplorerCLI.Domain";
        var allowed = domain ? [] : AllowedEdges.GetValueOrDefault(owner) ?? ProductNames.ToArray();
        if (owner != Path.GetFileNameWithoutExtension(assemblyPath)) errors.Add($"AssemblyIdentity: {owner}");
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            var product = ProductNames.Contains(name) && allowed.Contains(name, StringComparer.Ordinal);
            var adopted = !domain && owner == "NBTExplorerCLI.Formats" && name == "ICSharpCode.SharpZipLib";
            var framework = name is "System" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
            if (!product && !framework && !adopted)
                errors.Add($"AssemblyReference: {name}");
        }
        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            var ns = metadata.GetString(type.Namespace);
            var name = metadata.GetString(type.Name);
            if (ns == "System.Diagnostics" && name is "Process" or "ProcessStartInfo")
                errors.Add($"ProcessApi: {ns}.{name}");
            if (ns == "System.Runtime.Loader") errors.Add($"DynamicAssemblyLoading: {ns}.{name}");
            if (domain && (ns.StartsWith("System.IO", StringComparison.Ordinal) || (ns == "System" && name == "Console")
                || ns.StartsWith("System.Text.Json", StringComparison.Ordinal)
                || ns.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal)
                || ns.StartsWith("System.Xml.Serialization", StringComparison.Ordinal)))
                errors.Add($"DomainBoundary: {ns}.{name}");
        }
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var ns = metadata.GetString(type.Namespace);
            var typeName = metadata.GetString(type.Name);
            var method = metadata.GetString(member.Name);
            if (ns == "System.Reflection" && typeName == "Assembly" && method is "Load" or "LoadFrom" or "LoadFile" or "UnsafeLoadFrom")
                errors.Add($"DynamicAssemblyLoading: {ns}.{typeName}.{method}");
        }
        if (!pe.TryOpenAssociatedPortablePdb(assemblyPath, _ => File.OpenRead(Path.ChangeExtension(assemblyPath, ".pdb")),
            out var provider, out _) || provider is null)
        {
            errors.Add($"SymbolsMismatch: {assemblyPath}");
            return errors;
        }
        using (provider)
        {
            var pdb = provider.GetMetadataReader();
            foreach (var handle in pdb.Documents)
            {
                var document = pdb.GetDocument(handle);
                var source = Path.GetFullPath(pdb.GetString(document.Name)).Replace('\\', '/');
                var project = owner == "nbtcli" ? "NBTExplorerCLI.Cli" : owner;
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (ProductNames.Contains(owner) && !source.Contains($"/src/{project}/", comparison))
                    errors.Add($"CompileBoundary: {source}");
                if (baselineHashes.Contains(Convert.ToHexString(pdb.GetBlobBytes(document.Hash))))
                    errors.Add($"CopiedSource: {pdb.GetString(document.Name)}");
            }
        }
        return errors;
    }
}

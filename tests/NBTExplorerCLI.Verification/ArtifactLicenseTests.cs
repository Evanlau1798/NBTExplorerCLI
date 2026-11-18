namespace NBTExplorerCLI.Verification;

internal static class ArtifactLicenseTests
{
    public static void ExactLicenseAndArtifactSet()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            string[] names = ["NBTExplorerCLI.Domain", "NBTExplorerCLI.Formats", "NBTExplorerCLI.Minecraft", "NBTExplorerCLI.Application", "NBTExplorerCLI.Infrastructure", "nbtcli"];
            foreach (var name in names)
                foreach (var extension in new[] { ".dll", ".pdb" })
                    File.Copy(Path.Combine(AppContext.BaseDirectory, name + extension), Path.Combine(root, name + extension));
            foreach (var name in new[] { "nbtcli.exe", "nbtcli.deps.json", "nbtcli.runtimeconfig.json", "ICSharpCode.SharpZipLib.dll" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(root, name));
            var directory = Path.Combine(root, "licenses"); Directory.CreateDirectory(directory);
            var original = Path.Combine(AppContext.BaseDirectory, "licenses", "Unicode.md");
            var license = Path.Combine(directory, "Unicode.md"); File.Copy(original, license);
            var sharpOriginal = Path.Combine(AppContext.BaseDirectory, "licenses", "SharpZipLib.md");
            var sharpLicense = Path.Combine(directory, "SharpZipLib.md"); File.Copy(sharpOriginal, sharpLicense);
            AssemblyPolicyTests.ProductAssembliesAreIndependent(root);
            var depsPath = Path.Combine(root, "nbtcli.deps.json"); var depsText = File.ReadAllText(depsPath);
            File.WriteAllText(depsPath, depsText.Replace("SharpZipLib/1.4.2", "SharpZipLib/9.9.9", StringComparison.Ordinal)); Reject(root);
            File.WriteAllText(depsPath, depsText);
            var missingPackage = System.Text.Json.Nodes.JsonNode.Parse(depsText)!;
            missingPackage["libraries"]!.AsObject().Remove("SharpZipLib/1.4.2");
            File.WriteAllText(depsPath, missingPackage.ToJsonString()); Reject(root);
            File.WriteAllText(depsPath, depsText);
            File.Delete(sharpLicense); Reject(root); File.Copy(sharpOriginal, sharpLicense);
            File.AppendAllText(sharpLicense, "tampered"); Reject(root); File.Copy(sharpOriginal, sharpLicense, overwrite: true);
            var dependency = Path.Combine(root, "ICSharpCode.SharpZipLib.dll");
            File.AppendAllText(dependency, "tampered"); Reject(root);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ICSharpCode.SharpZipLib.dll"), dependency, overwrite: true);
            var domainErrors = AssemblyPolicy.FindViolations(Path.Combine(root, "NBTExplorerCLI.Formats.dll"), true, new HashSet<string>());
            if (!domainErrors.Contains("AssemblyReference: ICSharpCode.SharpZipLib")) throw new InvalidOperationException("Adopted compression dependency cannot enter Domain");
            File.AppendAllText(license, "tampered"); Reject(root); File.Copy(original, license, overwrite: true);
            var extra = Path.Combine(directory, "unexpected.dll"); File.WriteAllText(extra, "unexpected"); Reject(root); File.Delete(extra);
            var nested = Path.Combine(directory, "nested"); Directory.CreateDirectory(nested); Reject(root); Directory.Delete(nested);
            var unknown = Path.Combine(root, "other"); Directory.CreateDirectory(unknown); Reject(root); Directory.Delete(unknown);
            File.Delete(license); Reject(root); File.Copy(original, license);
            AssemblyPolicyTests.ProductAssembliesAreIndependent(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void Reject(string root)
    {
        try { AssemblyPolicyTests.ProductAssembliesAreIndependent(root); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Extra/missing/tampered artifact license must reject");
    }
}

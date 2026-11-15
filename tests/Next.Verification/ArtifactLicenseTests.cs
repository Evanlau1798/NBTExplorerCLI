namespace Next.Verification;

internal static class ArtifactLicenseTests
{
    public static void ExactLicenseAndArtifactSet()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            string[] names = ["Next.Domain", "Next.Formats", "Next.Minecraft", "Next.Application", "Next.Infrastructure", "nbtcli-next"];
            foreach (var name in names)
                foreach (var extension in new[] { ".dll", ".pdb" })
                    File.Copy(Path.Combine(AppContext.BaseDirectory, name + extension), Path.Combine(root, name + extension));
            foreach (var name in new[] { "nbtcli-next.exe", "nbtcli-next.deps.json", "nbtcli-next.runtimeconfig.json" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(root, name));
            var directory = Path.Combine(root, "licenses"); Directory.CreateDirectory(directory);
            var original = Path.Combine(AppContext.BaseDirectory, "licenses", "Unicode.md");
            var license = Path.Combine(directory, "Unicode.md"); File.Copy(original, license);
            AssemblyPolicyTests.ProductAssembliesAreIndependent(root);
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

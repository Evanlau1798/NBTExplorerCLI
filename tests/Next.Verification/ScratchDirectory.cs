namespace Next.Verification;

internal static class ScratchDirectory
{
    public static string CreateChild()
    {
        var parent = Environment.GetEnvironmentVariable("NBTCLI_VERIFICATION_SCRATCH")
            ?? throw new InvalidOperationException("測試 scratch 必須由 controller 擁有");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

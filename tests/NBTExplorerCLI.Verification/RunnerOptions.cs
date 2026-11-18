namespace NBTExplorerCLI.Verification;

internal sealed record RunnerOptions(string Suite, string? Case, string FixtureRoot, string? ArtifactRoot, int TimeoutMs, bool Worker)
{
    public static RunnerOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var worker = false;
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (key == "--worker")
            {
                if (worker) throw new ArgumentException("重複 --worker");
                worker = true;
                continue;
            }
            if (key is not ("--suite" or "--case" or "--fixture-root" or "--artifact-root" or "--timeout-ms"))
                throw new ArgumentException($"未知選項：{key}");
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"選項缺值：{key}");
            if (!values.TryAdd(key, args[index])) throw new ArgumentException($"選項重複：{key}");
        }
        var timeout = 30_000;
        if (values.TryGetValue("--timeout-ms", out var text)
            && (!int.TryParse(text, out timeout) || timeout < 100 || timeout > 120_000))
            throw new ArgumentException("timeout-ms 必須介於 100 與 120000");
        var fixtureRoot = Path.GetFullPath(values.GetValueOrDefault("--fixture-root") ?? Path.Combine(AppContext.BaseDirectory, "Fixtures"));
        if (!Directory.Exists(fixtureRoot)) throw new DirectoryNotFoundException($"Fixture root 不存在：{fixtureRoot}");
        var artifactRoot = values.TryGetValue("--artifact-root", out var artifact) ? Path.GetFullPath(artifact) : null;
        return new(values.GetValueOrDefault("--suite") ?? "all", values.GetValueOrDefault("--case"), fixtureRoot,
            artifactRoot, timeout, worker);
    }
}

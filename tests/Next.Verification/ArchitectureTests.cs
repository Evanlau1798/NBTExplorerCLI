using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Next.Verification;

internal static class ArchitectureTests
{
    public static void BuildRejectsExternalSource()
    {
        var (exit, text) = Probe("<Compile Include=\"../../outside.cs\" />");
        Require(exit != 0 && text.Contains("NBTARCH002", StringComparison.Ordinal),
            $"跨專案來源必須由架構門檻拒絕，exit={exit}: {text}");
    }

    public static void BuildRejectsOldProject()
    {
        var (exit, text) = Probe("<ProjectReference Include=\"../../.old/Old.csproj\" />");
        Require(exit != 0 && text.Contains("NBTARCH001", StringComparison.Ordinal),
            $"舊專案引用必須由架構門檻拒絕，exit={exit}: {text}");
    }

    public static void BuildAcceptsContainedSource()
    {
        var (exit, text) = Probe("");
        Require(exit == 0, $"合法新專案必須通過，exit={exit}: {text}");
    }

    public static void BuildRejectsAssemblyHintPath()
    {
        var (exit, text) = Probe("<Reference Include=\"Old\"><HintPath>../../.old/nbtcli.dll</HintPath></Reference>");
        Require(exit != 0 && text.Contains("NBTARCH003", StringComparison.Ordinal), "手動 DLL 路徑必須被拒絕");
    }

    public static void BuildRejectsDomainPackage()
    {
        var (exit, text) = Probe("<PackageReference Include=\"MemoryPack\" Version=\"1.21.3\" />");
        Require(exit != 0 && text.Contains("NBTARCH004", StringComparison.Ordinal), "Domain 必須拒絕外部套件");
    }

    public static void BuildRejectsLateSource()
    {
        var (exit, text) = Probe("", """
            <Target Name="AddLateSource" BeforeTargets="CoreCompile">
              <ItemGroup><Compile Include="../../outside.cs" /></ItemGroup>
            </Target>
            """, fullBuild: true);
        Require(exit != 0 && text.Contains("NBTARCH002", StringComparison.Ordinal),
            $"晚加入的跨根來源也必須被拒絕，exit={exit}: {text}");
    }

    public static void BuildRejectsDirectAssembly()
    {
        var assembly = System.Security.SecurityElement.Escape(Path.Combine(AppContext.BaseDirectory, "nbtcli-next.dll"));
        var (exit, text) = Probe($"<Reference Include=\"{assembly}\" />");
        Require(exit != 0 && text.Contains("NBTARCH003", StringComparison.Ordinal), "無 HintPath 的直接 DLL 引用必須被拒絕");
    }

    private static (int Exit, string Output) Probe(string items, string extraTargets = "", bool fullBuild = false)
    {
        var root = ScratchDirectory.CreateChild();
        Directory.CreateDirectory(Path.Combine(root, "src", "Next.Domain"));
        try
        {
            var target = Path.Combine(AppContext.BaseDirectory, "ArchitectureFixture", "Directory.Build.targets");
            if (File.Exists(target)) File.Copy(target, Path.Combine(root, "Directory.Build.targets"));
            File.WriteAllText(Path.Combine(root, "outside.cs"), "public class Outside {}");
            var project = Path.Combine(root, "src", "Next.Domain", "Next.Domain.csproj");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>{items}</ItemGroup>
                  {extraTargets}
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>");
            var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "msbuild", project, fullBuild ? "-t:Build" : "-t:PrepareForBuild", "-v:quiet", "-nr:false", "-p:UseSharedCompilation=false" })
                start.ArgumentList.Add(argument);
            if (fullBuild) start.ArgumentList.Add("-restore");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("無法啟動架構探針");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("架構探針超過 30 秒");
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

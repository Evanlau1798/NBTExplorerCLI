using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using System.Diagnostics;
using System.Text;
using static NBTExplorerCLI.Verification.CandidateIoTests;
using static NBTExplorerCLI.Verification.TextSlotTests;

namespace NBTExplorerCLI.Verification;

internal static class CandidatePathTests
{
    public static void RejectsRemoteVolumePolicy()
    {
        Require(GuardedArtifactPath.SupportsVolume(DriveType.Fixed, "NTFS"), "local NTFS remains supported");
        foreach (var type in new[] { DriveType.Network, DriveType.Unknown, DriveType.NoRootDirectory, DriveType.CDRom })
            Require(!GuardedArtifactPath.SupportsVolume(type, "NTFS"), $"{type} cannot enter local-only NTFS path");
        Require(!GuardedArtifactPath.SupportsVolume(DriveType.Fixed, "exFAT"), "unvalidated local filesystem rejects");
    }
    public static void ActualJunctionAndPathBoundaries()
    {
        var root = ScratchDirectory.CreateChild(); var outside = ScratchDirectory.CreateChild();
        var link = Path.Combine(root, "link");
        try
        {
            File.WriteAllBytes(Path.Combine(outside, "level.dat"), [1, 2, 3]);
            CreateJunction(link, outside);
            Require((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "fixture is a real junction");
            var store = new ArtifactStorage(root, World);
            var artifact = new ArtifactAddress(World, "link/level.dat", ArtifactKind.Nbt);
            Reject<IOException>(() => store.Read(artifact));
            Reject<IOException>(() => store.Stage(artifact, ArtifactImage.Present(Hash256.Compute(new byte[] { 1, 2, 3 })), new byte[] { 4 }));
            var aliasedRoot = new ArtifactStorage(link, World);
            Reject<IOException>(() => aliasedRoot.Read(new(World, "level.dat", ArtifactKind.Nbt)));
            Require(File.ReadAllBytes(Path.Combine(outside, "level.dat")).SequenceEqual(new byte[] { 1, 2, 3 }), "actual outside target remains untouched");
            Reject<ArgumentException>(() => store.Stage(new(World, "CON.dat", ArtifactKind.Nbt), ArtifactImage.Absent, new byte[] { 4 }));
            Reject<ArgumentException>(() => store.Stage(new(Guid.NewGuid(), "level.dat", ArtifactKind.Nbt), ArtifactImage.Absent, new byte[] { 4 }));
            Reject<ArgumentException>(() => store.Stage(new(World, "pack.zip", ArtifactKind.Archive, "../escape"), ArtifactImage.Absent, new byte[] { 4 }));
            Reject<ArgumentException>(() => store.Stage(new(World, ".nbtcli-writer.lock", ArtifactKind.Nbt), ArtifactImage.Absent, new byte[] { 4 }));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link); // Delete the junction itself, never traverse it.
            Directory.Delete(root, recursive: true); Directory.Delete(outside, recursive: true);
        }
    }
    internal static void CreateJunction(string link, string outside)
    {
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''", StringComparison.Ordinal)
            + "' -Target '" + outside.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000) || process.ExitCode != 0) throw new InvalidOperationException($"Junction fixture unavailable: {output} {error}");
        }
    }
}

using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Next.Domain;
using Next.Formats;
using static Next.Verification.CliScanTests;
using static Next.Verification.TextSlotTests;

namespace Next.Verification;

internal static class CliCompatibilityTests
{
    public static void LegacyRootAliasUsesTheCanonicalSessionContract()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            foreach (var args in new[] { new[] { "translate-session", "--help" }, new[] { "help", "translate-session" }, new[] { "translate-session", "start", "--help" } })
            {
                using var help = Success(Run(work, args)); var data = help.RootElement.GetProperty("data");
                Require(data.GetProperty("subject").GetString() == "translate" && data.GetProperty("subcommands").EnumerateArray().Any(value => value.GetString() == "start"), "root alias exposes the canonical registered session interface");
            }
            var id = Guid.NewGuid().ToString(); var operation = Guid.NewGuid().ToString();
            using var start = Success(Run(work, "translate-session", "start", root, "--session-id", id, "--operation-id", operation, "--source-language", "en_us", "--target-language", "zh_tw", "--state-root", work));
            using var status = Success(Run(work, "translate", "status", root, "--session-id", id, "--state-root", work));
            Require(start.RootElement.GetProperty("command").GetString() == "translate" && status.RootElement.GetProperty("command").GetString() == "translate", "alias and canonical entry share one persisted session");
            var failure = Run(work, "translate-session", "status", root, "--unknown"); using var error = JsonDocument.Parse(failure.Output);
            Require(failure.Exit == 2 && error.RootElement.GetProperty("command").GetString() == "translate", "alias errors retain canonical machine command");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    public static void NamedBackupsPublishVerifiedManifestsAndRestoreRawBytes()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = File.ReadAllBytes(Path.Combine(root, "level.dat")); var exported = Path.Combine(work, "exported.json");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? hash = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var backup = Success(Run(work, ["backup", root, "--path", "level.dat", "--suffix", "人工 備份", "--state-root", work, .. attempt == 0 ? new[] { "--output", exported } : Array.Empty<string>()]));
                var data = backup.RootElement.GetProperty("data"); var path = data.GetProperty("backupPath").GetString()!;
                var manifest = BackupManifestJson.Read(File.ReadAllText(path)); var manifestHash = data.GetProperty("manifestHash").GetString()!;
                Require(data.GetProperty("namedManifestPublished").GetBoolean() && Path.GetDirectoryName(path) == Path.GetFullPath(work) && Path.GetFileName(path).StartsWith("backup_人工 備份_", StringComparison.Ordinal) && paths.Add(path), "suffix confirms a unique named manifest outside the world");
                Require(Hash256.Compute(File.ReadAllBytes(path)).Hex == manifestHash && manifest.Artifacts.Single().Image.Hash == Hash256.Compute(before), "named reference contains the exact verified raw backup manifest");
                Require(hash is null || hash == manifestHash, "naming does not change immutable backup identity"); hash = manifestHash;
                if (attempt == 0) Require(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(path)), "explicit output and named reference export the same raw manifest");
            }
            File.WriteAllBytes(Path.Combine(root, "level.dat"), [0]);
            using var restored = Success(Run(work, "restore", root, "--manifest-hash", hash!, "--state-root", work, "--apply", "--world-offline"));
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(before), "the named manifest identifies shared verified blobs usable for raw restoration");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    public static void AFailedNamedPublicationKeepsTheCompletedVerifiedBackup()
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("NTFS access-control witness requires Windows");
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        var directory = Directory.CreateDirectory(Path.Combine(work, "state")); var originalAcl = directory.GetAccessControl();
        try
        {
            var original = File.ReadAllBytes(Path.Combine(root, "level.dat"));
            var child = Directory.CreateDirectory(Path.Combine(directory.FullName, "control")).FullName;
            var restricted = directory.GetAccessControl();
            restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            string hash;
            try
            {
                directory.SetAccessControl(restricted);
                var denied = false;
                try { File.WriteAllText(Path.Combine(directory.FullName, "denied-control.json"), "denied"); }
                catch (UnauthorizedAccessException) { denied = true; }
                Require(denied, "negative control denies new files in the state root");
                File.WriteAllText(Path.Combine(child, "allowed.json"), "allowed");
                var result = Run(work, "backup", root, "--path", "level.dat", "--suffix", "blocked", "--state-root", directory.FullName);
                using var output = JsonDocument.Parse(result.Output); var data = output.RootElement.GetProperty("data");
                Require(result.Exit == 5 && output.RootElement.GetProperty("error").GetProperty("code").GetString() == "EXPORT_FAILED"
                    && !data.GetProperty("namedManifestPublished").GetBoolean(), "failure occurs after backup completion and cannot claim named publication: " + result.Output);
                hash = data.GetProperty("manifestHash").GetString()!;
                var manifest = BackupManifestJson.Read(data.GetProperty("manifest").GetRawText());
                Require(data.GetProperty("stateRoot").GetString() == directory.FullName && !File.Exists(data.GetProperty("backupPath").GetString())
                    && Hash256.Compute(File.ReadAllBytes(Path.Combine(directory.FullName, "objects", "manifest-" + hash + ".json"))).Hex == hash
                    && manifest.Artifacts.Single().Image.Hash == Hash256.Compute(original), "failed named export retains the completed manifest, raw preimage and exact recovery identity");
                Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(original), "publication failure cannot change original world bytes");
            }
            finally { directory.SetAccessControl(originalAcl); }
            File.WriteAllBytes(Path.Combine(root, "level.dat"), [0]);
            using var restored = Success(Run(work, "restore", root, "--manifest-hash", hash, "--state-root", directory.FullName, "--apply", "--world-offline"));
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(original), "the identity from an export failure restores the verified backup through the public CLI");
        }
        finally { directory.SetAccessControl(originalAcl); Directory.Delete(root, true); Directory.Delete(work, true); }
    }

    public static void InvalidBackupNamesAndExistingExportsCannotPublishState()
    {
        var root = WorldQueryTests.CreateWorld(); var work = ScratchDirectory.CreateChild();
        try
        {
            var before = File.ReadAllBytes(Path.Combine(root, "level.dat"));
            foreach (var suffix in new[] { "../escape", "a/b", "a\\b", "bad:stream", "end.", "end ", " ", new string('a', 65) })
            {
                var state = Path.Combine(work, Guid.NewGuid().ToString("N"));
                var result = Run(work, "backup", root, "--path", "level.dat", "--suffix", suffix, "--state-root", state);
                Require(result.Exit == 2 && !Directory.Exists(state), "invalid suffix fails before any repository publication: " + result.Output);
            }
            var output = Path.Combine(work, "existing.json"); File.WriteAllText(output, "keep"); var blockedState = Path.Combine(work, "state");
            Require(Run(work, "backup", root, "--path", "level.dat", "--suffix", "safe", "--state-root", blockedState, "--output", output).Exit == 3
                && !Directory.Exists(blockedState) && File.ReadAllText(output) == "keep", "existing output is rejected before the verified backup or name is published");
            Require(File.ReadAllBytes(Path.Combine(root, "level.dat")).SequenceEqual(before), "all backup name failures preserve original bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(work, true); }
    }
}

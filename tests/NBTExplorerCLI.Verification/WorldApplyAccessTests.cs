using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Infrastructure;
using System.Diagnostics;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.CandidateIoTests;

namespace NBTExplorerCLI.Verification;

internal static class WorldApplyAccessTests
{
    public static void CaseSensitiveRootsCannotShareWriteAuthority()
    {
        foreach (var mode in new[] { "prepared", "preparer", "engine", "journal" })
        {
            var parent = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "fsutil.exe"))
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "file", "setCaseSensitiveInfo", parent, "enable" }) start.ArgumentList.Add(argument);
                using (var process = Process.Start(start)!)
                {
                    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new InvalidOperationException("Case-sensitive fixture timed out"); }
                    if (process.ExitCode != 0) throw new InvalidOperationException("Case-sensitive fixture unavailable: " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
                }
                var first = Path.Combine(parent, "World"); var second = Path.Combine(parent, "world");
                Directory.CreateDirectory(first); Directory.CreateDirectory(second);
                File.WriteAllText(Path.Combine(first, "only-first"), "control");
                Require(!File.Exists(Path.Combine(second, "only-first")), "case-only paths identify physically separate NTFS roots");
                var artifact = new ArtifactAddress(World, "a.bin", ArtifactKind.Text);
                File.WriteAllBytes(Path.Combine(first, "a.bin"), [2]);
                var inventoryA = new WorldInventory(first, World); var inventoryB = new WorldInventory(second, World);
                var storageA = new ArtifactStorage(first, World); var storageB = new ArtifactStorage(second, World);
                var backup = new WorldBackupService(inventoryA, new FileSystemWorldBackupStore(Path.Combine(state, "desired")))
                    .Create([new(artifact, ArtifactImage.Present(Hash256.Compute(new byte[] { 2 })))]);
                File.WriteAllBytes(Path.Combine(first, "a.bin"), [1]); File.WriteAllBytes(Path.Combine(second, "a.bin"), [1]);
                var plan = new WorldPlanBuilder(inventoryA).Build(backup, [new("restore", new(backup.ManifestHash, backup.Manifest.Artifacts,
                    new Dictionary<ArtifactAddress, ArtifactImage> { [artifact] = ArtifactImage.Present(Hash256.Compute(new byte[] { 1 })) }))]);
                if (mode == "preparer") Reject<ArgumentException>(() => { using var invalid = new WorldPlanPreparation(inventoryB, storageA).Prepare(backup, plan, true); });
                else if (mode == "engine") Reject<ArgumentException>(() => new WorldApplyEngine(inventoryB, storageA,
                    new FileSystemWorldBackupStore(Path.Combine(state, "objects")), new FileSystemApplyJournalStore(Path.Combine(state, "journals"))));
                else
                {
                    using var prepared = new WorldPlanPreparation(inventoryA, storageA).Prepare(backup, plan, true);
                    if (mode == "journal")
                    {
                        Require(WorldApplyTests.Engine(first, state, storageA).Apply(prepared, true).Success, "same-root apply is a valid positive control");
                        Reject<InvalidDataException>(() => WorldApplyTests.Engine(second, state, storageB).ReadReceipt(plan.PlanHash));
                    }
                    else
                    {
                        var rejected = false;
                        try { _ = WorldApplyTests.Engine(second, state, storageB).Apply(prepared, true); } catch (ArgumentException) { rejected = true; }
                        var actualA = File.ReadAllBytes(Path.Combine(first, "a.bin")).Single(); var actualB = File.ReadAllBytes(Path.Combine(second, "a.bin")).Single();
                        Require(rejected && actualA == 1 && actualB == 1,
                            $"case-sensitive root mismatch rejects before writing; rejected={rejected}, equalBindings={storageA.AccessBinding == storageB.AccessBinding}, A={actualA}, B={actualB}");
                    }
                }
                Require(storageA.AccessBinding != storageB.AccessBinding && File.ReadAllBytes(Path.Combine(second, "a.bin")).Single() == 1,
                    "root authority distinguishes native directories even when their spelling folds identically");
            }
            finally { Directory.Delete(parent, true); Directory.Delete(state, true); }
        }
    }
    public static void RecreatedRootsRequireNewAuthority()
    {
        var parent = ScratchDirectory.CreateChild();
        try
        {
            var root = Path.Combine(parent, "world"); Directory.CreateDirectory(root);
            var storage = new ArtifactStorage(root, World); var before = storage.AccessBinding;
            Directory.Move(root, Path.Combine(parent, "previous")); Directory.CreateDirectory(root);
            Require(storage.AccessBinding != before && storage.AccessBinding == new WorldInventory(root, World).AccessBinding,
                "a previously constructed port rebinds its current physical root, never reusing the replaced directory's write authority");
        }
        finally { Directory.Delete(parent, true); }
    }
    public static void IdenticalWorldCopiesCannotRedirectPreparationApplyOrReplay()
    {
        foreach (var mode in new[] { "prepared", "preparer", "engine", "journal" })
        {
            var first = WorldQueryTests.CreateWorld(); var second = ScratchDirectory.CreateChild(); var state = ScratchDirectory.CreateChild();
            try
            {
                foreach (var path in Directory.GetFiles(first, "*", SearchOption.AllDirectories))
                    WorldDiscoveryTests.Put(second, Path.GetRelativePath(first, path).Replace('\\', '/'), File.ReadAllBytes(path));
                var files = Directory.GetFiles(first, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(first, path)).ToArray();
                var beforeA = files.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(first, path)));
                var beforeB = files.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(second, path)));
                var storage = new ArtifactStorage(first, World);
                Require(storage.AccessBinding == new WorldInventory(first, World).AccessBinding
                    && storage.AccessBinding == new ArtifactStorage(Path.Combine(first, "."), World).AccessBinding
                    && storage.AccessBinding == new ArtifactStorage(first.ToUpperInvariant(), World).AccessBinding,
                    "native inventory/storage agree for one normalized NTFS root, including case and dot aliases");
                if (mode == "preparer") Reject<ArgumentException>(() => { using var invalid = WorldApplyTests.Functions(second, state, storage); });
                else if (mode == "engine") Reject<ArgumentException>(() => new WorldApplyEngine(new WorldInventory(second, World), storage,
                    new FileSystemWorldBackupStore(Path.Combine(state, "objects")), new FileSystemApplyJournalStore(Path.Combine(state, "journals"))));
                else
                {
                    using var prepared = WorldApplyTests.Functions(first, state, storage);
                    var inventoryB = new WorldInventory(second, World);
                    Require(prepared.Plan.Preconditions.All(input => ArtifactImage.Present(Hash256.Compute(inventoryB.Read(input.Artifact))) == input.Image), "distinct roots have identical GUIDs, sources and all preimage hashes, so content checks cannot distinguish their write authority");
                    if (mode == "prepared")
                    {
                        var rejected = false; try { _ = WorldApplyTests.Engine(second, state).Apply(prepared, true); } catch (ArgumentException) { rejected = true; }
                        var changedA = beforeA.Count(pair => !File.ReadAllBytes(Path.Combine(first, pair.Key)).SequenceEqual(pair.Value));
                        var changedB = beforeB.Count(pair => !File.ReadAllBytes(Path.Combine(second, pair.Key)).SequenceEqual(pair.Value));
                        Require(rejected && changedA == 0 && changedB == 0, $"prepared access must match the engine root before any write; rejected={rejected}, changedA={changedA}, changedB={changedB}");
                        var remaining = Directory.GetFiles(first, "*.candidate", SearchOption.AllDirectories).Length;
                        var input = prepared.Plan.Preconditions.Single(item => item.Artifact == prepared.Plan.Outputs[0].Artifact);
                        var otherWriter = new ArtifactStorage(first, World); var bytes = otherWriter.Read(input.Artifact); var released = false;
                        try { using var probe = otherWriter.Stage(input.Artifact, input.Image, bytes); released = true; } catch (IOException) { }
                        Require(remaining == 0 && released, $"rejected Apply consumes candidates and releases its writer without caller Dispose; candidates={remaining}, writerReleased={released}");
                    }
                    else
                    {
                        Require(WorldApplyTests.Engine(first, state, storage).Apply(prepared, true).Success, "journal control records a real successful apply to the first root");
                        Reject<InvalidDataException>(() => WorldApplyTests.Engine(second, state).ReadReceipt(prepared.Plan.PlanHash));
                    }
                }
                Require(beforeB.All(pair => File.ReadAllBytes(Path.Combine(second, pair.Key)).SequenceEqual(pair.Value))
                    && (mode == "journal" || beforeA.All(pair => File.ReadAllBytes(Path.Combine(first, pair.Key)).SequenceEqual(pair.Value))), "root binding errors never mutate either physical world or report another root's receipt as their own");
            }
            finally { Directory.Delete(first, true); Directory.Delete(second, true); Directory.Delete(state, true); }
        }
    }
}

using System.Buffers.Binary;
using Next.Application;
using Next.Domain;
using Next.Formats;
using Next.Infrastructure;
using static Next.Verification.TextSlotTests;
using static Next.Verification.WorldDiscoveryTests;

namespace Next.Verification;

internal static class RegionBatchPreparationTests
{
    public static void ManyChunkEditsPreserveExactBytesWithinAllocationBudget()
    {
        const int chunks = 512, selected = 64, sectorBytes = 4096;
        const string relative = "region/r.0.0.mca";
        var root = WorldQueryTests.CreateWorld();
        var cache = ScratchDirectory.CreateChild();
        try
        {
            byte[] original = new byte[(2 + chunks) * sectorBytes];
            for (var index = 0; index < chunks; index++)
                WriteChunk(original, index, index + 2, 200000 + index);
            Put(root, relative, original);

            var inventory = new WorldInventory(root, World);
            var snapshot = LegacyImportMappingTests.Query(root, cache);
            var artifact = new ArtifactAddress(World, relative, ArtifactKind.Region);
            var path = new TypedPath([PathSegment.ForKey("Counter")]);
            var operations = Enumerable.Range(0, selected).Select(index => new TypedPatch(
                $"edit-{index:D3}", new SourceAddress(artifact, path, "typed.value", "minecraft:overworld", new(index % 32, index / 32)),
                TypedEdit.Replace(path, new NbtInt(200000 + index), new NbtInt(index == 0 ? 200000 : 1200000 + index)))).ToArray();
            var plan = new WorldPlanBuilder(inventory).Build(snapshot, operations);
            var preparation = new WorldPlanPreparation(inventory, new ArtifactStorage(root, World));

            var before = GC.GetAllocatedBytesForCurrentThread();
            using var prepared = preparation.Prepare(snapshot, plan, worldOffline: true);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var candidate = File.ReadAllBytes(prepared.Manifest.Artifacts.Single().CandidatePath!);
            byte[] expected = new byte[original.Length + (selected - 1) * sectorBytes];
            original.CopyTo(expected, 0);
            for (var index = 1; index < selected; index++)
                WriteChunk(expected, index, 1 + chunks + index, 1200000 + index);
            Require(candidate.AsSpan().SequenceEqual(expected), "batch preserves original sectors, no-op bytes, codec, timestamps and selected append order");
            Require(original.AsSpan().SequenceEqual(inventory.Read(artifact)), "preparation does not change the live region");
            Require(allocated <= 120L * 1024 * 1024, $"512-chunk staging with 64 selected edits must allocate at most 120 MiB, observed {allocated} bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cache, true); }
    }

    private static void WriteChunk(byte[] region, int index, int sector, int counter)
    {
        BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(index * 4), (uint)((sector << 8) | 1));
        BinaryPrimitives.WriteUInt32BigEndian(region.AsSpan(4096 + index * 4), (uint)(1000000 + index));
        var document = new NbtDocument("", new NbtCompound([new("DataVersion", new NbtInt(3953)), new("Counter", new NbtInt(counter))]));
        var payload = CompressedNbt.WriteDocument(document, NbtCompression.Raw);
        var target = region.AsSpan(sector * 4096);
        BinaryPrimitives.WriteUInt32BigEndian(target, (uint)(payload.Length + 1));
        target[4] = 3;
        payload.CopyTo(target[5..]);
    }
}

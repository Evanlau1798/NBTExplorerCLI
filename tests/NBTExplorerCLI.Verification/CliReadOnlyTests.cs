using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Cli;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Verification.TextSlotTests;
using static NBTExplorerCLI.Verification.WorldDiscoveryTests;

namespace NBTExplorerCLI.Verification;

internal static class CliReadOnlyTests
{
    public static void WrappedPackMetadataLimitsRemainResourceFailures()
    {
        var failures = new List<string>();
        foreach (var zipped in new[] { false, true })
        {
            var root = ScratchDirectory.CreateChild();
            try
            {
                Level(root, false);
                void Metadata(string value) => Put(root, zipped ? "datapacks/p.zip" : "datapacks/p/pack.mcmeta", zipped ? Zip(("pack.mcmeta", value)) : Encoding.UTF8.GetBytes(value));
                Metadata("{\"pack\":{\"pack_format\":48,\"description\":\"Data\"},\"extra\":" + new string('[', 257) + "0" + new string(']', 257) + "}");
                var result = Run(root, "list", root); using var limited = JsonDocument.Parse(result.Output);
                if (result.Exit != 6 || limited.RootElement.GetProperty("error").GetProperty("code").GetString() != "RESOURCE_LIMIT") failures.Add((zipped ? "ZIP" : "directory") + ": " + result.Output);
                Metadata("{"); var malformed = Run(root, "list", root); using var invalid = JsonDocument.Parse(malformed.Output);
                Require(malformed.Exit == 4 && invalid.RootElement.GetProperty("error").GetProperty("code").GetString() == "INCOMPLETE_SOURCE", "ordinary pack metadata failures do not become resource limits");
            }
            finally { Directory.Delete(root, true); }
        }
        Require(failures.Count == 0, "wrapped pack metadata decoder budgets retain ResourceLimit: " + string.Join(" | ", failures));
    }
    public static void PackMetadataUsesNativeJsonInEveryQueryLocation()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); const string relative = "datapacks/p/pack.mcmeta";
            Put(root, relative, Encoding.UTF8.GetBytes(Format48Pack));
            var archive = Path.Combine(root, "metadata.zip"); File.WriteAllBytes(archive, Zip(("nested/pack.mcmeta", Format48Pack)));
            var failures = new List<string>();
            foreach (var args in new[] { new[] { "get", Path.Combine(root, relative), "--pointer", "/pack/pack_format" },
                new[] { "get", root, "--path", relative, "--pointer", "/pack/pack_format" },
                new[] { "get", archive, "--entry", "nested/pack.mcmeta", "--pointer", "/pack/pack_format" } })
            {
                var result = Run(root, args); using var json = JsonDocument.Parse(result.Output);
                if (result.Exit != 0 || json.RootElement.GetProperty("data").GetProperty("value").GetInt32() != 48) failures.Add(string.Join(' ', args) + ":" + result.Output);
            }
            Require(failures.Count == 0, "physical, world-relative and nested archive pack metadata must use native JSON: " + string.Join(" | ", failures));
        }
        finally { Directory.Delete(root, true); }
    }
    public static void WorldCatalogResourceLimitsKeepTheirExitClassification()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            Level(root, false); var large = Path.Combine(root, "data", "oversized.dat"); Directory.CreateDirectory(Path.GetDirectoryName(large)!);
            using (var stream = File.Create(large)) stream.SetLength(67_108_865);
            var result = Run(root, "list", root); using var json = JsonDocument.Parse(result.Output);
            Require(result.Exit == 6 && json.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT",
                "world catalog must retain the inventory's resource-limit diagnostic: " + result.Output);
            File.Delete(large); using (var stream = File.Create(Path.Combine(root, "level.dat"))) stream.SetLength(67_108_865);
            var metadata = Run(root, "list", root); using var limited = JsonDocument.Parse(metadata.Output);
            Require(metadata.Exit == 6 && limited.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT",
                "metadata discovery failure must retain its inventory size-limit classification: " + metadata.Output);
            File.WriteAllBytes(Path.Combine(root, "level.dat"), [0]); var corrupt = Run(root, "list", root); using var invalid = JsonDocument.Parse(corrupt.Output);
            Require(corrupt.Exit == 4 && invalid.RootElement.GetProperty("error").GetProperty("code").GetString() == "INCOMPLETE_SOURCE", "ordinary malformed metadata must remain distinct from a resource limit");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void InvalidGlobalLocalesKeepTheActualCommandIdentity()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            foreach (var args in new[] { new[] { "--ui-locale", "fr-FR", "get", "missing.dat" }, new[] { "--ui-locale=fr-FR", "list", "missing.dat" },
                new[] { "--ui-locale", "en-US", "--ui-locale", "zh-TW", "search", "missing.dat", "--query", "q" } })
            {
                var result = Run(root, args); using var json = JsonDocument.Parse(result.Output);
                var expected = args.Contains("get") ? "get" : args.Contains("list") ? "list" : "search";
                Require(result.Exit == 2 && json.RootElement.GetProperty("command").GetString() == expected
                    && json.RootElement.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENTS", "invalid or repeated global locale must preserve canonical command: " + result.Output);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed record Invocation(int Exit, string Output, string Error);
    private static Invocation Run(string cwd, params string[] args)
    {
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start product CLI");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Product CLI did not finish"); }
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
    private static JsonDocument Success(Invocation result)
    {
        Require(result.Exit == 0, $"real CLI must succeed: exit={result.Exit}, stdout={result.Output}, stderr={result.Error}");
        var json = JsonDocument.Parse(result.Output, new JsonDocumentOptions { MaxDepth = 1024 });
        Require(json.RootElement.GetProperty("success").GetBoolean() && json.RootElement.GetProperty("schemaFamily").GetString() == "nbtcli.cli", "stdout is one versioned successful CLI envelope");
        return json;
    }
    private static NbtDocument Document() => new("根", new NbtCompound([
        new("", new NbtString("空 key")), new("a.b", new NbtInt(7)), new("a", new NbtCompound([new("b", new NbtInt(8))])),
        new("odd", new NbtString("隔離\ud800碼")), new("x/y~z", new NbtLong(long.MaxValue)),
        new("byte", new NbtByte(-1)), new("short", new NbtShort(-2)), new("float", new NbtFloat(0x7fc00001)), new("double", new NbtDouble(long.MinValue)),
        new("arr", new NbtList(NbtType.String, [new NbtString("One"), new NbtString("Two")])),
        new("bytes", new NbtByteArray([-128, 127])), new("ints", new NbtIntArray([int.MinValue, int.MaxValue])), new("longs", new NbtLongArray([long.MinValue, long.MaxValue]))]));
    private static string PutDocument(string root)
    {
        var path = Path.Combine(root, "空 白.dat"); File.WriteAllBytes(path, CompressedNbt.WriteDocument(Document(), NbtCompression.GZip)); return path;
    }
    public static void GetTypedAndDisplayPreserveAllPhysicalTypesAndStrings()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = PutDocument(root); var before = File.ReadAllBytes(path);
            using var typed = Success(Run(root, "get", path, "--types")); var data = typed.RootElement.GetProperty("data");
            Require(data.GetProperty("representation").GetString() == "typed-nbt" && TypedNbtJson.Read(data.GetProperty("value").GetRawText()) == Document(), "all physical types, float bits, empty keys and UTF-16 survive actual typed CLI output");
            var source = data.GetProperty("source"); var address = SourceAddressJson.Read(source.GetProperty("address").GetRawText());
            Require(address.NodePath.Equals(TypedPath.Root) && address.Artifact.RelativePath == Path.GetFileName(path) && source.GetProperty("rawHash").GetString() == Hash256.Compute(before).Hex, "query address and raw hash bind the exact selected artifact");
            using var display = Success(Run(root, "get", path, "--pointer", "/odd"));
            Require(display.RootElement.GetProperty("data").GetProperty("representation").GetString() == "display-json"
                && display.RootElement.GetProperty("data").GetProperty("value").GetProperty("utf16be").GetString() == string.Concat("隔離\ud800碼".Select(unit => ((ushort)unit).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))), "display is labelled separately and uses the existing lossless UTF16 string representation");
            Require(File.ReadAllBytes(path).SequenceEqual(before) && Directory.GetFiles(root).Length == 1, "queries create no world files or modified bytes");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ExactPathsDistinguishLiteralAndNestedKeys()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = PutDocument(root);
            var invalid = Run(root, "get", path, "--path", "a.b"); using var failure = JsonDocument.Parse(invalid.Output);
            Require(invalid.Exit == 2 && failure.RootElement.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENTS", "standalone node selection requires a pointer or typed segments");
            foreach (var (pointer, expected) in new[] { ("/a.b", 7), ("/a/b", 8) })
            {
                using var result = Success(Run(root, "get", path, "--pointer", pointer)); Require(result.RootElement.GetProperty("data").GetProperty("value").GetInt32() == expected, "explicit pointer selects only its exact key structure");
            }
            using var empty = Success(Run(root, "get", path, "--segments", "[{\"kind\":\"key\",\"key\":\"\"}]"));
            Require(empty.RootElement.GetProperty("data").GetProperty("value").GetString() == "空 key", "typed segments address an empty key");
            using var indexed = Success(Run(root, "get", path, "--pointer", "/arr/1"));
            Require(indexed.RootElement.GetProperty("data").GetProperty("value").GetString() == "Two", "array pointer selects the exact typed index");
            using var escaped = Success(Run(root, "get", path, "--pointer", "/x~1y~0z", "--types"));
            Require(TypedNbtJson.Read(escaped.RootElement.GetProperty("data").GetProperty("value").GetRawText()).Root == new NbtLong(long.MaxValue), "pointer escaping retains slash and tilde keys");
            foreach (var args in new[] { new[] { "--pointer", "/missing" }, new[] { "--pointer", "/arr/99" }, new[] { "--pointer", "/a~2b" }, new[] { "--pointer", "/arr/-1" } })
                Require(Run(root, ["get", path, .. args]).Exit != 0, "missing and invalid addresses never select another node");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void ListAndSearchUseTypedNodesAndRealPagination()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = PutDocument(root);
            using var listed = Success(Run(root, "list", path, "--limit", "2", "--offset", "1")); var data = listed.RootElement.GetProperty("data");
            Require(listed.RootElement.GetProperty("command").GetString() == "list" && data.GetProperty("items").GetArrayLength() == 2
                && data.GetProperty("pagination").GetProperty("total").GetInt32() == ((NbtCompound)Document().Root).Entries.Length, "child listing reports its canonical command with total before pagination");
            using var found = Success(Run(root, "search", path, "--name", "b", "--type", "Int"));
            Require(found.RootElement.GetProperty("data").GetProperty("items").GetArrayLength() == 2, "name/type search traverses both physical Int leaves");
            using var array = Success(Run(root, "search", path, "--query", long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "--type", "Long"));
            Require(array.RootElement.GetProperty("data").GetProperty("items").GetArrayLength() == 1, "typed-array elements participate with exact numeric formatting");
            using var absent = Success(Run(root, "search", path, "--query", "definitely absent"));
            Require(absent.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("total").GetInt32() == 0, "a real zero-result search is successful and distinct from failed input");
            Require(Run(root, "search", path, "--type", "not-a-type").Exit != 0 && Run(root, "search", path).Exit != 0, "unsupported filters and absent criteria are errors");
            using var scoped = Success(Run(root, "search", path, "--pointer", "/arr/1", "--name", "[1]", "--query", "Two"));
            Require(scoped.RootElement.GetProperty("data").GetProperty("items").GetArrayLength() == 1, "searching a selected array node keeps its own index name rather than borrowing the document name");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void JsonWorldQueriesRetainNativeValuesAndRejectTraversal()
    {
        var root = WorldQueryTests.CreateWorld(); var cwd = ScratchDirectory.CreateChild();
        try
        {
            const string relative = "datapacks/p/data/test/advancement/query.json";
            Put(root, relative, "{\"nested\":[0,{\"a.b\":\"字\",\"raw\":1e+02}],\"odd\":\"\\ud800\"}"u8.ToArray());
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            using var result = Success(Run(cwd, "get", root, "--path", relative, "--pointer", "/nested/1")); var data = result.RootElement.GetProperty("data");
            Require(data.GetProperty("representation").GetString() == "native-json" && data.GetProperty("value").GetProperty("a.b").GetString() == "字"
                && data.GetProperty("value").GetProperty("raw").GetRawText() == "1e+02", "world-relative JSON query preserves native object, array and number spelling");
            using var odd = Success(Run(cwd, "get", Path.Combine(root, relative), "--pointer", "/odd"));
            Require(NativeJsonStrings.Read(odd.RootElement.GetProperty("data").GetProperty("value")) == "\ud800", "native JSON strings are not lossy NBT conversions");
            Require(Run(cwd, "get", root, "--path", "../outside.json").Exit != 0, "relative world targets cannot escape the chosen root");
            Require(before.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == before.Count, "native JSON queries preserve the complete world file set");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cwd, true); }
    }
    public static void WorldAndRegionViewsUseActualLayoutAndChunks()
    {
        var root = WorldReferenceTests.CreateWorld(true); var cwd = ScratchDirectory.CreateChild();
        try
        {
            using var world = Success(Run(cwd, "list", root)); var data = world.RootElement.GetProperty("data");
            Require(data.GetProperty("dimensions").GetArrayLength() == WorldMetadataTests.Dimensions.Entries.Length
                && data.GetProperty("dimensions").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "minecraft:overworld").GetProperty("entities").GetArrayLength() == 1, "world listing uses declared dimensions and distinguishes entity regions");
            var region = Path.Combine(root, WorldReferenceTests.Entities);
            using var index = Success(Run(cwd, "list", region, "--limit", "1"));
            Require(index.RootElement.GetProperty("data").GetProperty("representation").GetString() == "region-index" && index.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("total").GetInt32() == 2, "physical region listing reports exactly two populated chunks");
            using var chunk = Success(Run(cwd, "get", region, "--chunk", "1,0", "--dimension", "minecraft:overworld", "--types"));
            Require(TypedNbtJson.Read(chunk.RootElement.GetProperty("data").GetProperty("value").GetRawText()).Root is NbtCompound, "an explicit chunk resolves and decodes its actual codec");
            Require(Run(cwd, "get", region).Exit != 0 && Run(cwd, "get", region, "--chunk", "32,0", "--dimension", "minecraft:overworld").Exit != 0, "missing or different-region coordinates cannot pick an arbitrary chunk");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cwd, true); }
    }
    public static void ArgumentErrorsLocalesAndHelpMatchThePublicEntryPoint()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = PutDocument(root);
            foreach (var args in new[] { new[] { "--unknown" }, new[] { "--limit" }, new[] { "--offset", "-1" }, new[] { "--limit", "0" }, new[] { "--pointer", "", "--path", "a" }, new[] { "--types=false" }, new[] { "--lang", "zh_tw" } })
            { var result = Run(root, ["list", path, .. args]); using var json = JsonDocument.Parse(result.Output); Require(result.Exit != 0 && !json.RootElement.GetProperty("success").GetBoolean(), "unknown, missing, duplicated, conflicting or out-of-range options fail as JSON"); }
            Require(Run(root, "search", path, "--query", "One", "--query", "Two").Exit != 0, "repeated options cannot silently override a search value");
            var plain = Path.Combine(root, "value.json"); File.WriteAllText(plain, "{\"ok\":true}", new UTF8Encoding(false));
            var region = Path.Combine(root, "r.0.0.mca"); File.WriteAllBytes(region, new byte[8192]);
            var jsonCodec = Run(root, "get", plain, "--compression", "gzip").Exit;
            var regionCodec = Run(root, "list", region, "--compression", "raw").Exit;
            var indexDimension = Run(root, "list", region, "--dimension", "minecraft:overworld").Exit;
            Require(jsonCodec == 2 && regionCodec == 2 && indexDimension == 2, $"inapplicable source options cannot be ignored; jsonCodec={jsonCodec}, regionCodec={regionCodec}, indexDimension={indexDimension}");
            var missing = Path.Combine(root, "missing.dat"); var zh = Run(root, "get", missing); var en = Run(root, "--ui-locale", "en-US", "get", missing);
            using var z = JsonDocument.Parse(zh.Output); using var e = JsonDocument.Parse(en.Output);
            Require(zh.Exit != 0 && en.Exit == zh.Exit && z.RootElement.GetProperty("error").GetProperty("code").GetString() == "FILE_NOT_FOUND"
                && e.RootElement.GetProperty("error").GetProperty("code").GetString() == "FILE_NOT_FOUND" && z.RootElement.GetProperty("error").GetProperty("message").GetString() != e.RootElement.GetProperty("error").GetProperty("message").GetString(), "UI locale changes messages without changing machine codes or exit classification");
            using var help = Success(Run(root, "help", "get"));
            Require(help.RootElement.GetProperty("data").GetProperty("options").EnumerateArray().Any(option => option.GetString() == "--segments"), "command help names the implemented exact-path option");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void GlobalOptionsDoNotReplaceTheFailingCommandIdentity()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = PutDocument(root); var failed = Run(root, "--ui-locale", "en-US", "get", path, "--unknown"); using var json = JsonDocument.Parse(failed.Output);
            Require(failed.Exit == 2 && json.RootElement.GetProperty("command").GetString() == "get" && json.RootElement.GetProperty("error").GetProperty("code").GetString() == "UNKNOWN_OPTION", "a global option prefix cannot become the command recorded for an argument failure");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void DeepTreesArchivesAndDuplicateKeysKeepExactBoundaries()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            NbtValue value = new NbtString("bottom"); for (var index = 0; index < 80; index++) value = new NbtCompound([new("child", value)]);
            var deep = new NbtDocument("deep", value); var path = Path.Combine(root, "deep.dat"); File.WriteAllBytes(path, BinaryNbt.WriteDocument(deep));
            using var result = Success(Run(root, "get", path, "--types")); Require(TypedNbtJson.Read(result.RootElement.GetProperty("data").GetProperty("value").GetRawText()) == deep, "the CLI keeps valid NBT depth beyond the BCL default JSON depth");
            var duplicate = new NbtDocument("", new NbtCompound([new("same", new NbtInt(1)), new("same", new NbtInt(2))])); File.WriteAllBytes(path, BinaryNbt.WriteDocument(duplicate));
            using var exact = Success(Run(root, "get", path, "--types")); Require(TypedNbtJson.Read(exact.RootElement.GetProperty("data").GetProperty("value").GetRawText()) == duplicate, "whole typed inspection retains duplicate physical keys without choosing one");
            Require(Run(root, "get", path, "--pointer", "/same").Exit == 3 && Run(root, "list", path).Exit == 3, "ambiguous keys cannot become unique query addresses");
            const string entry = "data/test/advancement/query.json"; var first = Path.Combine(root, "one.zip"); var second = Path.Combine(root, "two.zip");
            File.WriteAllBytes(first, Zip((entry, "{\"selected\":\"One\"}"))); File.WriteAllBytes(second, Zip((entry, "{\"selected\":\"Two\"}")));
            foreach (var (file, expected) in new[] { (first, "One"), (second, "Two") })
            {
                using var archived = Success(Run(root, "get", file, "--entry", entry, "--pointer", "/selected")); var data = archived.RootElement.GetProperty("data");
                Require(data.GetProperty("value").GetString() == expected && SourceAddressJson.Read(data.GetProperty("source").GetProperty("address").GetRawText()).Artifact.ContainerEntry == entry, "identical entry names in different archives retain their physical archive binding");
            }
            Require(Run(root, "get", first).Exit != 0 && Run(root, "get", first, "--entry", "../escape.json").Exit != 0, "archive selection is explicit and cannot traverse entries");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void SizeBoundsInvalidCoordinatesAndLegalNonScalarKeysAreClassified()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var huge = Path.Combine(root, "huge.dat"); using (var stream = File.Create(huge)) stream.SetLength(67_108_865);
            var bounded = Run(root, "get", huge); using var size = JsonDocument.Parse(bounded.Output);
            var sizeClassified = bounded.Exit == 6 && size.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT";
            var region = Path.Combine(root, "r.2147483647.0.mca"); File.WriteAllBytes(region, RegionFile.Read(new byte[8192]).WithChunk(0, new("", new NbtCompound([])), NbtCompression.Zlib, 0));
            var regionExit = Run(root, "list", region).Exit;
            var path = Path.Combine(root, "key.dat"); var document = new NbtDocument("", new NbtCompound([new("\ud800", new NbtString("value"))])); File.WriteAllBytes(path, BinaryNbt.WriteDocument(document));
            using var typed = Success(Run(root, "get", path, "--types")); Require(TypedNbtJson.Read(typed.RootElement.GetProperty("data").GetProperty("value").GetRawText()) == document, "legal non-scalar UTF16 keys remain inspectable through typed interchange");
            var display = Run(root, "get", path); using var unavailable = JsonDocument.Parse(display.Output);
            var keyClassified = display.Exit == 4 && unavailable.RootElement.GetProperty("error").GetProperty("code").GetString() == "UNSUPPORTED_SOURCE";
            var array = PutDocument(root); var pointerExit = Run(root, "get", array, "--pointer", "/arr/01").Exit;
            Require(sizeClassified && regionExit == 2 && keyClassified && pointerExit == 2,
                $"size is ResourceLimit, coordinate overflow is InvalidArguments, non-scalar keys require typed display and JSON Pointer rejects leading zero indices; size={bounded.Exit}, region={regionExit}, key={display.Exit}, pointer={pointerExit}");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void TypedOutputLimitsDoNotMislabelValidSourceBytes()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var document = new NbtDocument("valid", new NbtCompound(Enumerable.Range(0, 100_000).Select(index => new NbtEntry(index.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), new NbtInt(index)))));
            var path = Path.Combine(root, "many.dat"); var bytes = BinaryNbt.WriteDocument(document); File.WriteAllBytes(path, bytes);
            var result = Run(root, "get", path, "--types"); using var json = JsonDocument.Parse(result.Output);
            var error = json.RootElement.GetProperty("error"); var code = error.ValueKind == JsonValueKind.Object ? error.GetProperty("code").GetString() : "none";
            var details = error.ValueKind == JsonValueKind.Object ? error.GetProperty("details").GetString() : "success";
            Require(result.Exit == 6 && code == "RESOURCE_LIMIT" && File.ReadAllBytes(path).SequenceEqual(bytes),
                $"valid source exceeding typed output budget must be ResourceLimit; exit={result.Exit}, code={code}, detail={details}");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void WorldCatalogDoesNotClaimDecodedPayloadCoverage()
    {
        var root = WorldReferenceTests.CreateWorld(); var cwd = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, WorldReferenceTests.Entities); var bytes = File.ReadAllBytes(path); var region = RegionFile.Read(bytes);
            bytes[region.Chunks[0].SectorOffset * 4096 + 5] = 0; File.WriteAllBytes(path, bytes);
            using var catalog = Success(Run(cwd, "list", root)); var data = catalog.RootElement.GetProperty("data");
            Require(data.TryGetProperty("representation", out var representation) && representation.GetString() == "world-catalog"
                && data.TryGetProperty("catalogComplete", out var complete) && complete.GetBoolean() && !data.TryGetProperty("coverageComplete", out _),
                "world inventory explicitly claims catalog completeness, not decoded payload or reference coverage");
            var payload = Run(cwd, "get", path, "--chunk", "0,0", "--dimension", "minecraft:overworld", "--types"); using var failed = JsonDocument.Parse(payload.Output);
            Require(payload.Exit != 0 && failed.RootElement.GetProperty("error").GetProperty("code").GetString() == "MALFORMED_SOURCE", "a valid physical catalog does not hide corrupt selected chunk bytes");
        }
        finally { Directory.Delete(root, true); Directory.Delete(cwd, true); }
    }
    public static void VirtualArrayChildrenRespectTheAddressDepthBudget()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            NbtValue value = new NbtIntArray([1]); for (var depth = 0; depth < 256; depth++) value = new NbtCompound([new("child", value)]);
            var path = Path.Combine(root, "depth.dat"); File.WriteAllBytes(path, BinaryNbt.WriteDocument(new("depth", value)));
            using var valid = Success(Run(root, "get", path));
            var search = Run(root, "search", path, "--query", "absent"); using var result = JsonDocument.Parse(search.Output);
            var listed = Run(root, "list", path, "--pointer", string.Concat(Enumerable.Repeat("/child", 256))); using var list = JsonDocument.Parse(listed.Output);
            Require(search.Exit == 6 && listed.Exit == 6 && result.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT"
                && list.RootElement.GetProperty("error").GetProperty("code").GetString() == "RESOURCE_LIMIT",
                $"valid arrays whose virtual children exceed typed address depth need ResourceLimit in both queries; search={search.Exit}, list={listed.Exit}");
        }
        finally { Directory.Delete(root, true); }
    }
    public static void InvalidUtf16SegmentSourceNeverNormalizesIntoAnotherKey()
    {
        var root = ScratchDirectory.CreateChild();
        try
        {
            var path = Path.Combine(root, "keys.dat"); var document = new NbtDocument("", new NbtCompound([new("\ud800", new NbtString("exact")), new("\ufffd", new NbtString("other"))]));
            File.WriteAllBytes(path, BinaryNbt.WriteDocument(document));
            using var exact = Success(Run(root, "get", path, "--segments", "[{\"kind\":\"key\",\"key\":\"\\ud800\"}]", "--types"));
            Require(TypedNbtJson.Read(exact.RootElement.GetProperty("data").GetProperty("value").GetRawText()).Root == new NbtString("exact"), "valid escaped segment JSON preserves a literal unpaired key");
            using var output = new StringWriter(); var exit = CliApplication.Run(["get", path, "--segments", "[{\"kind\":\"key\",\"key\":\"\ud800\"}]", "--types"], output);
            using var invalid = JsonDocument.Parse(output.ToString());
            Require(exit == 2 && invalid.RootElement.GetProperty("error").GetProperty("code").GetString() == "INVALID_ARGUMENTS", $"invalid raw UTF16 segment JSON must be rejected instead of selecting the replacement-character key; exit={exit}");
        }
        finally { Directory.Delete(root, true); }
    }
}

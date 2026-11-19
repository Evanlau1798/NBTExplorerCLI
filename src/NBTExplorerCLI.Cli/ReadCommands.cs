using System.Text.Json;
using NBTExplorerCLI.Application;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Cli;

internal static class ReadCommands
{
    internal static object Execute(CliArguments request)
    {
        var input = Path.GetFullPath(request.Input!);
        if (!Directory.Exists(input) && !File.Exists(input)) throw new FileNotFoundException("The input path does not exist", input);
        if (request.Command == "list" && Directory.Exists(input) && !request.Flag("path")) return World(input, request);
        if (request.Get("type") is { } filter && !Enum.GetNames<NbtType>().Concat(Enum.GetNames<JsonValueKind>()).Any(type => string.Equals(type, filter, StringComparison.OrdinalIgnoreCase))) throw new CliFailure("INVALID_ARGUMENTS", "Unknown type filter");
        using var source = new QuerySource(request);
        if (source.Region is not null && source.Root is null) return RegionIndex(source, request);
        var selected = QueryPath.Select(source.Root!, request);
        if (request.Command == "get")
        {
            object? value; string representation;
            if (request.Flag("types") && selected.Node.Nbt is { } nbt)
            {
                try
                {
                    using var typed = JsonDocument.Parse(TypedNbtJson.Write(new(source.RootName, nbt)), new JsonDocumentOptions { MaxDepth = 1024 }); value = typed.RootElement.Clone(); representation = "typed-nbt";
                }
                catch (JsonException error) { throw new CliFailure("RESOURCE_LIMIT", "Typed interchange output limit: " + error.Message); }
            }
            else { value = selected.Node.Display(); representation = selected.Node.Nbt is null ? "native-json" : "display-json"; }
            return new { source = source.Description(selected.Path), representation, tagType = selected.Node.Type, value };
        }
        var last = selected.Path.Segments.LastOrDefault();
        var selectedName = last is null ? source.RootName : last.Kind == PathSegmentKind.Key ? last.Key! : "[" + last.Index!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
        var items = request.Command == "list" ? selected.Node.Children().Select(child => (child.Node, Path: QueryPath.Append(selected.Path, child.Segment), child.Name))
            : Walk(selected.Node, selected.Path, selectedName).Where(item => Match(item.Node, item.Name, request));
        var page = new List<object>(); var total = 0; var offset = request.Number("offset", 0, 0, 2_000_000); var limit = request.Number("limit", 100, 1, 10_000);
        foreach (var item in items)
        {
            if (total >= 2_000_000) throw new CliFailure("RESOURCE_LIMIT", "Query node count exceeds its budget");
            if (total >= offset && page.Count < limit)
            {
                var scalar = item.Node.Scalar;
                page.Add(new { name = item.Name, type = item.Node.Type, address = source.Address(item.Path), preview = scalar is null ? item.Node.Type : scalar[..Math.Min(256, scalar.Length)] });
            }
            total++;
        }
        return new { source = source.Description(selected.Path), representation = "node-list", items = page, pagination = new { offset, limit, total } };
    }
    private static bool Match(QueryNode node, string name, CliArguments request)
        => (request.Get("name") is not { } named || name.Contains(named, StringComparison.OrdinalIgnoreCase))
        && (request.Get("type") is not { } type || string.Equals(node.Type, type, StringComparison.OrdinalIgnoreCase))
        && (request.Get("query") is not { } query || node.Scalar?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    private static IEnumerable<(QueryNode Node, TypedPath Path, string Name)> Walk(QueryNode node, TypedPath path, string name)
    {
        yield return (node, path, name);
        foreach (var child in node.Children())
        {
            foreach (var result in Walk(child.Node, QueryPath.Append(path, child.Segment), child.Name)) yield return result;
        }
    }
    private static object RegionIndex(QuerySource source, CliArguments request)
    {
        var position = source.RegionPosition(); var offset = request.Number("offset", 0, 0, 2_000_000); var limit = request.Number("limit", 100, 1, 10_000);
        var chunks = source.Region!.Chunks.Where(chunk => chunk.Present).ToArray();
        var items = chunks.Skip(offset).Take(limit).Select(chunk => new { chunk = new { x = checked(position.X * 32 + chunk.Index % 32), z = checked(position.Z * 32 + chunk.Index / 32) }, chunk.Timestamp, chunk.CodecId, chunk.External, support = chunk.Capability.Read.ToString() }).ToArray();
        return new { source = source.Description(null), representation = "region-index", items, pagination = new { offset, limit, total = chunks.Length } };
    }
    private static object World(string root, CliArguments request)
    {
        if (request.Options.Keys.Any(key => key is not ("limit" or "offset" or "types"))) throw new CliFailure("INVALID_ARGUMENTS", "World listing takes only pagination and display options");
        var inventory = QuerySource.Inventory(root); var discovery = WorldDiscovery.Discover(inventory, new(SnapshotScope.All));
        if (IsResourceLimit(discovery.DiagnosticCode) || discovery.Sources.Any(source => IsResourceLimit(source.DiagnosticCode)))
            throw new CliFailure("RESOURCE_LIMIT", "World catalog exceeds a source, path, depth or byte budget");
        var partial = discovery.Metadata?.Profile == MinecraftProfiles.ClassicAnvil && !discovery.Complete;
        if (discovery.Metadata is null || !discovery.Complete && !partial)
            throw new CliFailure(discovery.Sources.Any(source => source.Outcome == SourceOutcome.Unsupported) ? "UNSUPPORTED_SOURCE" : "INCOMPLETE_SOURCE",
                discovery.DiagnosticCode + " " + string.Join(",", discovery.Sources.Where(source => source.Outcome is not (SourceOutcome.Success or SourceOutcome.Irrelevant)).Take(3).Select(source => source.DiagnosticCode)));
        var level = CompressedNbt.ReadDocument(inventory.Read(new(inventory.WorldId, "level.dat", ArtifactKind.Nbt)), NbtCompression.GZip).Document;
        var named = NbtLocator.Resolve(level.Root, new([PathSegment.ForKey("Data"), PathSegment.ForKey("LevelName")]));
        var dimensions = discovery.Metadata.DimensionDirectories.OrderBy(pair => pair.Value, StringComparer.Ordinal).Select(pair =>
        {
            var files = discovery.Sources.Where(source => source.Chunk is null && source.Location.Dimension == pair.Value
                && source.Location.Role != WorldSourceRole.Irrelevant).ToArray();
            return new { id = pair.Value, directory = pair.Key, regions = files.Where(source => source.Location.Artifact.Kind == ArtifactKind.Region).Select(source => source.Location.Artifact.RelativePath).ToArray(),
                entities = files.Where(source => source.Location.Artifact.Kind == ArtifactKind.Entities).Select(source => source.Location.Artifact.RelativePath).ToArray() };
        }).ToArray();
        var offset = request.Number("offset", 0, 0, 2_000_000); var limit = request.Number("limit", 100, 1, 10_000);
        var data = new { representation = "world-catalog", worldId = inventory.WorldId, worldName = (named.Value as NbtString)?.Value, discovery.Metadata.DataVersion, profile = discovery.Metadata.Profile.Id,
            dimensions = dimensions.Skip(offset).Take(limit).ToArray(), pagination = new { offset, limit, total = dimensions.Length }, catalogComplete = discovery.Complete };
        if (partial) throw new CliFailure("UNSUPPORTED_SOURCE", "Classic Anvil catalog has unresolved sources; inspect scan diagnostics", data);
        return data;
    }
    internal static bool IsResourceLimit(string? code)
    {
        if (code?.StartsWith("PackMetadata:", StringComparison.Ordinal) == true) code = code["PackMetadata:".Length..];
        return code is "ArtifactSizeLimit" or "LimitExceeded" or "SourceLimitExceeded" or "DirectoryLimitExceeded"
            or "CatalogLimitExceeded" or "CatalogPathBytesLimit" or "DirectoryDepthLimit";
    }
}

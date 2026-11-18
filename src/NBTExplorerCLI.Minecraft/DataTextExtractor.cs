using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using System.Text;
using System.Text.Json;

namespace NBTExplorerCLI.Minecraft;

public static class DataTextExtractor
{
    public static TextExtractionResult Json(WorldSourceLocation source, byte[] json, WorldMetadata metadata, DecodeLimits? limits = null, SnapshotScope? scope = null, Action<SourceAddress>? componentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(metadata);
        limits ??= DecodeLimits.Default;
        using var document = BoundedJson.ReadDecoded(json, limits);
        var binding = new SourceAddress(source.Artifact, TypedPath.Root, "world.data", source.Dimension);
        var output = new TextExtractionCollector(binding, false, limits, scope);
        var roots = source.Dimension is null ? new[] { "" } : metadata.DimensionDirectories.Where(pair => pair.Value == source.Dimension).Select(pair => pair.Key).ToArray();
        if (roots.Length != 1 || source.Role != WorldSourceRole.SavedData || source.PackRoot is not null
            || metadata.Profile.Features[ProfileFacet.Layout].Schema != "java-dimensions-namespaced")
        { output.Diagnostic(TypedPath.Root, "UnsupportedDataJson"); return output.Finish(); }
        var prefix = (roots[0].Length == 0 ? "" : roots[0] + "/") + "data/minecraft/bossbars/";
        var path = source.Artifact.RelativePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
        { output.Diagnostic(TypedPath.Root, "UnsupportedDataJson"); return output.Finish(); }
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetNativeProperty("Name", out var name))
            output.Diagnostic(TypedPath.Root, "MissingBossbarName");
        else if (output.CouldInclude("bossbar.name"))
        {
            var address = new SourceAddress(source.Artifact, new([PathSegment.ForKey("Name")]), "bossbar.name", source.Dimension);
            output.Merge(TextComponentExtractor.Json(address, Encoding.UTF8.GetBytes(name.GetRawText()), metadata.Profile, limits, output.Scope, componentRoot));
        }
        return output.Finish();
    }
}

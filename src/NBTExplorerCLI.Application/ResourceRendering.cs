using System.Text.Json;
using System.Collections.Immutable;
using System.Diagnostics;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;

namespace NBTExplorerCLI.Application;

internal static class ResourceRendering
{
    internal static readonly DecodeLimits Limits = DecodeLimits.Default with { MaxDecodedBytes = 67_108_864, MaxContainerEntries = 100_000 };
    internal static Dictionary<string, byte[]> Render(ResourceGeneration generation, Dictionary<string, byte[]> existing, ResourceInputSnapshot? merge)
    {
        using var observation = WorkDiagnostics.Start("resource.render-set", phase: "render", worldId: generation.WorldId);
        var files = new Dictionary<string, byte[]>(existing, StringComparer.Ordinal);
        if (generation.Format != ResourceOutputFormat.LanguageFiles)
        {
            ResourceInputService.RequirePack(generation);
            if (files.Count != 0) Validate(generation, files);
            foreach (var entry in merge?.Entries ?? [])
            {
                using var memberTrace = ObserveMember(generation, entry.Path, "resource.merge");
                var bytes = merge!.Read(entry.Path);
                memberTrace?.SetTag("bytes", bytes.LongLength);
                if (files.TryGetValue(entry.Path, out var prior) && !prior.AsSpan().SequenceEqual(bytes))
                {
                    if (!IsLanguage(entry.Path)) throw new ArtifactConflictException("Merge input conflicts with existing resource bytes");
                    using var language = BoundedJson.ReadDecoded(bytes, Limits);
                    bytes = WorldLanguageEdits.AddValues(prior, language.RootElement.EnumerateObject().Select(property =>
                        (NativeJsonStrings.PropertyName(property), NativeJsonStrings.Read(property.Value))), Limits).Bytes;
                }
                files[entry.Path] = bytes;
            }
            if (!files.ContainsKey("pack.mcmeta"))
            {
                using var metadataTrace = ObserveMember(generation, "pack.mcmeta", "resource.render");
                var format = generation.PackFormat!;
                files.Add("pack.mcmeta", format.Major < 65
                    ? JsonSerializer.SerializeToUtf8Bytes(new { pack = new { pack_format = format.Major, description = "NBTExplorerCLI translations" } })
                    : JsonSerializer.SerializeToUtf8Bytes(new { pack = new { min_format = new[] { format.Major, format.Minor }, max_format = new[] { format.Major, format.Minor }, description = "NBTExplorerCLI translations" } }));
                metadataTrace?.SetTag("bytes", files["pack.mcmeta"].LongLength);
            }
        }
        foreach (var language in generation.Languages)
        {
            var path = generation.Format == ResourceOutputFormat.LanguageFiles ? language.Locale + ".json" : "assets/minecraft/lang/" + language.Locale + ".json";
            using var memberTrace = ObserveMember(generation, path, "resource.render");
            var current = files.GetValueOrDefault(path) ?? "{}"u8.ToArray();
            files[path] = WorldLanguageEdits.AddValues(current, language.Values.Select(value => (value.Key, WorldKeyEdits.LanguageText(value.Text, Limits))), Limits).Bytes;
            memberTrace?.SetTag("bytes", files[path].LongLength);
        }
        long retained = 0;
        foreach (var bytes in files.Values)
            if ((retained += bytes.Length) > Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Resource outputs exceed aggregate byte budget");
        if (generation.Format != ResourceOutputFormat.LanguageFiles) Validate(generation, files);
        return files;
    }
    private static void Validate(ResourceGeneration generation, Dictionary<string, byte[]> files)
    {
        using var observation = WorkDiagnostics.Start("resource.projection", phase: "projection", worldId: generation.WorldId);
        ResourceInputService.ValidateFiles(files, generation.PackFormat!, generation);
        // Reuse the recipe's complete path/count/retention rules for existing and generated packs too.
        _ = new ResourceGeneration(generation.WorldId, generation.Format, generation.Destination, generation.Languages, generation.PackFormat,
            new(Hash256.Compute([]), files.Select(file => new ResourceMergeEntry(file.Key, Hash256.Compute(file.Value), file.Value.Length)).ToImmutableArray()));
    }
    internal static Activity? ObserveMember(ResourceGeneration? generation, string member, string operation)
    {
        if (!WorkDiagnostics.IsEnabled) return null;
        string? physical = generation is null ? null : generation.Format == ResourceOutputFormat.ZipPack ? generation.Destination
            : generation.Destination.Length == 0 ? member : generation.Destination + "/" + member;
        var observation = WorkDiagnostics.Start(operation, physical, worldId: generation?.WorldId);
        observation?.SetTag("entry", member);
        return observation;
    }
    private static bool IsLanguage(string path)
    {
        var remaining = path.AsSpan(); if (!remaining.StartsWith("assets/", StringComparison.Ordinal)) return false;
        remaining = remaining[7..]; var separator = remaining.IndexOf('/'); if (separator <= 0) return false;
        remaining = remaining[(separator + 1)..];
        return remaining.StartsWith("lang/", StringComparison.Ordinal) && !remaining[5..].Contains('/') && remaining.EndsWith(".json", StringComparison.Ordinal);
    }
    internal static byte[] Zip(ResourceGeneration generation, byte[]? original, Dictionary<string, byte[]> files)
    {
        using var observation = WorkDiagnostics.Start("resource.archive", generation.Destination, "render", generation.WorldId);
        byte[] empty = [0x50, 0x4b, 5, 6, .. new byte[18]];
        var current = original ?? empty; var prior = ResourceInputService.ZipFiles(current);
        current = ZipArchiveValidation.ReplaceEntries(current, files.Where(file => prior.ContainsKey(file.Key))
            .Select(file => (file.Key, Hash256.Compute(prior[file.Key]), file.Value)), Limits);
        var output = ZipArchiveValidation.AddEntries(current, files.Where(file => !prior.ContainsKey(file.Key)).OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => (file.Key, file.Value)), Limits);
        observation?.SetTag("bytes", output.LongLength);
        return output;
    }
}

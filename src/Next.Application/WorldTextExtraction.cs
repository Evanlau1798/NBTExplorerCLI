using Next.Domain;
using Next.Formats;
using Next.Minecraft;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

namespace Next.Application;

public sealed record TextSourceExtraction(DiscoveredSource Source, SourceOutcome Outcome, TextExtractionResult? Text, string? DiagnosticCode = null, CommandExtractionResult? Commands = null);
public sealed partial class WorldTextExtraction
{
    private readonly IWorldInventory inventory;
    private readonly ScanRequest request;
    public ImmutableDictionary<ArtifactAddress, ImmutableArray<StorageTextRule>> StorageRules { get; }
    public WorldDiscoveryResult Discovery { get; }
    internal DecodeLimits Limits => request.Limits ?? DecodeLimits.Default;
    internal IEnumerable<DiscoveredSource> ScanSources => Discovery.Sources.Where(source => source.DiagnosticCode is not ("InternalWriterLease" or "InternalCandidate"));
    public WorldTextExtraction(IWorldInventory inventory, ScanRequest request, IReadOnlyDictionary<ArtifactAddress, IReadOnlyList<StorageTextRule>>? storageRules = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory); ArgumentNullException.ThrowIfNull(request);
        this.inventory = inventory; this.request = request;
        var rules = ImmutableDictionary.CreateBuilder<ArtifactAddress, ImmutableArray<StorageTextRule>>(); long total = 0;
        if (storageRules is not null)
        {
            if (storageRules.Count > 100_000) throw new ArgumentException("Storage policy exceeds artifact budget", nameof(storageRules));
            foreach (var pair in storageRules)
            {
                if (pair.Key is null || pair.Key.WorldId != inventory.WorldId || pair.Key.Kind != ArtifactKind.Nbt || pair.Key.ContainerEntry is not null
                    || pair.Value is null || pair.Value.Count > 256 || (total += pair.Value.Count) > 100_000
                    || pair.Value.Any(rule => rule is null || rule.Path is null || !Enum.IsDefined(rule.Kind)))
                    throw new ArgumentException("Invalid or oversized explicit storage policy", nameof(storageRules));
                rules.Add(pair.Key, pair.Value.ToImmutableArray());
            }
        }
        StorageRules = rules.ToImmutable(); Discovery = WorldDiscovery.Discover(inventory, request, cancellationToken);
    }
    public IEnumerable<TextSourceExtraction> ReadSources() => ReadSourcesCore(Discovery.Sources);
    internal IEnumerable<TextSourceExtraction> ReadGroup(ImmutableArray<DiscoveredSource> sources) => ReadSourcesCore(sources);
    private IEnumerable<TextSourceExtraction> ReadSourcesCore(IEnumerable<DiscoveredSource> selected)
    {
        foreach (var group in selected.GroupBy(source => source.Location.Artifact.RelativePath, StringComparer.Ordinal))
        {
            using var observation = WorkDiagnostics.Start("artifact.extract", group.Key, worldId: inventory.WorldId);
            var sources = group.ToArray();
            if (sources.All(source => source.Outcome != SourceOutcome.Success))
            { foreach (var source in sources) yield return Forward(source); continue; }
            byte[]? bytes = null; RegionFile? region = null; ZipArchive? archive = null;
            string? code = null; var failureOutcome = SourceOutcome.Failed;
            try
            {
                var outer = sources.Single(source => source.Location.Artifact.ContainerEntry is null && source.Chunk is null);
                if (!PackUnchanged(outer)) code = "PackMetadataChanged";
                else
                {
                    bytes = inventory.Read(outer.Location.Artifact);
                    if (!PackUnchanged(outer)) code = "PackMetadataChanged";
                    else if (Hash256.Compute(bytes) != outer.ContentHash) code = "SourceChangedDuringExtraction";
                    else if (outer.Location.Artifact.Kind == ArtifactKind.Archive) archive = ZipArchiveValidation.OpenRead(bytes, request.Limits);
                    else if (outer.Location.Role is WorldSourceRole.Terrain or WorldSourceRole.Entities or WorldSourceRole.PointOfInterest) region = RegionFile.Read(bytes);
                }
            }
            catch (Exception error) when (DiscoveryFailure.IsExpected(error))
            { var failure = DiscoveryFailure.Describe(error); code = failure.Code; failureOutcome = failure.Outcome; }
            if (code is not null)
            {
                archive?.Dispose();
                foreach (var source in sources) yield return source.Outcome == SourceOutcome.Success ? new(source, failureOutcome, null, code) : Forward(source);
                continue;
            }
            using (archive)
                foreach (var source in sources) yield return ReadOne(source, bytes!, region, archive);
        }
    }
    private TextSourceExtraction ReadOne(DiscoveredSource source, byte[] bytes, RegionFile? region, ZipArchive? archive)
    {
        if (source.Outcome != SourceOutcome.Success) return Forward(source);
        if (source.Location.Role == WorldSourceRole.Archive || region is not null && source.Chunk is null)
            return new(source, SourceOutcome.Irrelevant, null, "ContainerEnumerated");
        try
        {
            if (archive is not null)
            {
                var entry = archive.GetEntry(source.Location.Artifact.ContainerEntry!) ?? throw new InvalidDataException("Missing snapshotted ZIP entry");
                bytes = new byte[checked((int)entry.Length)]; using var content = entry.Open(); content.ReadExactly(bytes);
                if (Hash256.Compute(bytes) != source.ContentHash) return new(source, SourceOutcome.Failed, null, "SourceChangedDuringExtraction");
            }
            TextExtractionResult text; CapabilityProfile? commandProfile = null;
            if (region is not null && source.Chunk is { } chunk)
            {
                var document = region.ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, request.Limits).Document;
                text = NbtTextExtractor.Extract(source.Location, document, chunk, request.Limits, scope: request.Scope, layoutProfile: Discovery.Metadata?.Profile);
                commandProfile = NbtTextExtractor.Profile(source.Location, document);
            }
            else if (source.Location.Artifact.Kind == ArtifactKind.Nbt || source.Location.Role == WorldSourceRole.Structure)
            {
                var document = ReadDat(bytes);
                text = NbtTextExtractor.Extract(source.Location, document, limits: request.Limits, storageRules: StorageRules.GetValueOrDefault(source.Location.Artifact, ImmutableArray<StorageTextRule>.Empty), scope: request.Scope, layoutProfile: Discovery.Metadata?.Profile);
                commandProfile = NbtTextExtractor.Profile(source.Location, document);
            }
            else if (source.Location.Role is WorldSourceRole.Language or WorldSourceRole.PackMetadata or WorldSourceRole.PackJson)
            {
                text = PackTextExtractor.Extract(source.Location, bytes, source.Pack ?? throw new InvalidDataException("Missing pack metadata snapshot"), request.Limits, request.Scope);
                commandProfile = MinecraftProfiles.ForPackMetadata(source.Pack!);
            }
            else if (source.Location.Role == WorldSourceRole.Function)
            {
                var start = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
                if (!WorldSourceScope.IncludesRole("command.function", request.Scope)) return new(source, SourceOutcome.Irrelevant, null, "TypeFilter");
                var limits = request.Limits ?? DecodeLimits.Default;
                var encoding = new UTF8Encoding(false, true);
                var binding = new SourceAddress(source.Location.Artifact, TypedPath.Root, "command.function");
                if (bytes.Length - start > limits.MaxDecodedBytes || TextExtractionBudget.RecordBytes(binding, encoding.GetCharCount(bytes, start, bytes.Length - start)) > limits.MaxDecodedBytes)
                    throw new CodecException(CodecError.LimitExceeded, 0, "Pending command retention budget exceeded");
                var script = encoding.GetString(bytes, start, bytes.Length - start);
                var profile = MinecraftProfiles.ForPackMetadata(source.Pack ?? throw new InvalidDataException("Missing function pack metadata"));
                if (profile is null) return new(source, SourceOutcome.Unsupported, null, "UnsupportedPackProfile");
                var grammar = profile.Features[ProfileFacet.CommandGrammar];
                var syntax = new SyntaxLocator(grammar.Schema, grammar.Version, start, bytes.Length - start, Hash256.Compute(bytes).Hex, start == 0 ? "utf8" : "utf8-bom", SyntaxLocator.DetectNewline(script));
                var address = new SourceAddress(source.Location.Artifact, TypedPath.Root, "command.function", syntax: syntax);
                text = new([], [], [], [new(address, script)], []);
                commandProfile = profile;
            }
            else if (source.Location.Role == WorldSourceRole.SavedData && Discovery.Metadata is { } metadata)
                text = DataTextExtractor.Json(source.Location, bytes, metadata, request.Limits, request.Scope);
            else return new(source, SourceOutcome.Unsupported, null, "UnsupportedTextSource");
            CommandExtractionResult? commands = null;
            if (!text.PendingCommands.IsEmpty)
            {
                if (commandProfile is null) return new(source, SourceOutcome.Unsupported, text, "CommandGrammarPending");
                commands = ExtractCommands(text.PendingCommands, commandProfile);
                text = text with { Occurrences = text.Occurrences.AddRange(commands.Text.Occurrences), TranslationKeys = text.TranslationKeys.AddRange(commands.Text.TranslationKeys),
                    Diagnostics = text.Diagnostics.AddRange(commands.Text.Diagnostics), PendingCommands = commands.Text.PendingCommands };
                CheckRetention(text, commands.References);
            }
            if (!text.Unclassified.IsEmpty) return new(source, SourceOutcome.Unsupported, text, "UnclassifiedStorageText");
            if (!text.Diagnostics.IsEmpty)
            {
                var unsupported = text.Diagnostics.All(diagnostic => diagnostic.Code.StartsWith("Unsupported", StringComparison.Ordinal) || diagnostic.Code.StartsWith("Unknown", StringComparison.Ordinal) || diagnostic.Code.StartsWith("Dynamic", StringComparison.Ordinal) || diagnostic.Code.StartsWith("Unresolved", StringComparison.Ordinal));
                return new(source, unsupported ? SourceOutcome.Unsupported : SourceOutcome.Failed, text, text.Diagnostics[0].Code, commands);
            }
            if (commands is { ReferenceCompleteness: not ReferenceCompleteness.Complete }) return new(source, SourceOutcome.Unsupported, text, "IncompleteCommandReferences", commands);
            return new(source, text.Occurrences.IsEmpty && text.TranslationKeys.IsEmpty && (commands?.References.IsEmpty ?? true) ? SourceOutcome.Irrelevant : SourceOutcome.Success, text, Commands: commands);
        }
        catch (Exception error) when (DiscoveryFailure.IsExpected(error))
        { var failure = DiscoveryFailure.Describe(error); return new(source, failure.Outcome, null, failure.Code); }
    }
    private NbtDocument ReadDat(byte[] bytes)
    {
        // World DAT roots are compounds, so raw root 0A cannot collide with a valid zlib CMF.
        var codec = bytes.Length == 0 || bytes[0] == (byte)NbtType.Compound ? NbtCompression.Raw : bytes[0] == 0x1f ? NbtCompression.GZip : NbtCompression.Zlib;
        return CompressedNbt.ReadDocument(bytes, codec, request.Limits).Document;
    }
    private bool PackUnchanged(DiscoveredSource source)
        => source.Location.PackRoot is not { } root || source.Location.Artifact.Kind == ArtifactKind.Archive
            || source.PackMetadataHash is { } expected && Hash256.Compute(inventory.Read(new(inventory.WorldId, root + "/pack.mcmeta", ArtifactKind.Json))) == expected;
    private static TextSourceExtraction Forward(DiscoveredSource source) => new(source, source.Outcome, null, source.DiagnosticCode);
}

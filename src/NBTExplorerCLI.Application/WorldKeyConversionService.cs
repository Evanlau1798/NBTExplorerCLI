using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using NBTExplorerCLI.Minecraft;

namespace NBTExplorerCLI.Application;

public sealed class WorldKeyConversionService(IWorldInventory inventory)
{
    private readonly IWorldInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    public ImmutableArray<KeyConversionPatch> Create(WorldQuerySnapshot snapshot, IEnumerable<LiteralTranslation> translations, ArtifactAddress language, string prefix)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(translations); ArgumentNullException.ThrowIfNull(language);
        if (snapshot.Manifest.WorldId != inventory.WorldId || language.WorldId != inventory.WorldId) throw new ArgumentException("Key inputs and outputs must belong to one world");
        if (string.IsNullOrEmpty(prefix) || prefix.Length > 128 || !prefix.All(unit => char.IsAsciiLetterOrDigit(unit) || unit is '.' or '_' or '-')) throw new ArgumentException("Key prefix requires 1-128 ASCII letters, digits, dot, underscore or dash");
        var requested = new List<LiteralTranslation>(); var addresses = new HashSet<SourceAddress>(); long retained = 0;
        foreach (var item in translations)
        {
            if (requested.Count >= 100_000 || item?.Slot is null || item.Reply?.Text is null || item.Slot.Validate(item.Reply) != SlotValidation.Valid) throw new ArgumentException("Invalid bounded key translation set");
            Charge(item.Slot.Address, item.Slot.SourceText.Length + (long)item.Reply.Text.Length + item.Slot.Context.Length + item.Slot.SourceLocale.Length + item.Slot.TargetLocale.Length
                + item.Slot.Placeholders.Tokens.Sum(token => (long)token.Length) + item.Slot.Id.Canonical.Length + item.Reply.SlotId.Canonical.Length);
            if (!addresses.Add(item.Slot.Address)) throw new ArgumentException("A key slot can occur only once");
            if (WorldReferenceService.IsIdentifyingName(item.Slot.Role)) throw new EditRejectedException(EditFailure.Precondition, "Identifying key conversion requires independently verified name authority");
            requested.Add(item);
        }
        if (requested.Count == 0) return [];
        var output = ImmutableArray.CreateBuilder<KeyConversionPatch>();
        var groups = requested.GroupBy(item => (item.Slot.Address.Artifact, item.Slot.Address.Chunk)).ToArray();
        var sourceKeys = groups.Select(group => group.Key).ToHashSet();
        var bySource = snapshot.Discovery.Sources.Where(source => sourceKeys.Contains((source.Location.Artifact, source.Chunk)))
            .ToDictionary(source => (source.Location.Artifact, source.Chunk));
        var reader = new WorldPlanBuilder(inventory);
        foreach (var group in groups)
        {
            if (!bySource.TryGetValue(group.Key, out var source))
                throw new EditRejectedException(EditFailure.Missing, "Key source is absent from the snapshot");
            if (source.Outcome != SourceOutcome.Success || source.ContentHash is null) throw new NotSupportedException("Key source is incomplete or unsupported");
            var bytes = reader.ReadSource(source, snapshot.Limits); NbtDocument? document = null; CapabilityProfile? profile;
            var wanted = group.SelectMany(item => Candidates(item.Slot.Address).Select(path => (item.Slot.Role, path))).ToHashSet();
            var roots = new HashSet<SourceAddress>();
            void Root(SourceAddress address) { if (wanted.Contains((address.SemanticRole, Logical(address)))) roots.Add(address); }
            TextExtractionResult text;
            if (source.Location.Artifact.Kind is ArtifactKind.Nbt or ArtifactKind.Region or ArtifactKind.Entities || source.Location.Role == WorldSourceRole.Structure)
            {
                document = source.Chunk is { } chunk ? RegionFile.Read(bytes).ReadChunk(chunk.LocalZ * 32 + chunk.LocalX, snapshot.Limits).Document : WorldArtifactEdits.ReadNbt(bytes, snapshot.Limits).Document;
                profile = NbtTextExtractor.Profile(source.Location, document);
                text = NbtTextExtractor.Extract(source.Location, document, source.Chunk, snapshot.Limits, snapshot.StorageRules.GetValueOrDefault(source.Location.Artifact, []), snapshot.Manifest.Scope, snapshot.Discovery.Metadata?.Profile, Root);
            }
            else
            {
                profile = source.Pack is null ? null : MinecraftProfiles.ForPackMetadata(source.Pack);
                if (profile is null) throw new NotSupportedException("Unknown key source profile");
                text = source.Location.Role == WorldSourceRole.Function ? new([], [], [], [WorldCommandEdits.BindFunction(source.Location.Artifact, bytes, profile, source.Location.Dimension)], [])
                    : PackTextExtractor.Extract(source.Location, bytes, source.Pack!, snapshot.Limits, snapshot.Manifest.Scope, Root);
            }
            foreach (var command in text.PendingCommands) _ = CommandTextExtractor.Extract(command, profile, snapshot.Limits, snapshot.Manifest.Scope, Root);
            var index = roots.ToLookup(address => (address.SemanticRole, Logical(address)));
            var jsonSpans = document is null && source.Location.Role != WorldSourceRole.Function ? NativeJsonValueSpans.Read(bytes, snapshot.Limits) : null;
            foreach (var item in group)
            {
                var matches = new List<TranslationKeyConversion>(); var key = prefix + item.Slot.Id.Digest.Hex;
                var outer = language.ContainerEntry is null ? null : new ArtifactAddress(language.WorldId, language.RelativePath, ArtifactKind.Archive);
                foreach (var root in Candidates(item.Slot.Address).SelectMany(path => index[(item.Slot.Role, path)]))
                {
                    var (expected, rule) = Expected(root);
                    try
                    {
                        var conversion = new TranslationKeyConversion(root, expected, rule, profile.Id, profile.Version, item.Slot, item.Reply,
                            new(language, outer, item.Slot.TargetLocale, key, item.Reply.Text)); conversion.Validate(); matches.Add(conversion);
                    }
                    catch (ArgumentException) { }
                }
                if (matches.Count != 1) throw new NotSupportedException("Selected text has no unique profile-declared literal component for key conversion");
                var accepted = matches[0];
                var cost = accepted.Expected is NbtString encoded ? (long)encoded.Value.Length : BinaryNbt.MeasureDocument(new("", accepted.Expected), snapshot.Limits).Bytes;
                Charge(accepted.Address, cost + key.Length + item.Reply.Text.Length);
                output.Add(new("key-" + item.Slot.Id.Digest.Hex, accepted));
            }
            (NbtValue Expected, KeyConversionRule Rule) Expected(SourceAddress root)
            {
                if (document is not null && root.Syntax is null)
                {
                    var value = NbtLocator.Resolve(document.Root, root.NodePath).Value ?? throw new ArtifactConflictException("Key component disappeared");
                    if (root.ComponentPath is null && value is NbtString && !root.NodePath.Segments.IsEmpty && root.NodePath.Segments[^1].Kind == PathSegmentKind.Index
                        && NbtLocator.Resolve(document.Root, new(root.NodePath.Segments.Take(root.NodePath.Segments.Length - 1))).Value is NbtList { ElementType: NbtType.String })
                        throw new NotSupportedException("Scalar components in physical string lists require an explicit whole-list shape conversion before key planning");
                    return (value, root.ComponentPath is not null ? KeyConversionRule.EncodedTextToTranslate : value is NbtString ? KeyConversionRule.LiteralToTranslateComponent : KeyConversionRule.StructuredTextToTranslate);
                }
                if (document is not null || source.Location.Role == WorldSourceRole.Function)
                {
                    var body = document is null ? bytes : new UTF8Encoding(false, true).GetBytes(((NbtString)NbtLocator.Resolve(document.Root, root.NodePath).Value!).Value);
                    var syntax = root.Syntax ?? throw new NotSupportedException("Key command root needs an exact syntax span");
                    if (syntax.StartByte > body.Length || syntax.LengthBytes > body.Length - syntax.StartByte) throw new ArtifactConflictException("Key root exceeds its source span");
                    return (new NbtString(new UTF8Encoding(false, true).GetString(body.AsSpan(checked((int)syntax.StartByte), checked((int)syntax.LengthBytes)))), KeyConversionRule.EncodedTextToTranslate);
                }
                if (!jsonSpans!.TryGetValue(root.NodePath, out var span)) throw new ArtifactConflictException("Key JSON component disappeared");
                var raw = bytes.AsSpan(span.StartByte, span.LengthBytes).ToArray();
                if (root.Syntax is not null || root.ComponentPath is not null)
                { using var json = BoundedJson.ReadDecoded(raw, snapshot.Limits); return (new NbtString(NativeJsonStrings.Read(json.RootElement)), KeyConversionRule.EncodedTextToTranslate); }
                return (new NbtString(new UTF8Encoding(false, true).GetString(raw)), KeyConversionRule.EncodedTextToTranslate);
            }
        }
        return output.ToImmutable();
        void Charge(SourceAddress address, long characters)
        {
            retained = checked(retained + TextExtractionBudget.RecordBytes(address, characters));
            if (retained > snapshot.Limits.MaxDecodedBytes) throw new CodecException(CodecError.LimitExceeded, 0, "Key recipe inputs exceed the snapshot retention budget");
        }
    }
    private static TypedPath Logical(SourceAddress address) => new(address.NodePath.Segments.Concat(address.Syntax?.NodePath?.Segments ?? []).Concat(address.ComponentPath?.Segments ?? []));
    private static IEnumerable<TypedPath> Candidates(SourceAddress address)
    {
        var path = Logical(address); yield return path;
        if (!path.Segments.IsEmpty && path.Segments[^1] == PathSegment.ForKey("text")) yield return new(path.Segments.Take(path.Segments.Length - 1));
    }
}

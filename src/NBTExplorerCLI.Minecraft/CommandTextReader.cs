using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NBTExplorerCLI.Domain;
using NBTExplorerCLI.Formats;
using static NBTExplorerCLI.Minecraft.TextExtractionCollector;

namespace NBTExplorerCLI.Minecraft;

internal sealed partial class CommandTextReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly CommandCandidate source;
    private readonly CapabilityProfile profile;
    private readonly DecodeLimits limits;
    private readonly SnapshotScope scope;
    private readonly Action<SourceAddress>? componentRoot;
    private readonly Action<CommandSelectorReference>? selectorReference;
    private readonly Action<CommandNameTemplate>? nameTemplate;
    private readonly Action<CommandStorageAccess>? storageAccess;
    private readonly IReadOnlySet<(string Storage, string Path)>? storageDisplayLists;
    private readonly bool inspectNested;
    private readonly int runtimeDataVersion;
    private readonly Hash256 hash;
    private readonly int byteBase;
    private readonly string newline;
    private readonly List<TextOccurrence> texts = [];
    private readonly List<TranslationKeyReference> keys = [];
    private readonly List<ExtractionDiagnostic> diagnostics = [];
    private readonly List<NameReferenceCandidate> references = [];
    private ReferenceCompleteness completeness = ReferenceCompleteness.Complete;
    private long retained;
    private long tokensConsumed;
    private int lineStart;
    private long lineByte;
    internal CommandTextReader(CommandCandidate source, CapabilityProfile profile, DecodeLimits limits, SnapshotScope scope, Action<SourceAddress>? componentRoot, Action<CommandSelectorReference>? selectorReference, Action<CommandNameTemplate>? nameTemplate, bool inspectNested = true, Action<CommandStorageAccess>? storageAccess = null, IReadOnlySet<(string Storage, string Path)>? storageDisplayLists = null, int? runtimeDataVersion = null)
    {
        (this.source, this.profile, this.limits, this.scope) = (source, profile, limits, scope);
        this.componentRoot = componentRoot;
        this.selectorReference = selectorReference;
        this.nameTemplate = nameTemplate;
        this.storageAccess = storageAccess;
        this.storageDisplayLists = storageDisplayLists;
        this.inspectNested = inspectNested;
        this.runtimeDataVersion = runtimeDataVersion ?? MinecraftProfiles.EarliestDataVersion(profile);
        var length = Utf8.GetByteCount(source.Command);
        if (length > limits.MaxDecodedBytes || TextExtractionBudget.RecordBytes(source.Address, source.Command.Length) > limits.MaxDecodedBytes) Limit();
        byteBase = source.Address.Syntax is { Encoding: "utf8-bom", StartByte: 3 } ? 3 : 0;
        var bytes = Utf8.GetBytes(source.Command); hash = Hash256.Compute(byteBase == 0 ? bytes : [0xef, 0xbb, 0xbf, .. bytes]);
        if (source.Address.Syntax is { } syntax && (syntax.StartByte != byteBase || syntax.LengthBytes != length || syntax.PreimageHash != hash.Hex))
            throw new CodecException(CodecError.InvalidTag, 0, "Command preimage binding differs");
        newline = SyntaxLocator.DetectNewline(source.Command);
    }
    internal CommandExtractionResult Read()
    {
        var classic = MinecraftProfiles.IsVersionedClassic(profile);
        var line = 0; var start = 0; var macroContinuation = false;
        for (var index = 0; index <= source.Command.Length; index++)
        {
            if (index < source.Command.Length && source.Command[index] is not ('\r' or '\n')) continue;
            var end = index;
            if (index < source.Command.Length && source.Command[index] == '\r' && index + 1 < source.Command.Length && source.Command[index + 1] == '\n') index++;
            var body = source.Command[start..end]; var path = new TypedPath([PathSegment.ForIndex(line++)]);
            lineStart = start;
            var trimmed = body.TrimStart(' ', '\t');
            if (source.Address.SemanticRole == "command.function" && (macroContinuation || trimmed.StartsWith('$')))
            {
                if (!macroContinuation) Unknown(path, start, end - start, "DynamicCommandMacro");
                macroContinuation = body.TrimEnd(' ', '\t').EndsWith('\\');
                body = "";
            }
            else if (trimmed.StartsWith('#')) body = "";
            try
            {
                if (classic) ClassicCommand(body, start, path);
                else
                {
                    var tokens = CommandTokens.Read(body, start, limits);
                    tokensConsumed = checked(tokensConsumed + tokens.Count);
                    if (tokensConsumed > limits.MaxNodes) Limit();
                    if (tokens.Count != 0 && !tokens[0].Text.StartsWith('#'))
                    {
                        if (tokens[0].Text.StartsWith('$')) Unknown(path, start, end - start, "DynamicCommandMacro");
                        else { Selectors(tokens, path); Command(tokens, 0, path, end); }
                    }
                }
            }
            catch (CommandSyntaxException) { Unknown(path, start, end - start, "InvalidCommandSyntax"); }
            catch (JsonException) { Unknown(path, start, end - start, "InvalidCommandComponent"); }
            catch (SnbtException error) when (error.Code != SnbtError.LimitExceeded) { Unknown(path, start, end - start, "InvalidCommandSnbt"); }
            lineByte += Utf8.GetByteCount(source.Command.AsSpan(start, Math.Min(source.Command.Length, index + 1) - start));
            start = index + 1;
        }
        return new(new(texts.ToImmutableArray(), keys.ToImmutableArray(), diagnostics.ToImmutableArray(), [], []), references.ToImmutableArray(), completeness);
    }
    private void Command(List<CommandToken> tokens, int index, TypedPath path, int end)
    {
        if (index >= tokens.Count) throw new CommandSyntaxException();
        var name = tokens[index].Text;
        if (index == 0 && source.Address.SemanticRole == "command.block" && name.StartsWith('/')) name = name[1..];
        if (name == "give" && tokens.Count >= index + 3)
        { CountArgument(tokens, index + 3); ItemArgument(tokens[index + 2], At(path, "give.item")); return; }
        if (name == "data") { DataCommand(tokens, index, path, end); return; }
        if (name == "item") { ItemCommand(tokens, index, path, end); return; }
        if (name == "summon" && tokens.Count >= index + 6)
        {
            if (tokens.Count != index + 6) throw new CommandSyntaxException();
            var identity = MinecraftIdentifier.DefaultNamespace(tokens[index + 1].Text);
            Structured(tokens[index + 5].Start, tokens[index + 5].Start + tokens[index + 5].Text.Length, At(path, "summon.nbt"), identity, false, summonTemplate: true); return;
        }
        if (name == "setblock" && tokens.Count >= index + 5)
        {
            if (tokens.Count > index + 6 || tokens.Count == index + 6 && tokens[index + 5].Text is not ("replace" or "destroy" or "keep")) throw new CommandSyntaxException();
            BlockArgument(tokens[index + 4], At(path, "setblock.block")); return;
        }
        if (name == "execute")
        {
            var cursor = index + 1;
            while (cursor < tokens.Count)
            {
                var clause = tokens[cursor++].Text;
                if (clause == "run") { Command(tokens, cursor, At(path, "run"), end); return; }
                var operands = clause switch { "as" or "at" or "in" or "anchored" or "align" or "on" or "summon" => 1, "positioned" => 3, "rotated" => 2, "facing" => 3, _ => -1 };
                if (clause is "if" or "unless" && cursor + 1 < tokens.Count && tokens[cursor].Text == "entity") operands = 2;
                if (clause is "if" or "unless" && cursor < tokens.Count && tokens[cursor].Text == "score") operands = ScoreCondition(tokens, cursor);
                if (clause is "if" or "unless" && cursor < tokens.Count && tokens[cursor].Text is "block" or "blocks" or "items") operands = BlockOrItemCondition(tokens, cursor);
                if (clause is "if" or "unless" && cursor + 1 < tokens.Count && tokens[cursor].Text == "predicate"
                    && MinecraftIdentifier.ResourceLocation(tokens[cursor + 1].Text)) operands = 2;
                if (clause is "if" or "unless" && cursor < tokens.Count && tokens[cursor].Text == "data")
                {
                    var dataPath = DataTargetEnd(tokens, cursor + 1);
                    if (dataPath >= 0 && dataPath < tokens.Count)
                    {
                        ObserveStorageOperand(tokens, cursor + 1, path);
                        if (StaticDataControlPath(tokens[cursor + 1].Text, tokens[dataPath].Text)) operands = dataPath + 1 - cursor;
                    }
                }
                if (clause == "store" && cursor + 3 < tokens.Count && tokens[cursor].Text is "result" or "success" && tokens[cursor + 1].Text == "score"
                    && ScoreHolder(tokens[cursor + 2].Text, allowWildcard: true) && PlainControlToken(tokens[cursor + 3].Text)) operands = 4;
                if (clause == "store" && cursor + 3 < tokens.Count && tokens[cursor].Text is "result" or "success" && tokens[cursor + 1].Text == "bossbar"
                    && MinecraftIdentifier.ResourceLocation(tokens[cursor + 2].Text) && tokens[cursor + 3].Text is "value" or "max") operands = 4;
                if (clause == "store" && operands < 0 && cursor < tokens.Count && tokens[cursor].Text is "result" or "success")
                {
                    var dataPath = DataTargetEnd(tokens, cursor + 1);
                    if (dataPath >= 0 && dataPath + 2 < tokens.Count)
                    {
                        var staticPath = StaticDataPath(tokens[dataPath].Text);
                        if (tokens[dataPath + 1].Text is "byte" or "short" or "int" or "long" or "float" or "double"
                            && Decimal(tokens[dataPath + 2].Text, out _))
                        {
                            ObserveStorageOperand(tokens, cursor + 1, path);
                            if (staticPath) operands = dataPath + 3 - cursor;
                        }
                    }
                }
                if (cursor < tokens.Count && clause is "positioned" or "rotated" && tokens[cursor].Text == "as") operands = 2;
                if (operands < 0 || cursor + operands > tokens.Count) { Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownExecuteGrammar"); return; }
                cursor += operands;
                if (cursor == tokens.Count && clause is "if" or "unless") return;
            }
            throw new CommandSyntaxException();
        }
        if (storageAccess is not null && name == "function" && MinecraftProfiles.SupportsFunctionMacros(profile)
            && index + 4 < tokens.Count && ResourceOrTag(tokens[index + 1].Text)
            && tokens[index + 2].Text == "with")
        {
            var sourceEnd = DataTargetEnd(tokens, index + 3);
            if (sourceEnd >= 0 && tokens.Count <= sourceEnd + 1) ObserveStorageOperand(tokens, index + 3, path);
        }
        if (StaticControlCommand(tokens, index, name)) return;
        var argument = name switch
        {
            "tellraw" when tokens.Count >= index + 3 => index + 2,
            "title" when tokens.Count >= index + 4 && tokens[index + 2].Text is "title" or "subtitle" or "actionbar" => index + 3,
            "bossbar" when tokens.Count >= index + 4 && tokens[index + 1].Text == "add" => index + 3,
            "bossbar" when tokens.Count >= index + 5 && tokens[index + 1].Text == "set" && tokens[index + 3].Text == "name" => index + 4,
            "team" when tokens.Count >= index + 4 && tokens[index + 1].Text == "add" => index + 3,
            "team" when tokens.Count >= index + 5 && tokens[index + 1].Text == "modify" && tokens[index + 3].Text is "displayName" or "prefix" or "suffix" => index + 4,
            "scoreboard" when tokens.Count >= index + 6 && tokens[index + 1].Text == "objectives" && tokens[index + 2].Text == "add" => index + 5,
            "scoreboard" when tokens.Count >= index + 6 && tokens[index + 1].Text == "objectives" && tokens[index + 2].Text == "modify" && tokens[index + 4].Text == "displayname" => index + 5,
            _ => -1
        };
        if (argument < 0) { Unknown(path, tokens[index].Start, end - tokens[index].Start, "UnknownCommandGrammar"); return; }
        Component(tokens[argument].Start, end, At(path, name + ".text"));
    }
    private void Component(int start, int end, TypedPath grammarPath, string? itemComponent = null, string? storedRole = null, bool storedList = false)
    {
        var binding = new SourceAddress(source.Address.Artifact, TypedPath.Root, storedRole ?? source.Address.SemanticRole + ".text", source.Address.Dimension, source.Address.Chunk);
        var modern = itemComponent is not null || storedRole is not null || profile.Features[ProfileFacet.TextSchema].Schema == "java-text-nbt";
        var bytes = Utf8.GetBytes(source.Command[start..end]);
        var spans = modern ? null : componentRoot is null ? NativeJsonStringSpans.Read(bytes, limits) : NativeJsonValueSpans.Read(bytes, limits);
        var callback = ComponentCallback(grammarPath, start, modern, spans);
        var result = itemComponent is not null ? SnbtTextExtractor.ItemComponent(binding, source.Command[start..end], profile, itemComponent, limits, scope, callback)
            : modern ? SnbtTextExtractor.Component(binding, source.Command[start..end], profile, limits, scope, callback, storedList)
            : TextComponentExtractor.NativeJson(binding, bytes, profile, limits, scope, callback);
        MergeComponent(result, grammarPath, start, end, modern, spans);
        if (result.Diagnostics.IsEmpty) ComponentReferences(bytes, start, end, grammarPath, modern,
            itemTree: itemComponent is not null && itemComponent is not ("minecraft:custom_name" or "minecraft:item_name" or "minecraft:lore"));
    }
    private void MergeComponent(TextExtractionResult result, TypedPath grammarPath, int start, int end, bool syntaxResult, Dictionary<TypedPath, SnbtSpan>? spans = null)
    {
        SourceAddress Bind(SourceAddress address) => BindComponent(address, grammarPath, start, syntaxResult, spans);
        foreach (var text in result.Occurrences) { var address = Bind(text.Address); Charge(address, text.Text); texts.Add(text with { Address = address }); }
        foreach (var key in result.TranslationKeys) { var address = Bind(key.Address); Charge(address, key.Key); keys.Add(key with { Address = address }); }
        foreach (var diagnostic in result.Diagnostics) Unknown(grammarPath, start, end - start, diagnostic.Code);
        foreach (var pending in result.PendingCommands)
            if (!inspectNested || !IsLiteralFreeStoredCommand(pending)) Unknown(grammarPath, start, end - start, "NestedCommandGrammarPending");
    }
    private bool IsLiteralFreeStoredCommand(CommandCandidate pending)
    {
        var address = pending.Address;
        var nested = new CommandCandidate(new(address.Artifact, address.NodePath, address.SemanticRole, address.Dimension, address.Chunk), pending.Command);
        var parsed = new CommandTextReader(nested, profile, limits, SnapshotScope.All, null, null, null, inspectNested: false, runtimeDataVersion: runtimeDataVersion).Read();
        return parsed.Complete && parsed.Text.Occurrences.IsEmpty && parsed.Text.TranslationKeys.IsEmpty && parsed.References.IsEmpty;
    }
    private SourceAddress BindComponent(SourceAddress address, TypedPath grammarPath, int start, bool syntaxResult, Dictionary<TypedPath, SnbtSpan>? spans)
    {
        var path = syntaxResult ? address.Syntax!.NodePath! : address.NodePath;
        var span = syntaxResult ? new SnbtSpan(checked((int)address.Syntax!.StartByte), checked((int)address.Syntax.LengthBytes)) : spans![path];
        return Address(new(grammarPath.Segments.Concat(path.Segments)), start, 0, address.SemanticRole, span.StartByte, span.LengthBytes, address.ComponentPath);
    }
    private Action<SourceAddress>? ComponentCallback(TypedPath path, int start, bool syntaxResult, Dictionary<TypedPath, SnbtSpan>? spans = null)
        => componentRoot is null ? null : address => { var root = BindComponent(address, path, start, syntaxResult, spans); Charge(root, ""); componentRoot(root); };
    private SourceAddress Address(TypedPath path, int start, int length, string role, int? localByte = null, int? localLength = null, TypedPath? componentPath = null)
    {
        var grammar = profile.Features[ProfileFacet.CommandGrammar];
        var syntax = new SyntaxLocator(grammar.Schema, grammar.Version, byteBase + lineByte + Utf8.GetByteCount(source.Command.AsSpan(lineStart, start - lineStart)) + (localByte ?? 0),
            localLength ?? Utf8.GetByteCount(source.Command.AsSpan(start, length)), hash.Hex, byteBase == 0 ? "utf8" : "utf8-bom", newline, path);
        return new(source.Address.Artifact, source.Address.NodePath, role, source.Address.Dimension, source.Address.Chunk, componentPath, syntax);
    }
    private void Unknown(TypedPath path, int start, int length, string code)
    {
        var address = Address(path, start, length, source.Address.SemanticRole); Charge(address, code); diagnostics.Add(new(address, code)); completeness = ReferenceCompleteness.Unresolved;
    }
    private void Charge(SourceAddress address, string value)
    {
        retained = checked(retained + TextExtractionBudget.RecordBytes(address, value.Length)); if (retained > limits.MaxDecodedBytes) Limit();
    }
    private static void Limit() => throw new CodecException(CodecError.LimitExceeded, 0, "Command extraction exceeds retention budget");
}

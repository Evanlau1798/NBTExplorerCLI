using NBTExplorerCLI.Domain;
using System.Collections.Immutable;
using System.Text;

namespace NBTExplorerCLI.Formats;

internal sealed class SnbtReader
{
    private readonly ImmutableArray<byte> _source;
    private readonly string _text;
    private readonly SnbtProfile _profile;
    private readonly DecodeLimits _limits;
    private readonly SnbtBudget _budget;
    private bool Modern => _profile == SnbtProfile.Java1215;
    private int _pos, _mappedChar, _mappedByte;
    public SnbtReader(ImmutableArray<byte> source, SnbtProfile profile, DecodeLimits limits)
    {
        (_source, _profile, _limits, _budget) = (source, profile, limits, new(limits));
        try { _text = Snbt.Utf8.GetString(source.AsSpan()); }
        catch (DecoderFallbackException error) { throw new SnbtException(SnbtError.InvalidToken, Math.Max(0, error.Index), "SNBT requires valid UTF-8"); }
    }
    private int Offset()
    {
        var boundary = _pos;
        if (boundary > 0 && boundary < _text.Length && char.IsHighSurrogate(_text[boundary - 1]) && char.IsLowSurrogate(_text[boundary])) boundary--;
        _mappedByte = checked(_mappedByte + Snbt.Utf8.GetByteCount(_text.AsSpan(_mappedChar, boundary - _mappedChar)));
        _mappedChar = boundary;
        return _mappedByte;
    }
    private SnbtException Error(SnbtError code, string message) => new(code, Offset(), message);
    private void White() { while (_pos < _text.Length && SnbtLiterals.White(_text[_pos])) _pos++; }
    private bool Take(char value)
    {
        White();
        if (_pos >= _text.Length || _text[_pos] != value) return false;
        _pos++; return true;
    }
    private void Expect(char value)
    {
        if (!Take(value)) throw Error(_pos == _text.Length ? SnbtError.UnexpectedEnd : SnbtError.InvalidToken, "Expected " + value);
    }
    public SnbtDocument Read(int startByte, bool complete)
    {
        if (startByte < 0 || startByte > _source.Length) throw new ArgumentOutOfRangeException(nameof(startByte));
        try { _pos = Snbt.Utf8.GetCharCount(_source.AsSpan(0, startByte)); }
        catch (DecoderFallbackException) { throw new ArgumentException("Fragment must start at a UTF-8 boundary", nameof(startByte)); }
        if (_pos == 0 && _text.StartsWith('\ufeff')) _pos++;
        var root = Value(0);
        if (complete)
        {
            White();
            if (_pos != _text.Length) throw Error(SnbtError.TrailingInput, "Data follows the complete SNBT root");
        }
        return new(_source, _profile, root, new(startByte, Offset() - startByte), _limits, complete);
    }
    private SnbtNode Value(int depth, NbtType context = NbtType.End)
    {
        White();
        var start = Offset();
        _budget.Node(depth, start);
        if (_pos == _text.Length) throw Error(SnbtError.UnexpectedEnd, "Expected SNBT value");
        if (_text[_pos] == '{') return Compound(depth, start);
        if (_text[_pos] == '[') return ListOrArray(depth, start);
        NbtValue value;
        if (_text[_pos] is '\'' or '"') value = new NbtString(Quoted());
        else if (Modern && SnbtModernNumbers.Starts(_text[_pos])) value = SnbtModernNumbers.Parse(NumberToken(), context, start);
        else
        {
            var token = Token();
            var saved = _pos;
            if (Modern && Take('('))
            {
                var argument = Value(depth + 1); Expect(')');
                value = SnbtOperations.Evaluate(token, argument.Value, start);
            }
            else
            {
                _pos = saved;
                value = Modern ? token.ToLowerInvariant() switch { "true" => new NbtByte(1), "false" => new NbtByte(0), _ => new NbtString(token) } : SnbtLiterals.Classic(token);
            }
        }
        if (value is NbtString text) _budget.Text(text.Value, start);
        return new(new LogicalMinecraftLeaf(value), new(start, Offset() - start), []);
    }
    private SnbtNode Compound(int depth, int start)
    {
        _pos++;
        var entries = new List<LogicalMinecraftEntry>();
        var children = new List<SnbtChild>();
        if (!Take('}'))
        {
            do
            {
                White(); var keyStart = Offset(); _budget.Entry(keyStart);
                var key = _pos < _text.Length && _text[_pos] is '\'' or '"' ? Quoted() : Token();
                var keySpan = new SnbtSpan(keyStart, Offset() - keyStart);
                if (key.Length == 0) throw new SnbtException(SnbtError.InvalidToken, keyStart, "Java SNBT keys cannot be empty");
                _budget.Text(key, keyStart); Expect(':');
                var child = Value(depth + 1);
                entries.Add(new(key, child.Value)); children.Add(new(PathSegment.ForKey(key), keySpan, child));
                if (!Take(',')) break;
                White();
            } while (_pos < _text.Length && _text[_pos] != '}');
            Expect('}');
        }
        return new(new LogicalMinecraftCompound(entries), new(start, Offset() - start), children);
    }
    private SnbtNode ListOrArray(int depth, int start)
    {
        _pos++;
        var arrayType = NbtType.End;
        var arrayStart = _pos;
        if (Modern) White();
        if (_pos < _text.Length && _text[_pos] is 'B' or 'I' or 'L')
        {
            var marker = _text[_pos++];
            if (Modern) White();
            if (_pos < _text.Length && _text[_pos] == ';')
            {
                arrayType = marker switch { 'B' => NbtType.Byte, 'I' => NbtType.Int, _ => NbtType.Long }; _pos++;
            }
        }
        if (arrayType == NbtType.End) _pos = arrayStart;
        var children = new List<SnbtChild>();
        var values = new List<LogicalMinecraftValue>();
        if (!Take(']'))
        {
            do
            {
                _budget.Element(Offset());
                White();
                if (Modern && arrayType != NbtType.End && (_pos == _text.Length || !SnbtModernNumbers.Starts(_text[_pos])))
                    throw Error(SnbtError.InvalidArrayElement, "Modern typed arrays require number literals");
                var child = Value(depth + 1, Modern ? arrayType : NbtType.End);
                if (Modern && arrayType != NbtType.End) child = Promote(child, arrayType);
                if (arrayType != NbtType.End && SnbtLiterals.Type(child.Value) != arrayType)
                    throw new SnbtException(SnbtError.InvalidArrayElement, child.Span.StartByte, "Typed array element has the wrong type");
                if (!Modern && arrayType == NbtType.End && values.Count > 0 && SnbtLiterals.Type(child.Value) != SnbtLiterals.Type(values[0]))
                    throw new SnbtException(SnbtError.MixedList, child.Span.StartByte, "SNBT through Java 1.21.4 list must be homogeneous");
                children.Add(new(PathSegment.ForIndex(values.Count), null, child)); values.Add(child.Value);
                if (!Take(',')) break;
                White();
            } while (_pos < _text.Length && _text[_pos] != ']');
            Expect(']');
        }
        LogicalMinecraftValue value = arrayType switch
        {
            NbtType.Byte => new LogicalMinecraftLeaf(new NbtByteArray(values.Select(item => ((NbtByte)((LogicalMinecraftLeaf)item).Value).Value))),
            NbtType.Int => new LogicalMinecraftLeaf(new NbtIntArray(values.Select(item => ((NbtInt)((LogicalMinecraftLeaf)item).Value).Value))),
            NbtType.Long => new LogicalMinecraftLeaf(new NbtLongArray(values.Select(item => ((NbtLong)((LogicalMinecraftLeaf)item).Value).Value))),
            _ => new LogicalMinecraftList(values)
        };
        return new(value, new(start, Offset() - start), children);
    }
    private static SnbtNode Promote(SnbtNode node, NbtType target)
    {
        if (node.Value is not LogicalMinecraftLeaf leaf || leaf.Value.Type is < NbtType.Byte or > NbtType.Long || leaf.Value.Type > target)
            throw new SnbtException(SnbtError.InvalidArrayElement, node.Span.StartByte, "Array cannot narrow or convert a non-integer element");
        var number = leaf.Value switch { NbtByte n => n.Value, NbtShort n => n.Value, NbtInt n => n.Value, NbtLong n => n.Value, _ => 0L };
        NbtValue promoted = target switch { NbtType.Byte => new NbtByte((sbyte)number), NbtType.Int => new NbtInt((int)number), _ => new NbtLong(number) };
        return new(new LogicalMinecraftLeaf(promoted), node.Span, node.Children);
    }
    private string NumberToken()
    {
        var token = new StringBuilder();
        void Run(int radix)
        {
            var beforeSpace = _pos;
            White();
            var beforeDigits = _pos;
            while (_pos < _text.Length && (_text[_pos] == '_' || char.IsAsciiDigit(_text[_pos])
                || radix == 16 && _text[_pos] is >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                if (token.Length >= 65_535) SnbtBudget.Limit(Offset());
                token.Append(_text[_pos++]);
            }
            if (_pos == beforeDigits) _pos = beforeSpace;
        }
        char Part(string allowed)
        {
            var saved = _pos; White();
            if (_pos < _text.Length && allowed.Contains(_text[_pos])) { var unit = _text[_pos++]; token.Append(unit); return unit; }
            _pos = saved; return '\0';
        }
        Part("+-"); White();
        var radix = 10;
        if (_pos + 1 < _text.Length && _text[_pos] == '0' && _text[_pos + 1] is 'x' or 'X' or 'b' or 'B')
        {
            var digitStart = _pos + 2;
            while (digitStart < _text.Length && SnbtLiterals.White(_text[digitStart])) digitStart++;
            var hex = _text[_pos + 1] is 'x' or 'X';
            if (hex || digitStart < _text.Length && (char.IsAsciiDigit(_text[digitStart]) || _text[digitStart] == '_'))
            {
                radix = hex ? 16 : 2; token.Append(_text.AsSpan(_pos, 2)); _pos += 2;
            }
        }
        Run(radix);
        if (radix == 10)
        {
            if (Part(".") != '\0') Run(10);
            if (Part("eE") != '\0') { Part("+-"); Run(10); }
            if (Part("fFdD") != '\0') return token.ToString();
        }
        var signOrType = Part("uUsSbBiIlL");
        if (signOrType is 'u' or 'U' or 's' or 'S') Part("bBsSiIlL");
        return token.ToString();
    }
    private string Token()
    {
        var start = _pos;
        while (_pos < _text.Length && SnbtLiterals.Unquoted(_text[_pos]))
        {
            if (_pos - start >= 65_535) SnbtBudget.Limit(Offset());
            _pos++;
        }
        if (_pos == start) throw Error(_pos == _text.Length ? SnbtError.UnexpectedEnd : SnbtError.InvalidToken, "Expected unquoted token");
        return _text[start.._pos];
    }
    private string Quoted()
    {
        var quote = _text[_pos++];
        var builder = new StringBuilder(); var bytes = 0;
        while (_pos < _text.Length)
        {
            var unit = _text[_pos++];
            if (unit == quote) return builder.ToString();
            if (unit == '\\')
            {
                if (_pos == _text.Length) throw Error(SnbtError.UnexpectedEnd, "Truncated escape");
                if (Modern)
                {
                    string escaped;
                    try { escaped = SnbtModernEscapes.Read(_text, ref _pos); }
                    catch (FormatException) { throw Error(SnbtError.InvalidEscape, "Invalid modern SNBT escape"); }
                    foreach (var decoded in escaped) bytes += decoded is > '\0' and <= '\u007f' ? 1 : decoded <= '\u07ff' ? 2 : 3;
                    if (bytes > _limits.MaxMutf8StringBytes) SnbtBudget.Limit(Offset());
                    builder.Append(escaped); continue;
                }
                unit = _text[_pos++];
                if (unit != quote && unit != '\\') throw Error(SnbtError.InvalidEscape, "SNBT through Java 1.21.4 only escapes its quote and backslash");
            }
            bytes += unit is > '\0' and <= '\u007f' ? 1 : unit <= '\u07ff' ? 2 : 3;
            if (bytes > _limits.MaxMutf8StringBytes) SnbtBudget.Limit(Offset());
            builder.Append(unit);
        }
        throw Error(SnbtError.UnexpectedEnd, "Unterminated SNBT string");
    }
}

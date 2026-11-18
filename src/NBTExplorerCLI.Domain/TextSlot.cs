using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NBTExplorerCLI.Domain;

public sealed class PlaceholderPolicy
{
    public ImmutableArray<string> Tokens { get; }
    public PlaceholderPolicy(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        Tokens = tokens.Take(257).ToImmutableArray();
        if (Tokens.Length > 256 || Tokens.Any(string.IsNullOrEmpty) || Tokens.Distinct(StringComparer.Ordinal).Count() != Tokens.Length)
            throw new ArgumentException("Expected at most 256 distinct nonempty literal tokens", nameof(tokens));
    }
    public bool Accepts(string source, string replacement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacement);
        return Tokens.All(token => Count(source, token) == Count(replacement, token));
    }
    private static int Count(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (start <= text.Length - token.Length)
        {
            var found = text.IndexOf(token, start, StringComparison.Ordinal);
            if (found < 0) break;
            count++;
            start = found + 1;
        }
        return count;
    }
    public static PlaceholderPolicy None { get; } = new([]);
}

public enum SlotValidation { Valid, WrongSlot, StaleSource, WrongLocale, EmptyDisallowed, PlaceholderMismatch }
public sealed record SlotReply(StableIdentity SlotId, Hash256 SourceTextHash, string TargetLocale, string Text);

public sealed class TextSlot
{
    public SourceAddress Address { get; }
    public StableIdentity Id { get; }
    public string SourceText { get; }
    public Hash256 SourceTextHash { get; }
    public string Context { get; }
    public string SourceLocale { get; }
    public string TargetLocale { get; }
    public bool AllowedEmpty { get; }
    public PlaceholderPolicy Placeholders { get; }
    public string Role => Address.SemanticRole;
    public TextSlot(SourceAddress address, string sourceText, string context, string sourceLocale,
        string targetLocale, bool allowedEmpty, PlaceholderPolicy placeholders)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocale);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);
        ArgumentNullException.ThrowIfNull(placeholders);
        (Address, SourceText, Context, SourceLocale, TargetLocale, AllowedEmpty, Placeholders)
            = (address, sourceText, context, sourceLocale, targetLocale, allowedEmpty, placeholders);
        Id = SourceIdentity.Slot(SourceIdentity.Occurrence(address), address.ComponentPath ?? TypedPath.Root, address.SemanticRole);
        SourceTextHash = TextRevision.Compute(sourceText);
    }
    public SlotValidation Validate(SlotReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(reply.Text);
        if (reply.SlotId != Id) return SlotValidation.WrongSlot;
        if (reply.SourceTextHash != SourceTextHash) return SlotValidation.StaleSource;
        if (reply.TargetLocale != TargetLocale) return SlotValidation.WrongLocale;
        if (!AllowedEmpty && reply.Text.Length == 0) return SlotValidation.EmptyDisallowed;
        return Placeholders.Accepts(SourceText, reply.Text) ? SlotValidation.Valid : SlotValidation.PlaceholderMismatch;
    }
}

public static class TextRevision
{
    public static Hash256 Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("nbtcli.text-revision\x01"u8);
        Span<byte> buffer = stackalloc byte[1024];
        BinaryPrimitives.WriteInt32BigEndian(buffer, text.Length);
        hash.AppendData(buffer[..4]);
        for (var start = 0; start < text.Length; start += 512)
        {
            var count = Math.Min(512, text.Length - start);
            for (var index = 0; index < count; index++)
                BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(index * 2, 2), text[start + index]);
            hash.AppendData(buffer[..(count * 2)]);
        }
        return new(Convert.ToHexString(hash.GetHashAndReset()));
    }
}

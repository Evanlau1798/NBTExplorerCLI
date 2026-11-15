using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Next.Domain;

public sealed record Hash256
{
    public string Hex { get; }
    public Hash256(string hex)
    {
        if (hex is null || hex.Length != 64 || !hex.All(Uri.IsHexDigit)) throw new ArgumentException("Expected full SHA-256 hex", nameof(hex));
        Hex = hex.ToLowerInvariant();
    }
    public static Hash256 Compute(ReadOnlySpan<byte> bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)));
    public override string ToString() => Hex;
}

public enum IdentityKind : byte { Artifact = 1, Occurrence = 2, Slot = 3, Group = 4, Members = 5, Memory = 6, Snapshot = 7 }

public sealed record StableIdentity
{
    public IdentityKind Kind { get; }
    public Hash256 Digest { get; }
    public ImmutableArray<byte> Canonical { get; }
    public StableIdentity(IdentityKind kind, Hash256 digest, IEnumerable<byte> canonical)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(canonical);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var bytes = canonical.Take(IdentityCanonical.MaximumBytes + 1).ToImmutableArray();
        if (bytes.Length > IdentityCanonical.MaximumBytes) throw new ArgumentException("Canonical identity exceeds 64 MiB", nameof(canonical));
        (Kind, Digest, Canonical) = (kind, digest, bytes);
    }
    public bool IsValid => !Canonical.IsDefault && Canonical.Length >= IdentityWriter.Header.Length + 2
        && Canonical.AsSpan().StartsWith(IdentityWriter.Header)
        && Canonical[IdentityWriter.Header.Length] == 1 && Canonical[IdentityWriter.Header.Length + 1] == (byte)Kind
        && Hash256.Compute(Canonical.AsSpan()) == Digest && IdentityCanonical.Validate(Kind, Canonical.AsSpan());
    public bool Equals(StableIdentity? other) => other is not null && Kind == other.Kind && Digest == other.Digest
        && Canonical.SequenceEqual(other.Canonical);
    public override int GetHashCode() => HashCode.Combine(Kind, Digest);
}

public enum IdentityRegistration { Added, Existing, Collision, Invalid }
public sealed class IdentityRegistry
{
    private readonly Dictionary<Hash256, StableIdentity> _identities = new();
    public IdentityRegistration Register(StableIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_identities.TryGetValue(identity.Digest, out var existing))
            return existing == identity ? IdentityRegistration.Existing : IdentityRegistration.Collision;
        if (!identity.IsValid) return IdentityRegistration.Invalid;
        _identities.Add(identity.Digest, identity);
        return IdentityRegistration.Added;
    }
}

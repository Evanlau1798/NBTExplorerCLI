using System.Buffers.Binary;

namespace Next.Domain;

internal static class IdentityCanonical
{
    internal const int MaximumBytes = 67_108_864;

    internal static Guid ReadWorld(StableIdentity identity)
    {
        if (!identity.IsValid) throw new ArgumentException("Expected a valid world-bound identity", nameof(identity));
        return new Reader(identity.Canonical.AsSpan()).WorldReference(identity.Kind);
    }

    public static bool Validate(IdentityKind kind, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) return false;
        try
        {
            var reader = new Reader(bytes);
            var restored = reader.Identity(kind);
            return restored.Canonical.AsSpan().SequenceEqual(bytes);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    // Rebuilding through the factories validates both field shape and canonical ordering.
    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;

        public Guid WorldReference(IdentityKind kind)
        {
            Header(kind);
            if (kind is IdentityKind.Artifact or IdentityKind.Snapshot) return World();
            if (kind == IdentityKind.Group) _ = Text();
            if (kind is IdentityKind.Group or IdentityKind.Occurrence) return ArtifactParent().WorldId;
            throw new ArgumentException("Identity kind does not directly bind a world", nameof(kind));
        }

        public StableIdentity Identity(IdentityKind expected)
        {
            Header(expected);
            var result = expected switch
            {
                IdentityKind.Artifact => SourceIdentity.Artifact(ArtifactBody()),
                IdentityKind.Occurrence => Occurrence(),
                IdentityKind.Slot => SourceIdentity.Slot(Parent(IdentityKind.Occurrence), RequiredPath(), Text()),
                IdentityKind.Group => SourceIdentity.Group(Text(), Location(), Int()),
                IdentityKind.Members => Members(),
                IdentityKind.Memory => SourceIdentity.Memory(Text(), Text(), Text(), Text(), Text(), Text(), Text()),
                IdentityKind.Snapshot => Snapshot(),
                _ => throw new FormatException("Unknown identity kind")
            };
            End();
            return result;
        }

        private void Header(IdentityKind expected)
        {
            if (!Take(IdentityWriter.Header.Length).SequenceEqual(IdentityWriter.Header) || Byte() != 1 || Byte() != (byte)expected)
                throw new FormatException("Invalid identity header");
        }

        private StableIdentity Occurrence()
        {
            var location = Location();
            return SourceIdentity.Occurrence(new(location.Artifact, location.NodePath, Text(), location.Dimension,
                location.Chunk, location.ComponentPath, location.Syntax, location.JsonTarget));
        }

        private ArtifactAddress ArtifactBody()
        {
            var world = World();
            var kind = (ArtifactKind)Byte();
            return new(world, Text(), kind, Optional());
        }

        private ArtifactAddress ArtifactParent()
        {
            var reader = new Reader(Blob());
            reader.Header(IdentityKind.Artifact);
            var artifact = reader.ArtifactBody();
            reader.End();
            return artifact;
        }

        private SourceAddress Location()
        {
            var artifact = ArtifactParent();
            var dimension = Optional();
            var chunk = Present() ? new ChunkPosition(Int(), Int()) : null;
            var node = RequiredPath();
            var component = Path();
            var target = (JsonTargetKind)Byte();
            var syntaxKind = Byte();
            if (syntaxKind > 2) throw new FormatException("Invalid syntax identity variant");
            var syntax = syntaxKind == 0 ? null : new SyntaxLocator(Text(), Int(), 0, 0, new string('0', 64), "utf-8", "none", syntaxKind == 2 ? RequiredPath() : null);
            return new(artifact, node, "canonical.location", dimension, chunk, component, syntax, target);
        }

        private StableIdentity Parent(IdentityKind expected)
        {
            var reader = new Reader(Blob());
            return reader.Identity(expected);
        }

        private StableIdentity Members()
        {
            var group = Parent(IdentityKind.Group);
            var revision = Long();
            var count = Count(100_000);
            var members = new StableIdentity[count];
            for (var index = 0; index < count; index++)
            {
                var data = Blob();
                if (data.Length <= IdentityWriter.Header.Length + 1) throw new FormatException("Missing member kind");
                var kind = (IdentityKind)data[IdentityWriter.Header.Length + 1];
                if (kind is not (IdentityKind.Occurrence or IdentityKind.Slot)) throw new FormatException("Invalid member kind");
                members[index] = new Reader(data).Identity(kind);
            }
            return SourceIdentity.Members(group, members, revision);
        }

        private StableIdentity Snapshot()
        {
            var world = World();
            var scope = new SnapshotScope(Set(), Set(), Set(), Set());
            var versions = new InterpretationVersions(Text(), Text(), Text(), Text());
            var count = Count(100_000);
            var sources = new ManifestSource[count];
            for (var index = 0; index < count; index++)
            {
                var artifact = ArtifactParent();
                var hex = Optional();
                var outcome = (SourceOutcome)Byte();
                sources[index] = new(artifact, hex is null ? null : new Hash256(hex), outcome, Optional());
            }
            return SourceIdentity.Snapshot(world, sources, scope, versions);
        }

        private Guid World() => Guid.TryParseExact(Text(), "D", out var world)
            ? world : throw new FormatException("Invalid world GUID");

        private TypedPath RequiredPath() => Path() ?? throw new FormatException("Required path is absent");
        private TypedPath? Path()
        {
            if (!Present()) return null;
            var count = Count(256);
            var segments = new PathSegment[count];
            for (var index = 0; index < count; index++)
                segments[index] = Byte() switch
                {
                    0 => PathSegment.ForKey(Text()),
                    1 => PathSegment.ForIndex(Int()),
                    _ => throw new FormatException("Unknown path segment kind")
                };
            return new(segments);
        }

        private string[] Set()
        {
            var count = Count(_remaining.Length / 4);
            var values = new string[count];
            for (var index = 0; index < count; index++) values[index] = Text();
            return values;
        }

        private string Text()
        {
            var count = Count((_remaining.Length - 4) / 2);
            var data = Take(checked(count * 2));
            var chars = new char[count];
            for (var index = 0; index < count; index++)
                chars[index] = (char)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index * 2, 2));
            return new string(chars);
        }

        private string? Optional() => Present() ? Text() : null;
        private bool Present() => Byte() switch
        {
            0 => false, 1 => true, _ => throw new FormatException("Invalid optional tag")
        };
        private ReadOnlySpan<byte> Blob() => Take(Count(_remaining.Length - 4));
        private byte Byte() => Take(1)[0];
        private int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        private long Long() => BinaryPrimitives.ReadInt64BigEndian(Take(8));
        private int Count(int maximum)
        {
            var count = Int();
            if (count < 0 || count > maximum) throw new FormatException("Invalid canonical length");
            return count;
        }
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _remaining.Length) throw new FormatException("Truncated canonical identity");
            var result = _remaining[..count];
            _remaining = _remaining[count..];
            return result;
        }
        private void End()
        {
            if (!_remaining.IsEmpty) throw new FormatException("Trailing canonical bytes");
        }
    }
}

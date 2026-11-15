# Domain contracts, version 1

These contracts belong to the new product. The Domain library performs no file I/O,
world discovery, serialization, cache publication or writes. Synthetic profiles prove
the model; they do not claim support for a particular Minecraft release.

## Addresses and trees

`ArtifactAddress` includes the world GUID, literal relative path, artifact kind and
optional exact archive entry. Paths use `/`, prohibit traversal and ambiguous Windows
filename characters; entry names retain their own case-sensitive spelling. Filesystem
containment, reparse points and duplicate ZIP entries still require storage validation.
`SourceAddress` adds dimension, global chunk, typed node/component paths, semantic role,
JSON value/property-name target and optional syntax locator. Region coordinates must
match the artifact filename; negative chunks use floor division by 32 and nonnegative
local coordinates. A display path or JSON pointer is a projection, never an identity.

Typed keys and indices remain different, including empty keys and numeric-looking keys.
NBT names and strings retain UTF-16 code units, including unpaired surrogates. The NBT
tree preserves every payload type, integer width, array kind, empty-list element type,
compound order and duplicate names. Float/double equality is raw-bit equality: signed
zero and distinct NaN payloads remain distinct. Compound duplicates resolve as
`Ambiguous`; missing nodes and invalid traversal are different outcomes.

Source address JSON uses `nbtcli.next.source-address`, schema version 1. All DTO member
names are camelCase; numeric coordinates and syntax fields must be present. Unknown or
duplicate members, mixed key/index segments and malformed strings are rejected.
Optional locations are null. Unsupported family/version is distinct from malformed
input. Normal Unicode is a JSON string; unpaired code units use the exact single-member
object `{"utf16be":"D800"}` (hex big-endian UTF-16). Limits: 64 MiB input, depth 64,
8192 JSON tokens and 256 path segments. Syntax byte offsets are hints valid only for
the specified SHA-256 preimage; later resolvers must check that hash and span bounds.

## Persistent identity encoding

Use SHA-256 over the complete canonical bytes; hex display is full 64-character
lowercase. Store the canonical bytes with the digest. Do not persist `GetHashCode()`.
Registration of a known digest with different canonical bytes returns `Collision`
without replacing the old identity; malformed new claims return `Invalid`.
Claim validation consumes the complete kind-specific structure, restores it through the
same semantic factories, and requires byte-for-byte canonical re-encoding. Header-only,
truncated, trailing, noncanonical and oversized claims cannot become usable parents.

Every identity starts with ASCII `nbtcli.next.identity`, byte `01` (encoding version),
then one kind byte: artifact=1, occurrence=2, slot=3, group=4, members=5, memory=6,
snapshot=7. Artifact kinds are Nbt=0, Region=1, Entities=2, Json=3, McFunction=4,
Archive=5, Text=6. Source outcomes are Success=0, Irrelevant=1, Unsupported=2,
Failed=3, Cancelled=4. These numeric values are part of this encoding version.

Primitive encoding:

- Integers are signed two's-complement big-endian, 32 or 64 bits as specified.
- Text is a nonnegative int32 UTF-16 **code-unit count**, then each UInt16 big-endian;
  no normalization, replacement, case folding or BOM. GUID text is lowercase `D` form.
- Optional text is byte 0 for absent, or byte 1 followed by text. Empty is not absent.
- A parent identity is int32 canonical-byte length followed by its full canonical bytes;
  parent kind and digest validity must match the required type.
- A path is byte 0 for absent, or byte 1 + int32 segment count + segments. Each segment
  starts with byte 0 + text for key, or byte 1 + int32 for nonnegative index.
- A set is int32 count + text elements, deduplicated and sorted by ordinal UTF-16 units.
  Semantic sequences retain their supplied order. Canonical identity limit is 64 MiB.

The following field lists are exact and ordered:

| Kind | Canonical fields after the header |
| --- | --- |
| Artifact | world GUID text; artifact kind byte; relative path text; optional entry |
| Occurrence | location; semantic-role text |
| Slot | occurrence parent; component path; role text |
| Group | group-kind text; container location; int32 rule version |
| Members | group parent; int64 revision; int32 member count; ordered occurrence/slot parents |
| Memory | source text; source locale; target locale; context class; placeholder contract; glossary version; profile version (all text) |
| Snapshot | world GUID text; dimension/region/type/artifact-path sets; codec/profile/rules/policy version text; int32 source count; ordered source records |

Location is artifact parent, optional dimension, chunk-present byte (then int32 X/Z
when present), node path, component path, JSON target byte (value=0/propertyName=1),
syntax variant byte (0=absent; 1=grammar-profile text and int32 grammar version;
2=the same grammar fields followed by a required typed syntax node path). Variant 1
retains its original canonical bytes; variant 2 distinguishes syntax leaves nested in
one physical String without using mutable byte offsets. Syntax
preimage hash, spans, encoding/newline and current text are excluded from location IDs.
They belong to source revision/preconditions, so text changes cannot silently retarget
an occurrence. Group kind distinguishes structural and inferred algorithms; rule version
is required and current members are excluded. A location shift still needs explicit
rebase/conflict handling; stable IDs do not authorize automatic relocation.
For syntax addresses the typed syntax node path, when present, identifies the grammar
node inside the physical String at `nodePath`; `componentPath` may identify a JSON leaf
inside that syntax String. The optional `syntax.nodePath` roundtrips in address schema 1.
For older addresses without that field, the typed node/component path identifies the grammar node;
Root denotes the grammar root. A byte span cannot turn Root into an arbitrary child.
G02/G03 resolvers must verify that the preimage-bound span matches that exact typed node,
reject inconsistencies, and never use an occurrence digest to skip those checks.

Snapshot source records sort by relative path, then exact entry, then artifact kind,
all ordinal (null entry precedes text). Each record contains artifact parent, optional
content-hash hex, outcome byte, optional diagnostic code. Duplicate artifacts and other
worlds are rejected. Success/irrelevant outcomes require a content hash. The manifest
binds revisions separately from occurrence/slot IDs; it is not an atomic world snapshot.
At most 100000 sources or group members are accepted.

## Independent golden vectors

Generated independently with Python `struct.pack('>i' / '>q')`,
`encode('utf-16-be', 'surrogatepass')` and `hashlib.sha256`, without loading product
assemblies. Verification runs the same seven vectors in separate en-US and tr-TR
worker processes, comparing fixed lengths/digests rather than runtime hash codes.

Common input: world `11111111-2222-3333-4444-555555555555`, Nbt artifact `level.dat`,
null entry, node path Key(`text`), role `literal`, all other location fields null/default.
Slot path is Key(empty)/Index(0)/Key(code unit D800), role `text.fragment`. Group kind
is `structural.book`, rule version 1, using that same container location. Members are
the occurrence then slot, revision 2. Memory fields: `same`, `en_us`, `zh_tw`, `name`,
empty, `1`, `1`. Snapshot uses empty filter sets, four version strings `1`, one Success
source for that artifact with SHA-256 of bytes `01 02 03`, and null diagnostic.

| Kind | Bytes | SHA-256 |
| --- | ---: | --- |
| Artifact | 122 | `3bc0b827727a2420fdfb8382dcbe487d01bbbffda09f6f7d285feec3a7dff00f` |
| Occurrence | 189 | `64caca3ac0c3c2f794582fe95a024a987bed3628d600c70776c1151db1ecc06f` |
| Slot | 267 | `8ff3bf40f09fbf73bd13e80aa486e8a0177bb6da872206dc2e6f64aafae70c2e` |
| Group | 209 | `c0eb641692a07b51e0527e23de3a20504ee36e3a48ffd3819315dcabdfb668f1` |
| Members | 711 | `e75af3620dfdc21c0e6fde810b4129837a0a1c506bdb64fe9cdb5bd2b369d1da` |
| Memory | 90 | `d754a175424deba34579097801b1c65872df01a1c782a57c23bd808dfc4a1942` |
| Snapshot | 403 | `3bf774fdbae8b3e0e228edf137ea072dd18e2b3ac748b90bf279c3e24b6a01f8` |

## Profile boundary

Layout, item schema, text schema, command grammar and compression have independent
schema names, versions and explicit read/extract/plan/write/recover support levels.
Unknown and Unsupported both deny the requested operation. Callers must supply their
required facets; read never implies extract or write, and write never implies recover.
Profile versions are separate from each feature schema version and the product version.

Physical NBT lists remain homogeneous. `LogicalNbtList` is an immutable heterogeneous
view retaining typed payloads, not a binary payload. `ListMapping` explicitly declares
homogeneous or compound-wrapper representation; wrapper mapping requires a literal key
(including the legal empty key). Unknown mapping allows no inferred conversion. G02
implements and verifies the actual adapter, preserving ordinary empty-key compounds.
The schema families `nbtcli.next.typed-nbt`, `nbtcli.next.display-json` and
`nbtcli.next.minecraft-text` are separate; none permits guessing that arbitrary
`type/value` properties are typed wrappers. Their interchange codecs follow in G02.

# Standalone resource generation protocol

This contract adds `ResourceGeneration` to the original five PatchKinds. Its
payload has `version: 1`; existing operation values, complete JSON bytes and
canonical hashes remain unchanged. Unknown generation versions are refused.
The original five-kind baseline is recorded in
`fixtures/contracts/pre-resource-operations.json`, captured from the accepted
`655f0fe` source before adding the new kind.

This is the Domain/Formats contract. The existing world preparation path refuses
this kind until a dedicated resource preparation service is integrated.
Public `translate export-lang` and `translate create-pack` remain integration work.

| Format | Destination | Complete physical output set |
| --- | --- | --- |
| `languageFiles` | Empty string; caller selects the physical output root. | One `<locale>.json` per declared locale. |
| `zipPack` | One basename ending in `.zip`. | One archive. |
| `directoryPack` | One nonempty directory basename. | `pack.mcmeta`, generated `assets/minecraft/lang/<locale>.json`, and declared merge files. |

The payload declares one physical scope identity, output format, destination,
languages, optional major/minor pack format, and optional immutable merge input.
Pack formats are required for packs and absent for standalone language files.
The Minecraft adapter must independently validate actual profile support; a
positive major number alone is not a support claim. The current key dependency
writer is unchanged, including its caller-selected resource namespace.

Language values are decoded strings, including empty values and exact UTF-16 code
units. Keys and locales have ordinal identity. Canonical encoding sorts locales,
keys and merge paths; enumeration order and worker count do not become identity.
Duplicate locales or keys are errors. The dedicated renderer must use the same
literal-percent adapter as key conversion, without granting component edits.

Merge input declares an immutable blob hash and the complete path/hash/length
manifest. It must include exact `pack.mcmeta`. Non-root entries are limited to
`assets/`; gameplay `data/` entries cannot borrow resource-generation authority.
Paths are relative, representable as UTF-8 and safe for physical Windows paths;
case aliases, traversal and ambiguous separators are refused. The future reader
must verify the blob and every entry against this manifest, validate metadata and
resource-pack shape, and preserve all undeclared content. Declared hashes are
not evidence that a caller-supplied blob has already been verified.
Preparation must classify the actual destination as a resource output scope;
gameplay directories cannot be repurposed as pack destinations. Existing pack
metadata and opaque contents are retained; differing opaque bytes or language
values at an existing claimed key are conflicts, not implicit overwrite grants.
External roots need the same semantic scope checks as world-local outputs.

The complete recipe is immutable and bounded: at most 256 locales, 100000 total
new language entries, 4096 UTF-16 units per key/path, 100000 merge entries and
physical outputs, 64 MiB of declared merge contents, and 64 MiB of charged UTF-16
recipe retention including all derived output paths. The 4096-unit path limit
applies after combining destination and entry, and each component is at most
255 units. Windows devices (including numbered/superscript forms), console device
aliases and tool-reserved `.nbtcli-` components are refused. JSON byte/token/depth
limits still apply independently. Invalid
or over-budget input is refused before constructing an accepted recipe.

A resource plan has exactly one operation under profile `resource-output-v1`
and policy `resource-generation-v1`. It cannot mix with TypedEdit, literal,
key-conversion, reference or restore operations. It exposes no gameplay mutation
targets, raw byte replacement, deletion or authority derived from a translation
page. Every output still needs an exact raw preimage or expected absence and a
complete preparation/check set. Raw bytes may only enter later preparation as
verified merge inputs; they are not a generation payload.

One operation counts once even when it promises multiple files. Applied results
require proofs for every physical output. A dedicated preparation service must
stage and verify the whole set, bind its actual output root, and then use the
existing backup/journal/commit/readback/recovery engine. Those filesystem and
public CLI obligations are not established by this pure protocol checkpoint.

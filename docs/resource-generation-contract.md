# Standalone resource generation protocol

This contract adds `ResourceGeneration` to the original five PatchKinds. Its
payload has `version: 1`; existing operation values, complete JSON bytes and
canonical hashes remain unchanged. Unknown generation versions are refused.
The original five-kind baseline is recorded in
`fixtures/contracts/pre-resource-operations.json`, captured from the accepted
`655f0fe` source before adding the new kind.

`ResourceInputService` freezes verified ZIP/directory inputs in immutable backup
objects. `ResourceOutputService` builds and prepares this dedicated kind, then
uses the existing apply/journal/readback/recovery engine. The gameplay snapshot
preparation path still refuses this kind. Public `translate export-lang` and
`translate create-pack` remain integration work.

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
case aliases, traversal and ambiguous separators are refused. The reader
verifies the blob and every entry against this manifest, validates metadata and
resource-pack shape, and preserves all undeclared content. Declared hashes are
not evidence that a caller-supplied blob has already been verified.
Preparation classifies the actual destination as a resource output scope;
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
require proofs for every physical output. Preparation stages and verifies the
whole set and binds its actual output root. Commit and recovery inspect the
physical scope again; directory pack members and preserved file preconditions
are checked before each commit. Output roots must already exist on local NTFS.
Every ancestor carrying `level.dat` is validated, including its separate registry
where required. Every actual output path is checked relative to every world
ancestor. Metadata and gameplay/dimension subtrees are refused; safe resource
locations such as root `resources.zip`, named resource directories,
`.nbtcli_translate` language files and `resourcepacks/` remain available.
Malformed or unknown ancestor metadata is refused. Nested world
markers cannot hide a more distant gameplay ancestor. Existing metadata handles
remain open during the operation. Scope checks are repeated between commits to
detect newly appearing markers; this does not claim atomic exclusion of an
arbitrary external process creating new files between the check and replacement.
Ancestor directory handles are acquired once; separate metadata branches add only
their own directory segments. The native handle witness uses live process handle
deltas and checks release after disposal; it does not measure peak RSS.

Immutable input capture validates before publishing its reusable resource index.
Source changes refuse capture; interrupted capture may leave verified raw backup
objects, without a completed resource input index. Frozen inputs and prepared
postimages are independent of later source-path changes. Resource output tests
cover native NTFS writes, injected I/O interruption and recovery; public CLI,
Minecraft loading, power loss and peak RSS remain separate gates.

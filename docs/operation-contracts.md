# Operation model contracts

These are pure Domain contracts. They perform no filesystem operations and do not
establish offline-world, atomic replace, journal, actual grammar completeness or disk
readback guarantees. Storage and scanner services must establish those facts before
constructing the corresponding claims. Generic edits and the durable write/restore
pipeline have [CLI adapters](cli-edit-contract.md); [session key plans](cli-key-plan-contract.md)
bind validated replies to live component roots and required language/pack outputs.

For key conversions the public root targets establish profile/source binding.
Conflict validation derives the literal slot and newly generated translate/percent
with fields, allowing independent nested components while rejecting competing
mutations. Native composition first checks every original root preimage, then
retains child conversions when transforming a containing root. This changes no
canonical operation payload or existing legal plan hash.

Several key operations may share one language target only when locale and content
are identical. The common validator coalesces that output claim, while preserving
every component target, generated-field conflict and operation receipt. Conflicting
claims still reject. `SourceIdentity.ReadSnapshotScope` exposes all four sets from
a validated canonical SnapshotId; exported semantic plans rebind that same scope.

## Text slots

`TextSlot` binds a complete `SourceAddress`, decoded text, context, source/target locale,
allowed-empty policy and explicit literal placeholder tokens. Its ID uses the accepted
occurrence/slot encoding; changing text at the same location changes its revision hash,
not its ID. Reply validation checks exact slot identity, source text hash, locale, empty
policy and placeholder multiplicities, in that order. No trimming, escaping, Unicode
normalization, marker replacement or automatic translation-memory reuse occurs here.

`TextRevision` hashes ASCII `nbtcli.next.text-revision`, byte 1, int32 big-endian UTF-16
code-unit count, then UInt16 big-endian code units. Unpaired surrogates are preserved.
The implementation hashes bounded chunks rather than allocating an encoded copy.
Independent Python struct/UTF-16BE surrogatepass/hashlib vectors are:

| Text | SHA-256 |
| --- | --- |
| `Hello %s` | `e96dd057ebff49fbe88f0484ee916a4f3df674fc8dafb60520b37f577128922a` |
| one code unit D800 | `87ede1dfd4bc7641e9f809caa12eb878a226cfab5468ae048c262aa01076d826` |
| 512 `x`, D800, 512 `y` | `5f547456a20ded98157dbb3226ae89daf1cbbbafb97fc0d102ed31ab9e8e117a` |

Placeholder policy lists at most 256 distinct, nonempty literal tokens. Each token's
ordinal occurrence count, including overlapping matches, must stay equal. Reordering
is allowed; undeclared markers such as `{{T0}}` or `‖§‖` remain ordinary text. Format
adapters must explicitly extract the correct token contract for their grammar.

## Exact typed edits and literal translation

`TypedEdit` declares Replace, Add, Remove or Rename with a typed path and exact expected
value (therefore expected type). Add targets an exact expected parent and declares its
child key/index and typed value. Compound additions append entries; list/array additions
insert at the declared snapshot index. Remove/Rename cannot remove or rename the root.
Rename is only for compound keys, preserving the value and entry position. Empty keys
are legal. A new name or key must be absent; ambiguous duplicate keys are never selected.

`TypedTreeEdits` validates all preconditions against one immutable source, rejects equal
or ancestor/descendant target paths, and applies changes in deterministic descending path
order so deletions preserve snapshot indices. Competing rename destinations are rejected.
Lists stay homogeneous; arrays retain their kind and scalar width. Root name, unaffected
entries, ordering and every unselected value remain unchanged. At most 100000 edits are
accepted. `Verify` compares the entire candidate with the exact authorized result; it
does not mask an authorized subtree and ignore changes within it.

`LiteralTranslations` only accepts validated replies targeting actual NBT string nodes.
The full artifact/world, dimension and chunk must match the supplied document binding.
Syntax locators, JSON targets and component views require their format adapter; this
physical NBT helper does not guess or parse strings as components. Translation reply
text cannot become an arbitrary TypedEdit or authorize a number, UUID or event change.

## Key conversion

`TranslationKeyConversion` explicitly declares a profile/version and one of two physical rules
for structured NBT components: literal-string to translate component, or a unique `text`
entry to `translate`. The source slot and its validated reply must match the exact
location and preimage. The structured rule preserves every other entry and its order;
existing `translate`, `with` or `fallback` fields and duplicate keys are rejected to
avoid activating fields that were not literal semantics. Other component encodings
need their own profile adapter; these synthetic rules do not claim Minecraft support.

`EncodedTextToTranslate` instead retains the exact raw expected text as an NBT String,
the component root, the source slot and its decoded-layer bindings. `Validate` requires
the same artifact, physical storage layer, syntax preimage/grammar and component scope;
the slot must be the root literal or its direct `text` member. `KeyConversionPatch`
validates this recipe without parsing it. `Replacement` and the physical NBT apply
helper reject it: the profile format adapter must parse and verify the actual source
before performing any transformation. The recipe alone is not write authority.

The language dependency names the exact output artifact/entry, locale, translation key
and decoded translated text. Archived language output also names its outer pack;
standalone JSON has no pack. World, locale and text must agree with the operation.
`TranslationKeyConversions.Verify` compares the complete physical document, including
events and styles inside the selected component. Plan/apply services stage all pack
outputs and verify dependency completion; session sealing requires their complete
durable receipt as described in [session-contract.md](session-contract.md).

## References and raw restore

`ReferenceSynchronization` stores a valid GroupId, revision, ordered membership digest,
coverage state and exact address/old-text/new-text/grammar declarations for each member.
Repeated member identities and a mismatched digest are rejected. Authorization requires
Complete coverage and the entire exact ordered declaration; missing, extra or changed
members and Incomplete/Unresolved coverage deny authorization. Syntax grammar/version
must match its locator. This models a claim produced by a scanner/parser; it does not
prove that an arbitrary supplied Complete claim covers every reference in a real world.

`ArtifactRestore` binds the verified backup manifest hash, exact authorized physical
artifacts and each current raw precondition. The current and backup artifact sets must
match, belong to one world and contain unique physical relative paths. ZIP entries
cannot masquerade as independent physical restore units. Target and current images each
distinguish Present(SHA-256) from Absent; null byte input denotes absence, while an empty
array denotes an existing empty file. Raw-byte verification permits corrupt preimages
without parsing. It neither verifies the backup manifest itself nor writes/deletes files;
G05 must supply verified provenance and enforce containment, locks and journal recovery.

## Receipt accounting

`OperationOutcome` has exactly nine terminal values: Applied, Unchanged, Skipped, Failed,
Conflict, Unsupported, Cancelled, RecoveryRequired and RolledBack. Pending/staged belong
to progress/journal state and cannot be terminal receipt values. A receipt has the exact
unique requested operation-ID set, one result per ID, canonical ordinal ID order, plan
hash, revision and dependency-completion flag. Each count includes zero and their sum
always equals Requested. Plan construction must provide the authoritative requested set.
All image proofs in one final receipt must agree on the Readback image for a physical
world/path, using ordinal case-insensitive Windows path comparison regardless of artifact
kind. Multiple operations may share the same consistent readback; they cannot claim that
one file is simultaneously at two different pre/postimages.

Applied/Unchanged require matching expected/readback postimage claims; RolledBack requires
matching preimage claims. Each proof names its physical artifact and Present(hash) or
Absent image; the proof collection is immutable and canonically sorted. Other outcomes
carry a reason and no image proofs. Matching image pairs are validated data claims, not
evidence that this pure model performed I/O. G05 must perform actual durable readback.
Success requires all operations Applied/Unchanged and all dependencies complete.

Recovery creates a strictly newer receipt over the same IDs and plan hash. Unresolved
RecoveryRequired may become a resolved image, conflict, failure, cancellation or remain
unresolved. Applied may become RolledBack, Conflict or RecoveryRequired. Unchanged and
RolledBack may become Conflict or RecoveryRequired after later conflicting or unavailable
readbacks. Other terminal records must remain identical. Earlier receipts stay immutable; results from different
revisions are never added together. Restoring data does not make the original apply a
success.

## Exact format preparation

`NativeJsonEdits.ReplaceStrings` accepts exact typed paths and expected/replacement
decoded strings. It validates the complete input, replaces only the selected value
tokens, and reparses the candidate. BOM, whitespace, number spellings, property names
and all unselected bytes survive. Escaped Java UTF16 code units remain strings; no-op
values preserve their original token spelling. Duplicate, missing, non-string and stale
targets fail before a candidate is returned. The bounded string-span reader is shared
with command extraction and limits its locator retention estimate to 128 MiB.

`Snbt.ReplaceMany` binds one immutable parsed snapshot to exact typed expected values.
It rejects overlapping targets including no-op ancestors, updates the authorized logical
tree, splices the original spans once, and reparses once to verify the complete result
and fragment boundary. Complete documents and embedded fragments retain unselected raw
bytes; typed arrays retain their kind. Both batch APIs accept at most 100,000 changes
and enforce codec budgets against the final candidate, independent of request ordering.
These format operations produce bytes; application preparation still verifies the
source preimages, operation/profile authority and complete physical output set.

## Immutable plans and prepared output

`PatchOperation` is a closed, explicitly tagged union: TypedPatch, LiteralPatch,
KeyConversionPatch, ReferencePatch or RestorePatch. Operation IDs are unique within
the plan. Payloads retain the complete typed edit, slot/reply, component conversion and
language output, complete ordered reference group, or raw restore authorization.
Typed operation paths must agree with their addresses; only key targets can be renamed.
Unknown reference coverage cannot become a plan operation. Reference group members must
belong to the world encoded in their GroupId.

`PatchPlan` binds a validated snapshot identity to its world, profile/policy versions,
and explicit RollForwardPreferred/RollbackPreferred recovery strategy,
immutable operations, raw artifact preconditions, output declarations and dependencies.
Physical artifacts are outer files, never ZIP entries. The declared outputs must exactly
equal the operation/dependency outputs; every output has an exact Present(hash) or Absent
precondition. Inputs and outputs reject duplicate Windows physical relative paths using
ordinal case-insensitive comparison, including aliases with different artifact kinds.
Semantic edits require an existing source image; a new language output may be Absent.
Standalone mcfunction syntax hashes must equal the physical file precondition. For an
archive entry or a decoded syntax string, the entry/decoded preimage hash is a separate
binding from the outer artifact's raw hash. Preparation must resolve and verify both
layers against the actual source; constructing a plan does not verify live bytes. Snapshot membership,
freshness, complete scan coverage and actual profile support still require G03/G05 checks;
the identity reference alone is not a substitute for loading that snapshot's metadata.

Key conversion automatically contributes its required language/pack output dependency.
Explicit dependencies must refer to distinct declared outputs. Raw restore and semantic
edits cannot compete for a file, and two restore operations cannot target the same file.
Restore raw preconditions and output Write/Delete intent must agree with its manifest
authorization. No output outside the declared operation set can be silently staged.

Across semantic operations the plan rejects equal and ancestor/descendant paths within
the same physical artifact/entry and chunk. Dimension metadata and semantic roles do not disambiguate physical
targets. Physical node, syntax logical and component paths are concatenated
conservatively for conflict checking. Targets in one syntax container must agree on
grammar/version, decoded preimage hash, encoding and newline. One logical syntax node
has one span, while different syntax nodes cannot have overlapping spans; independent
component fragments within the same encoded syntax node may share its span.
Rename destinations also participate, preventing a later operation from claiming the
same destination. This is intentionally conservative; later services must explicitly
resolve overlapping edits instead of choosing a last writer.

`OutputDeclaration` lists its distinct required validation check names. A
`PreparedManifest` requires exactly one candidate per output, all required checks passed
with evidence hashes, unique candidate paths under ordinal case-insensitive comparison,
and postimage Present(hash) plus a candidate path for Write, or Absent plus null path for
Delete. Restore candidates must also match the exact authorized backup image hash.
Candidate paths are opaque data bindings here; G05 must validate actual staging-root
containment and file ownership before using them. A validation check is a declared
result, not proof that a pure constructor ran a codec or touched disk.

`PatchPlan.Receipt` derives the requested ID set and plan hash from that actual plan for
non-image outcomes. `PreparedManifest.Receipt` additionally binds Applied/Unchanged/
RolledBack proofs to the exact operation output set and plan/prepared pre/postimages,
including multi-artifact operations and restoration of absence. Missing/extra artifacts,
wrong hashes and manifests belonging to another plan are rejected. Pure receipt revision
must be validated through this bound factory again before publication.
`JournalBinding.From` retains both PlanHash and ManifestHash. Re-staging the same semantic
plan at a different path preserves PlanHash and changes ManifestHash. Neither plan nor
prepared data contains timestamps, worker counters or mutable execution progress.

## Canonical plan/prepared encoding, version 1

Both hashes use standard SHA-256. The prefix is ASCII `nbtcli.next.contract`, byte 1,
then the family as text, then int32 schema version 1. Families are
`nbtcli.next.patch-plan` and `nbtcli.next.prepared-manifest`. Integers are signed
big-endian at their stated width. Text is int32 UTF-16 code-unit count plus UInt16
big-endian units, with no normalization/replacement. Optional text is byte 0 or byte 1
followed by text. Hashes are exactly 32 raw bytes. Identities are int32 byte length plus
the complete accepted identity canonical bytes; artifact fields use artifact identities.
Images are byte 0 for Absent or byte 1 plus hash. Arrays begin with int32 element count.

An address is its occurrence identity plus, when that identity declares syntax present,
int64 start byte, int64 byte length, raw preimage hash, encoding text and newline text.
This preserves revision-specific hints in plan hashes while keeping them out of stable
location IDs. Paths are int32 segment count followed by byte 0 + text for Key, or byte
1 + int32 for Index. Optional path segments use a separate byte 0/1 presence tag.

NBT values begin with the existing NbtType byte. Byte/Short/Int/Long use signed 8/16/32/64
bits; Float/Double use their raw 32/64 bits. String is text. Numeric arrays are int32 count
then fixed-width elements without per-element tags. List is element-type byte, int32
count, then recursively tagged child values. Compound is int32 entry count followed by
ordered name text and tagged values. This is a hash encoding, not a binary NBT codec.
Limits are 64 MiB canonical bytes, 256 nested NBT edges, 100000 total tagged NBT nodes
per contract, and 100000 entries per contract collection/aggregate target/artifact budget.

Plan field order after prefix/version: world GUID lowercase D text; snapshot identity;
profile version text; policy version text; recovery byte (RollForwardPreferred=0,
RollbackPreferred=1); operations; preconditions; outputs; dependencies.
Operations sort by ordinal ID. Preconditions and outputs sort by ordinal relative path.
Preconditions contain artifact then image. Outputs contain artifact, intent byte
(Write=0/Delete=1), sorted required check-name texts. Dependencies sort by consumer path
then required path, with exact duplicates removed, and contain consumer/required artifacts.

Each operation begins with ID text and PatchKind byte (TypedEdit=0, LiteralTranslation=1,
TranslationKeyConversion=2, ReferenceSynchronization=3, ArtifactRestore=4), then:

| Payload | Exact ordered fields |
| --- | --- |
| Typed | Address; action byte Replace=0/Add=1/Remove=2/Rename=3; path; expected typed value; replacement-present byte and optional typed value; child-present byte and optional segment; optional new name |
| Literal | Slot/reply bundle below |
| Key conversion | Address; expected value; rule byte StructuredTextToTranslate=0/LiteralToTranslateComponent=1/EncodedTextToTranslate=2; profile text; int32 profile version; slot/reply bundle; language artifact; pack-present byte and optional pack artifact; locale/key/text texts |
| References | Group identity; int64 group revision; member digest; completeness byte Complete=0/Incomplete=1/Unresolved=2; ordered members (address, expected/replacement texts, grammar text, int32 grammar version) |
| Restore | Verified manifest hash; authorized artifacts sorted by ordinal relative path (artifact, current image, backup target image) |

Slot/reply bundle: address; slot identity; decoded source text; source text hash; context;
source locale; target locale; allowed-empty byte 0/1; sorted placeholder-token texts;
reply slot identity; reply source hash; reply target locale; reply decoded text. Reference
member order and NBT entry/list order remain semantic sequences, never sorted as sets.

Prepared field order after prefix/version: plan hash; artifacts sorted by ordinal relative
path. Each artifact contains artifact identity, optional candidate path, postimage, then
checks sorted by ordinal name. A check contains name text, passed byte 0/1 and evidence
hash. Failed checks can be represented as input data but cannot form a PreparedManifest.

Independent Python struct/UTF-16BE/hashlib golden vectors use world
`11111111-2222-3333-4444-555555555555`, artifact `level.dat` kind Nbt, snapshot with that
one Success source hashing byte `01`, empty filters and four version strings `1`. The
plan has profile/policy `1`, RollForwardPreferred recovery, TypedPatch ID `edit`, role `item.name`, Key(`number`), Replace
Int(1) with Int(2); one Present(SHA256(`01`)) precondition, one Write output with checks
`parse`, `scope`, and no dependencies. Prepared uses `stage/a.dat`, SHA256(`02`) postimage,
passed parse evidence SHA256(`03`) and scope evidence SHA256(`04`).

| Contract | Canonical bytes | SHA-256 |
| --- | ---: | --- |
| Plan | 1152 | `1484b7c07af3ffc3bb1e09e74c097c538854cb484a04c3e8b79cf4d184439160` |
| Prepared | 407 | `2e76b68b64520e31b2f8a22cd9f040e387bea49521a5adfe15b0e3cce049d2ee` |

Verification compares these fixed digests in separate en-US and tr-TR worker processes;
other tests independently reverse operation/input/output/check enumeration and verify
semantic equivalence. Changing payload bits, policy, candidate path, postimage or evidence
must change the corresponding hash.

## Initial exchange families

`nbtcli.next.text-slot` and `nbtcli.next.typed-edit` use version 1 envelopes with exactly
`schemaFamily`, `schemaVersion`, and object `data`. Unknown versions/families are distinct
Unsupported errors; missing/unknown/duplicate members and malformed values are JSON
errors. Input limits are 64 MiB UTF-8, 1000000 JSON tokens and nesting 1024. Typed value
payloads additionally enforce depth 256 and 100000 nodes. Unpaired UTF-16 units use the
same exact `utf16be` wrapper as source addresses. Stable identities carry explicit kind,
digest and Base64 canonical bytes and must pass the accepted complete canonical validator.

TextSlot data contains address, slotId, sourceText, sourceTextHash, context, sourceLocale,
targetLocale, allowedEmpty and placeholders. The reader recomputes both ID and revision
and rejects inconsistent claims. The writer sorts placeholder tokens as a semantic set.
Embedded addresses reuse the full versioned SourceAddress contract.

TypedEdit data always declares action, path, expected, replacement, child and newName,
using explicit null for inapplicable fields. Replace needs replacement only; Add needs
replacement and a one-segment child path; Remove needs neither; Rename needs newName
(which may be empty). Mixing these fields is rejected. Expected and replacement use
explicit typed NBT objects, not display JSON: `tag` plus `value`, and `elementType` for
List. Byte/Short/Int are width-checked integers, Long and LongArray elements are decimal
strings, Float/Double are exact 8/16-digit raw-bit hex strings. Compound values are
ordered name/value entry arrays, preserving duplicates and ordinary type/value names.

Shared serialized fixtures live in `fixtures/contracts/`. SourceAddress, TextSlot and
TypedEdit each currently have minimal, boundary, unknown-version, missing-field and
exclusive-state examples, independently authored from these contracts with Python
struct/hashlib identities. The fixture manifest retains every baseline entry and includes
each new file's bytes/hash/provenance. The same envelope/budgets apply to the completed
plan/prepared/receipt exchange below. Legacy mapping is defined separately in
[legacy-mapping-contract.md](legacy-mapping-contract.md).

## Plan, prepared and receipt exchange

`PatchPlanJson` uses the plan family/version and all canonical semantic fields, including
recovery strategy and declared planHash. Operations are tagged `{id, kind, payload}`;
each kind has its own exact payload structure. Nested TypedEdit and TextSlot reuse their
versioned families; embedded artifacts carry worldId/relativePath/kind/containerEntry.
Images explicitly carry expectedAbsent plus hash, with mutually exclusive valid states.
The reader reconstructs the complete plan, runs Domain invariants, and recomputes its hash.

`PreparedManifestJson.Read` requires the actual PatchPlan. It checks the declared plan
hash, complete artifact/check set, passed validations, restore target images and the
recomputed manifestHash. `ApplyReceiptJson.Read` likewise requires the actual plan, plus
its PreparedManifest for image outcomes. It rechecks exact operation membership and every
image proof, then recomputes requested, all nine counts and success. Those redundant
fields are claims that must match, not trusted values. Pending and unknown terminal
statuses cannot deserialize into an outcome. A non-image failed/cancelled receipt may be
exchanged before preparation; a fabricated Applied record cannot use that path.

Durable receipt/header writers validate byte, token and depth budgets with their
shared reader before publication. Preparation additionally bounds the worst-case
receipt (IDs, proofs, counts, revision and diagnostics) before any world commit,
reserving 64 KiB for the CLI envelope. Dynamic engine reasons use at most 256 UTF-16
code units and cannot be empty where an outcome requires a reason. These limits
reject oversized plans; they never truncate operation identities or user content.
`WorldApplyEngine.ReadStatus` returns direction, phase, sequence, backup manifest
hash and any receipt from the same loaded journal, including unfinished recovery.

All DTO constructor fields are required, including explicit null for inapplicable fields;
unknown or duplicate members are rejected. Output is deterministic in canonical collection
order. Six implemented exchange families have five shared input categories each; two
additional plan/receipt fixtures express all nine outcomes over one frozen operation set.
The fixture manifest includes 50 files, preserving the original 16 baseline entries.
The nine-outcome positive plan uses nine distinct physical artifacts. The original
same-file pre/postimage contradiction remains as a rejected `conflicting-images` fixture.

## World candidate preparation

`WorldPlanBuilder` binds explicit literal slots and typed NBT edits to live standalone,
Region/chunk or ZIP/entry sources from a `WorldQuerySnapshot`. It re-extracts literal scope,
uses the shared reply validator, and records raw output, world metadata and directory
pack metadata preimages even when the query excludes metadata payloads. Preview only
reads sources; it checks preimages again after binding instead of rebasing changed input.

`WorldPlanPreparation.Prepare` requires the caller's offline-world acknowledgement and
reconstructs the exact plan policy/hash. It stages and flushes every physical output,
then verifies the entire set and rechecks sources before exposing a `PreparedWorldPlan`.
Failure cleans the whole uncommitted set; disposal only cleans candidates. The returned
manifest is evidence of preparation, not a commit receipt or immunity to later changes.
NBT readback checks the exact authorized typed tree; JSON and command edits splice only
selected string tokens. Commands also re-extract slot values, keys, references and
diagnostics. Legacy SNBT values that require physical command line breaks are rejected.

Region preparation preserves unselected chunk descriptors, compressed payloads and
old sectors, and groups all selected edits into one physical region candidate. ZIP
replacement retains original local records, appends changed entry records and updates
only their central directory bindings; unselected compressed streams and metadata remain
byte-identical. No-op edits retain the complete original container. Nested JSON strings,
SNBT inside JSON and NBT command fields use their own decoded paths and preimage hashes;
their physical artifact preimage remains a separate plan condition.

Literal changes to entity/armor stand identifying name text or fallback require the
explicit complete reference synchronization path; ordinary literal plans reject them.
No-op names and distinct show_text hover/hover fallback slots remain valid. Region
preparation verifies one chunk tree at a time and retains
only its descriptor and payload hash for persisted verification. Entry output buffers and
nested replacement strings have aggregate retention bounds as well as per-transform limits.

Key conversion binds the exact live text component root declared by its source profile,
not every string slot (plain book titles/authors cannot become components). Native NBT,
native JSON, legacy embedded JSON and JSON/SNBT/embedded JSON book pages can convert the
selected literal component to `translate`, preserving sibling text, style and events.
Entity/armor stand identifying name conversions require reference authority even when
the requested text equals the old name. Existing resource language files, including ZIP
entries, are explicit plan outputs and dependencies; incompatible pack formats and
different existing key values are rejected. Literal percent signs use one fixed percent
argument and a language pattern that preserves them without interpreting user text.

A missing language JSON may be declared under a known resource pack, including absent
namespace/language directories. A new resource ZIP can declare its language entries and profile
metadata together. Both use expectedAbsent raw preconditions, stage as part of the same
complete output set and reject files created after preview. New ZIPs are deterministic,
bounded, parsed and verified against every declared entry image before staging succeeds.
World language dependencies require locale filenames containing only lowercase ASCII
letters, digits, underscore, dot or hyphen. Invalid locales are rejected for existing and
new directory/ZIP targets without normalization; session locale values remain unchanged.
Missing language entries may also join an existing resource ZIP, bound to its complete
raw preimage. Additions preserve old local records and central metadata, require absent
unique entry names, and check final entry count, decoded bytes and ZIP size. Component
and language targets inside one ZIP are prepared together as a single physical output.

Known command profiles support key conversion in standalone/archived functions and real
NBT command fields. Component roots come from the command grammar and text profile;
ordinary `text` or `fallback` leaf fields do not grant component authority. Exact raw
root preconditions precede JSON/SNBT conversion. Command readback checks the complete
literal/key projection, including fixed percent arguments, references and diagnostics.
Literal and key edits sharing one encoded leaf are composed from the original snapshot
before a single physical splice. Native SNBT components inside JSON follow the same rule
and preserve raw bytes outside the selected component.

Live typed reference synchronization uses the source and group authorization contract
in [reference-analysis.md](reference-analysis.md). Unsupported reference domains remain
blocked. Durable apply/rollback services are described in [apply-recovery.md](apply-recovery.md).
Preparation does not change the original world.

## Verified raw backups

`WorldBackupService.Create` takes explicit physical artifact preconditions for one world.
It copies exact raw bytes without parsing Minecraft data, distinguishes empty files from
expectedAbsent images, verifies stored blobs and rereads sources before publishing the
canonical version 1 backup manifest. Content hashes identify immutable blobs and the
completion manifest; reordered replay produces the same backup identity.

`FileSystemWorldBackupStore` uses a separate owned atomic repository. Each object and
manifest is limited to 64 MiB, artifact declarations are bounded, and physical paths have
a 32767 UTF-16 unit limit. Manifest encoding stops at its output budget. A conflicting or
corrupt stored object is preserved and rejected. Failed capture may leave reusable blobs;
the service exposes a complete backup only after its manifest and all referenced images
read back successfully.

`Load` verifies world binding and all distinct stored images. `VerifiedBackup.ReadArtifact`
rechecks the requested blob before returning bytes; an unlisted artifact is rejected.
Backup validation covers byte integrity and manifest membership. Restore plans must bind
the current world's raw images separately, then use the same staging and commit engine.

## Raw restore preparation

`WorldPlanBuilder.Build(VerifiedBackup, restoreOperations)` accepts explicit subsets of
one verified backup. Each operation retains its own ID and declares its exact current
raw images. Membership, target image, manifest hash, world and current preconditions
are checked before a plan is returned. The raw snapshot identity binds present current
images and the complete declared path set; absent current files remain explicit plan
preconditions. No scan, Minecraft metadata or successful parser is required.

`WorldPlanPreparation.Prepare(VerifiedBackup, plan, worldOffline)` rebinds that policy and
uses the same complete-output staging pipeline as semantic plans. Every raw candidate
is flushed and checked against its verified backup bytes, then all candidates and current
preimages are checked again before exposing a PreparedManifest. Later stage failures,
candidate tampering and changed backup blobs clean the whole set without world writes.

An absent backup target produces a Delete intent with an absent postimage and no
manifest candidate path. `IArtifactStorage.StageDelete` retains a separate empty staging
marker and writer lease. It requires flush, verify and a matching current image before
explicit commit; the marker never replaces the source with an empty file. Deletion uses
the same checked handle with DELETE access and excludes external renames/content writes
until handle-based deletion completes, then checks absence by readback.
An uncertain deletion attempt preserves its marker for recovery;
Dispose of an unattempted candidate only removes the marker. This primitive does not
provide a complete journal, automatic rollback or a world-level restore command.

Missing output parents do not require preview or staging to create formal directories.
Plans reject physical input/output file paths that are ancestors of another declared
file, using case-insensitive component boundaries before any candidate is staged.
An expectedAbsent candidate stages in the nearest existing guarded ancestor on the same
NTFS volume. Before reading the current target, staging/commit validates and holds any
parents that have appeared since discovery; reparse paths are rejected before target reads.
Only explicit commit creates and guards remaining parents, then atomically moves the
candidate without overwriting an externally created target. Already absent deletion
creates no directories. A failed commit may retain newly created parents and its candidate
for recovery; unattempted Dispose removes only its candidate and leaves parents absent.

Agent configuration uses `nbtcli.next.agent-questionnaire` and
`nbtcli.next.agent-configuration`, version 1. Answers bind operation/world/revision;
confirmed state retains the raw input/hash and consecutive submission receipts.
Questionnaires are bounded to 16384 UTF-8 bytes and state to 1024 submissions.
Exact historical resends do not overwrite newer preferences; defaults affect only
new sessions. Preferences and explanatory instructions grant no world authority.
Public workflow and compatibility details are in [agent CLI](cli-agent-contract.md).

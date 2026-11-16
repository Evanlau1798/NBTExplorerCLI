# Read-only legacy mapping design

The G04 importer retains identified legacy inputs in a separate owned store, then
reconstructs eligible text candidates against a supplied new scan snapshot. New core
services never load a legacy assembly, launch an old CLI, or fall back to an old writer.

## Implemented service boundary

`LegacyImportService.Import` receives bytes, an explicit `LegacyInputProfile`, target
snapshot/locales and an explicit world-rebind acknowledgement. It never opens the old
WorldPath. An immutable version-1 report stores exact raw bytes as Base64, SHA-256,
profile/importer version, target world/snapshot, locales, and one decision per parsed
record. Unknown fields, historical notes/glossary/IDs/retry hints remain in the raw
input. Old files are neither rewritten nor deleted. Settings, notes, glossary and key
mapping are retained data; cache is RescanRequired, not trusted new state.

Automatic reconstruction currently supports the identified legacy bossbar shape:
`level.dat#Data/CustomBossEvents/{literal-key}/Name`, type `bossbar`, exact historical
`Boss 血條 ({literal-key})` context, one unmerged slot, and one matching complete current
occurrence with the same decoded original. This deliberately narrow mapping does not
guess locale-specific contexts, list alignment, missing dimension/ZIP identity, command
locations or other legacy paths. Those records retain their translations and receive
RescanRequired/Conflict. Duplicate IDs and competing records for one new slot are all
Conflict. Incomplete or scoped snapshots cannot prove uniqueness.

Legacy schemas usually lack a source hash. A missing hash stays null and is reported
separately from the verified current artifact hash. The optional explicit extension
`SourceHashKind: "raw-artifact-sha256"` identifies a supplied SourceHash; an unidentified
hash meaning is Unsupported. Changed text/hash is Conflict, never a stale mapping.

`CreateSession` verifies the durable report, re-evaluates its raw input against the
supplied opaque snapshot, and requires all records to be mapped. It uses ordinary
session start/submit/validation with one existing slot per page. No translation is
split or treated as AST, and records without a translation stay pending. Deterministic
operation IDs make interrupted projection retryable; later user operations cause a
conflict rather than silently rebasing the import. Session creation does not write the
world or establish its current preimage; G05 must check that independently.

Input is capped at 16 MiB, with bounded JSON depth/strings/tokens and 100,000 records
or scanned occurrences. Reports permit at most 4,096 total candidate addresses and
use the shared 64 MiB/1,000,000-token contract boundary. Oversized expansion fails
explicitly before publishing a partial report; the caller retains the source input.
The atomic immutable report store rejects foreign directories, preserves existing
reports, and permits same-byte retries. Flush failure and lost receipt tests cover
retention/retry; existing shared storage/session process tests cover the NTFS primitive.
This is a domain API; public CLI and candidate-release migration remain later gates.

## Explicit input profiles

Legacy inputs have no trustworthy new-product schema/version. The boundary must select
an explicit legacy input profile and report its version; it must not guess a new family
from coincidental property names. Unknown input profiles are Unsupported and retained.
The following source shapes were checked read-only; their code is not reused:

| Input profile | Source shape | Preserved data |
| --- | --- | --- |
| command-session-unversioned | `.old/Commands/TranslateSessionCommand.Types.cs` | WorldPath, timestamps, paging/unique/key preferences, Notes, Entries, KeyMapping, Glossary; entry Id/Path/Original/RawContent/Translated/Type/Context/LinkedIds/LineIndex/LineCount/RetryCount/RetryHint |
| translation-session-unversioned | `.old/Translation/TranslateSession.cs` | Common session/entry fields; absence of command-session notes/retry fields is not proof of another schema |
| batch-unversioned | `.old/Commands/BatchModels.cs` | WorldPath, operation Id/Path/Value/LineIndex, CreateBackup and ValidateBeforeWrite preferences |
| translation-reply-unversioned | Explicitly selected historical reply array | key/original/translated plus confidence/note where present; extra data remains attached |
| legacy-cache | Old scan indexes or MemoryPack cache | Raw input bytes/hash and provenance only; requires a new scan, never a trusted snapshot |

An input that matches multiple unversioned shapes remains ambiguous until its profile is
explicitly identified. Required field absence, duplicate object properties, oversized
input and unrecognized structures are reported; the importer must not silently turn them
into empty sessions or zero operations. WorldPath is historical provenance, not authority
to open another directory or establish the new world identity. Current world/root binding
must come from the requested import target, including an explicit rebind/fork decision.

## Classification before generating any operation

| Classification | Required evidence | Result |
| --- | --- | --- |
| UniquelyReconstructable | Identified input profile; complete fresh target snapshot; exactly one complete artifact/entry/dimension/chunk/typed-node candidate satisfying original text, type and context; any recorded legacy source hash still matches | Preserve old ID and source provenance; create a new occurrence/slot mapping bound to the fresh snapshot and current raw hash; translated text remains a candidate requiring normal validators |
| RescanRequired | No trusted current snapshot, missing location/type/grammar evidence, no candidate, or legacy cache | Retain all original data; request a bounded fresh scan or missing explicit information; produce no writable mapping |
| AmbiguousRejected | More than one candidate; duplicate/conflicting legacy IDs or replies; same basename across dimensions/packs; inconsistent explicit bindings | Retain all candidates/reasons; report conflict, never select first/last or pair by enumeration order |

Source changes against a recorded legacy preimage are Conflict and cannot retain a
previous writable mapping. They require a new rescan/reconstruction revision, preserving
the old decision. Unsupported profiles and malformed inputs remain separately reported;
they are not silently counted as successful RescanRequired mappings.

A display path, old ID, worker counter or line index alone is insufficient. Exact archive
entry and outer pack must be reconstructed independently; basename lookup cannot invent
`resources.zip` or assume the overworld. List indices belong to their source revision;
UUID/coordinates may generate explicit rebase candidates but never silently relocate an
operation. A scan with missing/corrupt sources cannot prove uniqueness by omission.

The legacy types above often do not contain a source artifact hash. Do not invent one.
Preserve `legacySourceHash = null` with a reason, the full legacy input hash, and the fresh
verified source hash separately. A new unique mapping needs full reconstruction and
current text/type/context evidence; it cannot claim that a missing historical hash was
verified. A supplied legacy hash must be checked under its identified encoding/meaning,
not reinterpreted as a new TextRevision or a stable ID digest.

## Candidate conversion and retained provenance

For each legacy record, retain input-profile/version, raw input SHA-256, old record index
and ID, original path/value/type/context/raw content, any original source hash and its
meaning, candidate complete addresses, target snapshot ID and source hashes, decision
and reason. Preserve translated text, progress, notes, glossary, key mapping, linked IDs,
retry hints and unrecognized fields/attachments. Lossless raw input retention protects
data that cannot yet be mapped. Never rewrite or delete the legacy originals.

Only decoded translated text may become a SlotReply. Recompute the new slot ID/revision,
check locale, exact slot set, allowed-empty and placeholder rules, then use the same
LiteralTranslation/KeyConversion validators used by the normal import/apply flow. An old
RawContent or translated AST cannot authorize changes to styles, events or UUIDs.
LinkedIds and matching original text do not prove reference-group completeness or require
all contexts to use the same translation. KeyMapping is candidate provenance, not proof
that a pack/lang dependency has been created or validated.

Batch Value is historically a string without a guaranteed typed interchange schema.
Resolve the target's actual type and apply an explicit typed-value grammar/profile before
generating TypedEdit. Ordinary type/value properties are not wrappers. Translation reply
input cannot obtain TypedEdit authority merely because the new product has that PatchKind.
Legacy CreateBackup/ValidateBeforeWrite preferences cannot disable required preconditions,
staging validation, journal or recovery evidence. Old backup directories must pass the new
backup verifier before an ArtifactRestore authorization can reference their manifest.

The report preserves one decision per input record, with totals for mapped, rescan,
conflict, unsupported and rejected records. Totals equal requested records; partial or
unresolved mapping is not whole-input migration success. Re-import of the same raw input,
profile version and target snapshot is idempotent. A changed snapshot creates a new
mapping revision instead of overwriting past provenance. G04 must test durable CAS,
interruption/retry and preservation of original files; G08 repeats against the candidate.

## Frozen evidence and consumers

The G00 fixture `fixtures/legacy-user-data/.nbtcli_translate/session.json` contains two
`duplicate-id` entries with the same display path/original and different translations.
It must classify as AmbiguousRejected; last-writer-wins is forbidden even if a fresh scan
has only one matching physical node. The adjacent synthetic `translated.json` deliberately
has a nonstandard shape and must remain retained/Unsupported until a profile is identified.
`key-mapping.json` preserves candidate translations and cannot alone authorize pack writes.

G02 and G04 consume the same versioned fixtures in `fixtures/contracts/`, not regenerated
copies with separate expectations. G02 uses the typed edit/address/plan values with its
new codecs; G04 uses slots, reply preconditions and plan/receipt bindings with synthetic
occurrences. Both must reject unknown versions and malformed/mutually exclusive states.
A schema change must update the shared fixtures, both consumers, canonical vectors and
the owning version before acceptance; no hidden fallback to old DTOs is allowed.

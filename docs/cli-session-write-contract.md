# Session plans and verified completion

The public direct-translation workflow keeps the full text structure local:

1. Use the [session file exchange](cli-session-contract.md) to validate every reply.
2. `translate plan <world> --session-id <guid> --revision <n> --direct --output <new-plan.json>` previews the exact literal PatchPlan.
3. `apply <world> --plan <plan.json> --world-offline` uses the existing world engine.
4. `translate seal <world> --session-id <guid> --revision <same-n> --operation-id <retained-guid> --plan-hash <hash>` records completion after durable proof.

All commands accept the same explicit `--state-root` and global `--ui-locale`.
Use the same state root throughout. `translate plan --help` and
`translate seal --help` expose their actual flags. Plan and seal also accept `output`
and `help`; existing destinations and world-local tool-state/export paths are
rejected before changing session state. These two actions do not write world
content. World writes in this workflow use the existing `apply`/`recover` commands, with their offline
acknowledgement, writer lease, staging, backup, readback and nine-state receipts.

This is the explicit replacement for the old `translate apply --direct` workflow.
The new product separates preview, world commit/recovery and session completion.
There is no old executable fallback or inferred write from a session reply.
[Key planning](cli-key-plan-contract.md) uses explicit `--keys` and declares its
required language/pack outputs. Canonical `plan` always requires one mode. The
old `apply`/`apply-keys` names are preview aliases with explicit migration data;
global `apply` performs the world commit. [World conversion](cli-convert-contract.md)
has its own explicit source selection; standalone resource export remains integration work.

Plan requires the exact current session revision and every slot to be Translated
or Unchanged. Pending, conflict or already-applied slots require inspecting or
resetting the session. The selected source scope is rescanned/verified by the
shared session query, with the same profile and completeness requirements. A
changed snapshot conflicts and preserves every accepted reply; start a new
session from current sources rather than rebasing stored translations silently.

In ordinary direct mode, each selected slot becomes one LiteralTranslation, including explicit Unchanged
replies with their original text. Existing format adapters keep unchanged source
images byte-for-byte. Field names, styles, events, UUIDs and unselected tree fields
are outside this authorization. Changed identifying entity/armor-stand names
require explicit [reference synchronization](cli-reference-contract.md) via
`--synchronize-references`; the default literal mode continues to refuse them.
Proven name groups replace their individual literals without duplicating members.
An optional plan `--operation-id <guid>` fixes the
operation-ID namespace; otherwise the preview returns a generated ID. Retain
the exported canonical `nbtcli.next.patch-plan` and its plan hash. An exact
preview retry can use the same operation ID while sources and revision match.
A new ID creates a fresh explicit attempt after terminal rollback, which does
not reuse a rolled-back journal as a forward success.

Apply can be retried from that file without regenerating a plan from a partially
changed world. Use `status --plan-hash` or `recover --plan-hash --world-offline`
after interruption; rollback retains its distinct exit 8. See the
[editing contract](cli-edit-contract.md) for actual world results. Translations
remain unapplied in the session until seal validates the current durable journal,
its bound plan/prepared images, every promised operation/output and dependency,
the exact source/revision, and all translated slots. A successful subset receipt
cannot seal omitted work. Partial, failed or rolled-back results preserve replies.

The generator includes explicit no-op operations for Unchanged slots. The seal
entry point also accepts other valid plans under the existing G05 contract:
every Translated slot must be covered, while Unchanged means no translation was
requested and may be omitted without inventing a write. Its retained reply stays
null. Applied session counts include these completed no-write decisions; they
are distinct from the world receipt's requested operations and their counts.

Seal requires a caller-retained operation ID and the original revision. Exact
retries replay the same completion after restart without another revision; a
changed proof/revision or borrowed operation ID conflicts. The response projects
completion identity/hash/revisions/kind/count and session status. It does not
export the retained internal completion input/AST. Original reply inputs and
the complete proof remain archived in the local session repository.

Missing proof returns exit 5 and `repair=inspect-apply-status-and-retry-seal`.
Incomplete/rolled-back journals return the engine's actual result and exit 7/8,
with the same repair instruction. Incompatible session/proof bindings use exit 3.
Session-store I/O uses exit 5 / `SESSION_SEAL_IO`, retaining session ID, original
revision, operation ID and plan hash with `repair=retry-same-seal`. Resolve I/O
and resend those exact values; the response does not assert that persistence
failed before publication. A completed world apply is never rolled back merely
because session bookkeeping or a later export failed.

After completion, `translate status` reports sealed session history with
`sourceFresh=null`, `freshness=not-checked`,
`validationScope=sealed-session-history`. It does not reclassify the expected
world edits as stale untranslated work or claim to have checked current world
postimages. Inspect world status/validation separately. Unsealed session status
retains its existing current-source comparison and conflict behavior.

Verification includes public CLI pure datapack/mixed/unchanged workflows, native
blocked session publication and a controlled application-engine replace failure
followed by public recover/seal. The latter uses a test fault seam, not a killed
CLI process. These cases do not establish arbitrary kill windows, Minecraft,
power-loss, RSS/performance or release acceptance.

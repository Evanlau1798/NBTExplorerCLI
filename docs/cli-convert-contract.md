# Verified world conversion

`convert <world> --lang <locale>` previews key conversions for the selected world
text. `--lang` is explicit and independent of global `--ui-locale`; the tool does
not guess the language from display text. It accepts a lowercase resource filename
of at most 64 characters. `--prefix` defaults to `nbt` and gains one trailing dot
when needed. Its remaining validation is the [key planner](cli-key-plan-contract.md).

| Option | Meaning |
| --- | --- |
| `keepdup`, `-k` | Keep a distinct key for every physical occurrence. |
| `no-backup` | Omit only the additional full-world convenience backup. |
| `language-output` | Exact world-relative language artifact, optionally with a ZIP entry. |
| `path`, `dimension`, `region`, `types`, `match-pattern` | Select source scope and text through the shared query/parser. |
| `workers` | The shared bounded scan worker count, 1–8. |
| `apply`, `world-offline` | Explicitly commit through the ordinary world engine. |
| `state-root`, `output` | Existing world-state repository and new external plan/receipt export. |
| `verbose`, `-v` | Human diagnostics on stderr; stdout remains machine JSON. |

The default language dependency is
`resources.zip#assets/nbt/lang/<locale>.json`. A new resource ZIP or a supported
existing pack/directory language uses the same profile checks, collision rules
and complete candidate set as session keys. `output` exports the raw PatchPlan
for a preview; it does not name the language file. Applying exports the ordinary
receipt instead. Existing export destinations are preserved.

Conversion uses each selected original string as its validated value; it performs
structural key conversion rather than machine translation. The existing literal
percent adapter preserves visible percent text through the language pattern and
component argument. Language tables and resource metadata are excluded from the
gameplay conversion inputs and counted as `excludedResourceText`; they remain
required dependencies where declared. All other selected text must have a legal
exact component root. Unknown/non-component shapes or physical list promotions
are refused; use explicit scope rather than silently skipping unsupported data.
Identifying entity names cannot borrow key authority. Their refusal preserves the
world and directs the user to the typed reference workflow.

Without `keepdup`, a stable memory identity shares keys only across identical
source/target locales, profile, role, context, placeholder signature and source
text. Same-spelled different contexts retain different keys. No worker counter or
enumeration position becomes a key. Shared language targets must declare identical
locale and content; any disagreement is rejected before preparation. Each physical
component still has its own operation and receipt result, even when keys share a
single language entry. Requested operations and unique language-key counts are
different quantities.

Source coverage must be complete for the requested scope. The supplied snapshot
is reused for planning; the exported plan binds its scope in the canonical
SnapshotId. Global `apply` decodes the validated snapshot scope before rebuilding
the same plan, without widening it to All. Original preconditions still detect
changed selected sources, and unselected name/artifact bytes stay protected.
Existing output dependencies outside a selected `path` are discovered separately,
including directory pack metadata and ZIP entries. They join the exact preconditions
without becoming additional gameplay inputs. Changing a bound dependency after
preview refuses apply before any component is written.

By default a committing conversion first creates a verified full-world convenience
backup and reports `convenienceBackupHash`. `no-backup` omits that copy; mandatory
raw preimages, staging, journal, readback and recovery always remain enabled. A
failed backup or preparation does not begin world commits. Preview never writes
world content. Keep the exported plan and use global `apply`/`status`/`recover`
for the same journal after interruption. No session is implicitly completed.
If apply or its following status read fails during journal I/O, exit 7 retains
the plan hash and actual world/state-root under `data.result.recovery`. Query
`status` with those values before `recover`; a failure before journal publication
may leave no journal. Do not start a new conversion to retry an interrupted one.
Any returned receipt is preserved, but `receiptIsTerminal` remains false until
the journal state can be confirmed. Unconfirmed operation counts are separate
from durable receipt history and never claim that all world files were untouched.

The command returns the ordinary projection under `data.result`, conversion
settings/counts under `data.conversion`, and the optional convenience backup hash.
Empty eligible input is an honest zero-operation plan. Existing conflict,
unsupported, resource and recovery exit classifications remain in force. A new
conversion after a successful apply is a new scan/intent; it is not a replay of
the earlier retained plan.

Evidence includes actual CLI mixed conversion, context-aware sharing/keepdup,
locale separation, both backup preferences, name refusal and scoped plan/apply
with unchanged outside artifacts. Core controls cover conflicting shared values
and decoding all four canonical scope sets. This does not establish performance,
RSS, arbitrary kill/OS races, power loss, Minecraft or release acceptance.

# Public CLI protocol and command registry

The machine-readable [command surface](cli-command-surface.json) freezes every
registered root command and action, its accepted options and the detailed input
contract. It is a product contract checked against actual help, not an acceptance
ledger. [Migration mappings](cli-migration.json) explain retained aliases and
breaking changes. Individual use cases and schemas remain in the linked contracts.

The surface's `rootAliases` includes `-h`/`--help` and `-v`/`--version` as well as
retained legacy command names. `optionAliases` lists every explicit short/long
flag alias with its command/action scope and accepted option key. For example,
root `-v` means version, while `convert -v` means verbose. `--merge-existing` is a
resource-service equivalent of `--merge`; both spellings remain visible in help.

Every invocation writes exactly one newline-delimited UTF-8 JSON envelope to stdout:

```json
{"schemaFamily":"nbtcli.next.cli","schemaVersion":1,"success":true,"command":"version","outcome":"Success","data":{},"error":null}
```

`data` above is illustrative; version returns the product and actual informational
version. `help` returns usage, the registered command list, selected subject/action,
available actions and exact accepted options. Help and version require no world or
state. Aliases emit canonical machine commands. Invalid command names and flags,
repeated flags, missing values, invalid numeric bounds and incompatible selectors
fail with exit 2 before executing the use case. Paths with spaces/non-ASCII text
remain single arguments; `--` ends option parsing. Only documented short aliases
are accepted. Value flags accept both `--name value` and `--name=value`; boolean
flags accept no value. Input length and schema bounds remain mandatory.

`--ui-locale zh-TW` (default) or `en-US` can appear before or after the command.
It selects error messages independently of source/target language or resource
locale. Machine commands, schema keys, codes, data, counts and options are locale
independent. Unknown message codes have a localized generic fallback. Technical
error details and source text are not translated. JSON escapes preserve Unicode;
decoded strings and typed interchange retain the source's exact UTF-16 semantics.
The current CLI emits no normal progress text. Any future human progress belongs
on stderr, never mixed into stdout. Diagnostics are bounded; result IDs/text and
per-operation receipts are never silently truncated.

| Exit | Meaning |
| --- | --- |
| 0 | Complete success, including an actual empty query or verified no-op. |
| 1 | Unexpected internal failure. |
| 2 | Invalid command, option, argument or required selector. |
| 3 | Conflict, ambiguous address or changed binding. |
| 4 | Unsupported source/profile or incomplete discovery. |
| 5 | Read, malformed/invalid input, denied access, preparation, export or apply failure. |
| 6 | Declared resource bound or pattern timeout. |
| 7 | Recovery required; retain exact plan/world/state identity from the response. |
| 8 | Terminal rollback, not a successful forward apply. |
| 130 | Cancelled receipt; this does not promise graceful process Ctrl+C recovery. |

Nonzero outputs have `success=false`, a stable `error.code`, localized `message`
and bounded optional `details`. Use the detailed contract and `error.code` to
distinguish cases sharing an exit. Exports may fail after a completed apply; the
failure response preserves actual operation data and cannot imply the world stayed
unchanged. A replay uses the same durable receipt rather than adding counts again.

World writes use the shared planner/stage/verify/commit engine, require explicit
apply and `--world-offline`, and return immutable versioned per-operation receipts.
All nine outcomes (`applied`, `unchanged`, `skipped`, `failed`, `conflict`,
`unsupported`, `cancelled`, `recoveryRequired`, `rolledBack`) have counts summing to
the requested operation count. A forward success requires every requested operation
Applied/Unchanged **and** complete dependencies and a confirmed completed journal.
An all-postimage result with pending dependencies is still recovery-required.
An unfinished journal/receipt is not terminal even when one physical image matches.
Rollback uses journal direction and complete preimage proofs, including unchanged
members. Pre-commit validation results are explicitly separate from durable receipts.

Session requests/replies, typed NBT, operation sets, plans, receipts, legacy reports,
questionnaires and manifests use their own family/version; they are never inferred
from arbitrary JSON or display output. File/stdin admission is as follows:

| Input family | Entry and input selection |
| --- | --- |
| `typed-nbt` | `set --value JSON` or `--value-file FILE|-`. |
| `operations` | `batch FILE|- --world DIR`, or `plan DIR --request FILE|-`. |
| `patch-plan` | `apply DIR --plan FILE|-`; receipt/hash recovery remains `status`/`recover`. |
| Session reply/command | `translate next/submit --reply FILE|-`; navigation/metadata/reset use `--request FILE|-`. |
| Questionnaire/agent answers | `agent setup` exports a questionnaire; `agent submit --request FILE|-` validates and retains the raw answers. |
| Legacy raw input | Explicit `import-legacy --input FILE|- --profile ...`; legacy batch requires `--legacy` and a retained migration report. |
| Backup manifest | `backup` returns its verified hash and optionally exports/names the raw manifest; `restore --manifest-hash HASH` resolves retained verified objects. |

Read-query display JSON is inspection output and cannot authorize writes. Resource
generation's `--output DIR` and canonical `--plan-output FILE` are distinct; use
returned `commitArguments` when logical output and physical root differ. Session
management writes tool state under atomic/CAS ownership; it does not silently apply
world content or seal unfinished outputs. Refer to the detailed contracts for
required bindings, limits and actionable retry paths.

Developer-only old commands are replaced by the standalone verification harness;
they are excluded from the product command registry. This CLI contract does not
claim performance, arbitrary power-loss durability, live Minecraft acceptance or
release readiness.

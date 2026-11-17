# Generic editing, durable apply and raw restore

These entry points use the same new planner, preparation, verified backup and
durable apply/recovery services as the application API. They never invoke the old
product. File-based session/agent management has separate CLI contracts; world translation
and the legacy batch importer remain separate integration work.
This document covers the new versioned operations input.

| Command | Input and options |
| --- | --- |
| `set <world>` | `path`, one node selector (`pointer`, `segments` or unique legacy node path), `chunk`/`dimension` for regions, `compression`, one of `value`/`value-file`, `operation-id`, `apply`, `world-offline`, `state-root`, `output` |
| `batch <file-or-dash>` | `world`, `apply`, `world-offline`, `state-root`, `output` |
| `plan <world>` | `request`, `state-root`, `output` |
| `apply <world>` | `plan`, `world-offline`, `state-root`, `output` |
| `status <world>` | `plan-hash`, `state-root`, `output` |
| `recover <world>` | `plan-hash`, `world-offline`, optional `rollback`, `state-root`, `output` |
| `backup <world>` | Optional exact physical `path`, `state-root`, `output` |
| `restore <world>` | `manifest-hash`, optional exact physical `path`, `apply`, `world-offline`, `state-root`, `output` |

`--ui-locale`, `--help` and `help <command>` retain the common protocol. World paths
with spaces or non-ASCII names are ordinary single arguments. `--state-root` must
be outside the selected world. Its dedicated cache/object/journal repositories
keep atomic ownership and CAS checks; they do not overwrite unowned contents.
The default is `%LOCALAPPDATA%/nbtcli-next/worlds/<scope-id>`.

`set`, `batch`, `plan` and `restore` produce a preview by default. A preview returns
`data.preview=true` and a complete `nbtcli.next.patch-plan` under `data.plan`.
`set`/`batch`/`restore --apply` and the `apply`/`recover` commands require
`--world-offline`. This is the caller's explicit declaration that Minecraft and
other writers have closed the world; the internal writer lease still arbitrates
concurrent tool writers. The CLI does not start Minecraft.

`set` replaces exactly the selected physical NBT value. `--value` is a complete
version 1 `nbtcli.next.typed-nbt` JSON document; `--value-file` reads the same UTF-8
contract from a file or `-` for stdin. Its root value is the replacement; the
artifact's existing root name is retained. A plain untyped JSON/string value or
the old ambiguous type inference is not accepted. `--operation-id` defaults to
`edit-0`. Other physical values, widths, ordering and unselected artifacts remain
protected by exact candidate verification.

`batch` and `plan --request` accept UTF-8 version 1 `nbtcli.next.operations`:

```json
{
  "schemaFamily": "nbtcli.next.operations",
  "schemaVersion": 1,
  "data": { "operations": [] }
}
```

The displayed empty array is a shape example; actual requests require 1–100000
operations with distinct IDs. Each operation uses the existing `id`, `kind`,
`payload` contract from the [operation model](operation-contracts.md). A typed
payload contains a versioned source `address` and a versioned `typed-edit`.
Addresses can be obtained from `get`; expected values are explicit preconditions.
Literal, key and reference intents retain the existing G05 validators. Raw restore
intents use `restore` or an exported raw restore plan rather than this semantic
request path. Unknown fields, duplicate members, unsupported families/versions,
empty requests and invalid bindings are rejected. `batch - --world <world>` reads
the same request from stdin. Set and batch resolve the same typed intent through
the same planner. Legacy batch JSON has not yet been wired to a migration adapter.

`--output` creates a new file outside the world and preserves existing files.
For previews it exports the raw patch-plan family, usable by `apply --plan`.
For backup it exports the raw backup manifest; for apply/status/recover it exports
the raw apply-receipt family. Stdout always remains the CLI envelope. These
raw exports preserve the exact codec/prompt content without an added newline;
CLI-envelope exports and stdout remain newline-delimited. Existing
destinations are checked before a world write, and publication still checks
absence atomically. If a later export fails, stdout retains actual operation data
with a nonzero export error; the export result must not conceal a completed apply.

Apply receipts appear under `data.receipt`, including their own family/version
and `data`. Nine outcome counts sum to the requested operations; physical outputs
are not operation counts. Partial apply uses exit 7 / `RECOVERY_REQUIRED`, and
`recover` resumes the persisted plan using actual pre/postimages. Resubmitting an
already journaled plan returns its persisted result. Rollback uses exit 8 /
`ROLLED_BACK`; terminal rollback is determined by the persisted direction/phase,
including mixed unchanged/rolled-back and all-unchanged plans. It is not a
successful forward apply. `data.direction`, `phase`, `sequence`,
`backupManifestHash` and `receiptIsTerminal` describe the current journal.
An older receipt during an unfinished journal cannot imply success; a journal
without a receipt still returns structured recovery state. A fresh operation identity is
needed for a new apply after terminal rollback. Receipt `Cancelled` uses exit 130;
this does not claim graceful Ctrl+C handling has been validated. Other common
exit codes retain their [query meanings](cli-query-contract.md).

Before a plan or candidate can be completed, rejected valid intents appear as
`data.validation`, with `phase=PlanRejected` or `PreparationRejected`, all intents
`Skipped`, and the nine counts. This is a pre-commit validation result, not a
durable apply receipt or proof of any write. Malformed input that cannot establish
a valid intent set returns the ordinary error envelope. Input admission also
checks the aggregate escaped size of this rejection response. If the complete
ID set cannot fit, it returns exit 6 / `RESOURCE_LIMIT` before planning, with no
validation records; IDs and translated content are never truncated.

Before any world commit, preparation checks the worst-case durable receipt
(including every ID, proof, count, revision and bounded reason) against the
64 MiB byte limit and reader depth/token budgets, with 64 KiB reserved for the
CLI wrapper/export error. Journal headers also have to pass their reader budgets
before publication. Over-budget requests fail before the first replacement.
Engine receipt reasons are nonempty when required and at most 256 UTF-16 code
units; CLI error details are at most 4096. Only diagnostics are abbreviated.

Backups contain verified immutable raw blobs and manifests. Without `--path`,
backup selects the physical inventory excluding tool-reserved `.nbtcli-*` names;
an incomplete or resource-limited catalog cannot produce an apparently completed
backup. `restore` verifies the manifest/blobs before planning, binds current raw
images, then stages every target before commit. Missing/corrupt targets need not
pass a text or NBT parser. Only manifest-authorized artifacts are selected.

World IDs here use the existing NTFS directory scope binding. They are not portable
world UUIDs. A byte-identical other root cannot consume a plan or backup as the
original root; directory/volume replacement requires new authority. Cross-root
backup rebinding is not provided by these entry points. Minecraft load, publishing,
RSS/performance and power-loss validation remain later gates.

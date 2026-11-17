# Generic editing, durable apply and raw restore

These entry points use the same new planner, preparation, verified backup and
durable apply/recovery services as the application API. They never invoke the old
product. File-based session/agent management has separate CLI contracts;
[direct session plans and completion](cli-session-write-contract.md) use these
same apply/recover commands. Explicit [legacy batch migration](cli-legacy-batch-contract.md)
maps safely reconstructed old requests to these operations. [Session key plans](cli-key-plan-contract.md)
generate exact conversion operations and their necessary language/pack outputs.
[World conversion](cli-convert-contract.md) reuses those operations without a session.
This document covers the new versioned operations input.

| Command | Input and options |
| --- | --- |
| `set <world>` | `path`, one node selector (`pointer`, `segments` or unique legacy node path), `chunk`/`dimension` for regions, `compression`, one of `value`/`value-file`, `operation-id`, `apply`, `world-offline`, `state-root`, `output` |
| `batch <file-or-dash>` | `world`, optional `legacy` with required `migration-report`, `apply`, `world-offline`, `state-root`, `output` |
| `plan <world>` | `request`, `state-root`, `output` |
| `apply <world>` | `plan`, `world-offline`, `state-root`, `output` |
| `status <world>` | `plan-hash`, `state-root`, `output` |
| `recover <world>` | `plan-hash`, `world-offline`, optional `rollback`, `state-root`, `output` |
| `backup <world>` | Optional exact physical `path`, `suffix`, `state-root`, `output` |
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
An exported semantic plan retains its canonical snapshot scope; `apply` rebuilds
that exact scope rather than assuming an unfiltered scan.
Literal, key and reference intents retain the existing G05 validators. Raw restore
intents use `restore` or an exported raw restore plan rather than this semantic
request path. Unknown fields, duplicate members, unsupported families/versions,
empty requests and invalid bindings are rejected. `batch - --world <world>` reads
the same request from stdin. Set and batch resolve the same typed intent through
the same planner. Legacy batch JSON requires the explicit migration mode and report;
it is never inferred from an unversioned request.

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

Apply/recovery I/O failures may occur after physical commits. Journal lookups in
`apply` replay and `status` use the same protected error response. Exit 7 /
`RECOVERY_REQUIRED` preserves `planHash`, `recovery.world`, `recovery.stateRoot`
and a machine repair action. Use these exact values with `status` then `recover`;
do not infer that the world is unchanged. Before journal publication there may
be no journal, indicated by `journalMayBeAbsent`. Inspect the retained plan and
current sources before replanning in that case. A definitively absent journal
on initial recovery retains exit 5 / `FILE_NOT_FOUND` and the supplied identity.
Other missing persistence objects after execution begins remain unconfirmed.
A successfully returned durable receipt survives a subsequent status read
failure, but `receiptIsTerminal` is
false until the current pointer can be confirmed. When no receipt is available,
`unconfirmedOperations` conserves all parsed IDs as RecoveryRequired; these are
observations, without a fabricated durable revision. Recovery by hash alone may
have no parsed operation set and leaves that projection null. Recovery identity
is bounded before engine execution; results and IDs are never truncated.
Known I/O failures aggregated during native candidate cleanup use this same
recovery response. A saved receipt is read once when available, and retained
alongside the cleanup failure. A reference-policy refusal still reports exit 4
when its status is readable; if that status read fails, exit 7 retains both the
recovery identity. Both `recovery.repair` and `policyRepair` then direct explicit
rollback and replanning; forward resume remains refused.

A cleanup I/O failure can leave reserved `.candidate` files containing derived
output bytes. Recovery repairs the promised world artifacts; it does not collect
those old candidates or immutable history. They retain their filesystem access
controls and may consume disk space. Automatic garbage collection is not provided.

Backups contain verified immutable raw blobs and manifests. Without `--path`,
backup selects the physical inventory excluding tool-reserved `.nbtcli-*` names;
an incomplete or resource-limited catalog cannot produce an apparently completed
backup. `restore` verifies the manifest/blobs before planning, binds current raw
images, then stages every target before commit. Missing/corrupt targets need not
pass a text or NBT parser. Only manifest-authorized artifacts are selected.

`backup --suffix <name>` also publishes an immutable named raw manifest directly
under the returned `stateRoot`: `backup_<name>_<UTC timestamp>_<unique ID>.json`.
`data.backupPath` identifies it and `namedManifestPublished` confirms publication.
The suffix is one nonblank safe filename segment, at most 64 UTF-16 units;
separators, control characters and Windows-ambiguous trailing dots/spaces fail
before state publication. Each invocation gets a new name; the manifest hash and
content-addressed blobs remain shared. `--output FILE` independently exports the
same manifest and preserves existing destinations. Without a suffix, no named
reference is created and `backupPath` is null. Naming failure returns nonzero
`EXPORT_FAILED` with `namedManifestPublished=false`, the verified manifest/hash and
state root still available; it does not invalidate already verified objects.

This replaces the old sibling directory copy: retain the named manifest **and**
the state's `objects` repository. A manifest alone is an index, not a portable
copy of world contents. Restore with its `manifestHash` and the same `--state-root`;
restoration still uses the verified planner/apply path. Naming never disables
required preimage backups or journals. Migration mappings are machine-readable in
[cli-migration.json](cli-migration.json).

World IDs here use the existing NTFS directory scope binding. They are not portable
world UUIDs. A byte-identical other root cannot consume a plan or backup as the
original root; directory/volume replacement requires new authority. Cross-root
backup rebinding is not provided by these entry points. Minecraft load, publishing,
RSS/performance and power-loss validation remain later gates.

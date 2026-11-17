# Explicit legacy batch migration

`batch <file-or-dash> --legacy --world <root> --migration-report <new-file>`
accepts the unversioned `BatchModels` input described in the
[capability ledger](capabilities.md#舊-batch-欄位映射cap07).
The default batch command continues to require versioned operations.

The report destination is mandatory, must be outside the selected world and must
be distinct from `--output`. Existing destinations are preserved. File inputs
are read without modification; stdin uses the same bounded reader. The adapter
retains exact raw bytes, including a BOM, as `rawInputBase64`, their SHA256, world
binding, preferences, diagnostics and every parsed mapping in version 1
`nbtcli.next.legacy-batch-report`. The CLI returns input/report hashes and counts;
the report is the full migration record. Keep its supplied path.

`WorldPath` is required and must resolve to the explicit `--world` root. Known
field names are case insensitive; case-insensitive duplicate fields are rejected.
Missing/empty operation sets are refused. Unknown fields are retained in the raw
input and reported as `LegacyUnknownField`; they do not select behavior.

| Legacy field | Current meaning |
| --- | --- |
| `Id` | Provenance only. Generated `legacy-N` operation IDs are unique even when old IDs repeat or are absent. |
| `Path` | Must resolve to one exact artifact/dimension/chunk/typed node. Missing or ambiguous targets refuse the whole batch. |
| `Value` | A string target receives the exact original string, including JSON-looking text. A non-string target requires a complete versioned typed-NBT document inside this string. Arbitrary JSON wrappers and automatic type inference are refused. |
| `LineIndex` | Retained; `LegacyFieldUnused`. It cannot change the locator. |
| `CreateBackup` | Defaults to true; an applying import first creates a verified full-world convenience backup through the existing backup service. Its manifest hash is returned as `convenienceBackupHash`. False omits this extra copy. |
| `ValidateBeforeWrite` | Retained; `LegacyFieldUnused`. Validation always remains mandatory. |

Every applying plan still creates its mandatory verified raw preimages and
journal, including when `CreateBackup=false`. Backup creation failure prevents
world writes. A preview does not create the convenience backup or mutate the
world; it does create the explicit migration report outside the world.

Supported old locators are DAT `relative.dat#Data/Node`, region
`region/r.x.z.mca/Chunk[x,z]/Node`, and entity
`entities/r.x.z.mca/[x,z]/Entities/[n]/Node`; `.mcr` is also accepted.
Windows separators and inline indices such as `Entities[0]` are accepted. A bare
region filename requires exactly one match in a complete physical catalog;
terrain/entity/dimension collisions are conflicts. Exact paths use the world's
discovered layout for dimension binding, and chunk coordinates must belong to
the named region. Paths containing separator/index syntax that also forms a
literal compound key refuse rather than choosing an interpretation. Use fresh
versioned addresses for those cases, archive entries, native JSON or other old
shapes whose complete binding cannot be recovered.

All mappings must succeed before planning. One rejected mapping prevents every
world write; other mapped rows remain proposals, never applied receipts. The
report retains per-row decisions/reasons, while the CLI gives skipped operation
accounting and a repair instruction. Raw input is limited to 16 MiB and reports
to the existing 64 MiB contract/storage budget. Inputs exceeding the raw bound
are refused before retention. A report or source I/O failure never becomes a
successful migration; report publication must finish before world preparation.
When the raw input fits but decoded JSON exceeds its node/string/depth budget,
the adapter retains the raw report and returns ResourceLimit without mappings.

After successful mapping, the adapter creates ordinary TypedEdit operations and
uses the same [planner, preparation and durable writer](cli-edit-contract.md).
The CLI wraps the usual projection under `data.result`, with `data.migration`
and `data.convenienceBackupHash`; a preview's `--output` is still a raw patch-plan
usable by `apply --plan`. Applying requires both `--apply` and `--world-offline`.
Recover an interrupted apply using its retained plan hash and the normal
`status`/`recover` commands, not by treating a newly imported proposal as the old
journal. Existing report paths are never overwritten for retries.

Mapping conflicts retain exit 3; unsupported inference/bindings use exit 4;
source I/O or malformed value documents use exit 5; resource limits use exit 6.
Argument errors use exit 2. The normal apply/recovery projection retains its
existing nine outcomes and exits. Machine codes are independent of UI locale.

Evidence includes actual CLI processes, NTFS report/input protection, native
source locking, DAT and region changes, unselected chunk preservation, input
limits, provenance/preferences and shared writer regression. It does not cover
arbitrary process kills, power loss, Minecraft, RSS/performance or release.

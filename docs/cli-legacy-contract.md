# Read-only legacy import and session projection

`import-legacy <world>` exposes the existing conservative importer. It reads
explicitly selected legacy bytes and the current target world, retaining an
immutable report in a separate owned repository. It never opens the historical
WorldPath, loads old assemblies, runs an old executable or applies world edits.

Choose exactly one mode:

| Mode | Required and optional options |
| --- | --- |
| New report | `--input <file-or-dash>`, `--profile <name>`; `source-language`/`target-language` unless confirmed agent defaults exist; optional `acknowledge-world-rebind`, `create-session`. |
| Stored report | `--import-id <sha256>`; optional `create-session`. Stored profile, locales and rebind acknowledgement cannot be replaced. |
| Recovery index | `--list`; optional `limit`/`offset`. Lists opaque candidate import IDs in the selected repository without reading report bodies or world contents. No projection or replacement import options. |

Both modes accept `state-root`, `output <new-file>`, `limit`, `offset`, `help` and
the global `ui-locale`. State and export paths must be outside the world; existing
output and unowned repository contents are preserved. Reports live in the owned
`imports` repository under the same state root as agent/session management.

Profile names are `command-session`, `translation-session`, `reply-array`,
`glossary`, `key-mapping`, `agent-config`, `questionnaire`, `notes-text`,
`scan-cache`, `unknown`. They identify a parser explicitly; no file-name guessing
or fallback to an old runtime is performed. Raw files and stdin have a 16 MiB read
budget before allocation/decoding. Non-UTF-8 legacy bytes can still be retained in
a rejection report rather than being discarded by the CLI text decoder.

Reports use `nbtcli.next.legacy-import`, schemaVersion 1. Exact original bytes are
stored as base64 with SHA-256, profile/importer version, target world/snapshot,
locales, acknowledgement and one decision per record. The import identity binds
all those inputs; exact retries reuse the report. Existing caller files are never
rewritten or deleted. Unknown user fields and historical notes remain in raw data.

Stdout uses the ordinary CLI envelope with a paged `data.report` projection:
identity, profile, world/snapshot/input hashes, locale, decision counts, records,
reasons, candidate counts and opaque slot IDs. Raw input and full physical
candidate addresses remain in the local report. `--output` exports the complete
canonical report JSON, byte-for-byte as the codec writes it, without adding a
newline; it can be read directly by `LegacyImportJson.Read`. Stdout retains its
newline-delimited CLI protocol. Other raw codec/prompt exports also preserve the
provided content exactly; CLI-envelope exports retain their final newline.

Report-store creation/read/save I/O failures return exit 5 / `IMPORT_STATE_IO`,
the computed `importId`, underlying diagnostic code and
`repair=retry-same-import-input`. This means persistence is unconfirmed; it does
not assert that no report was published. Keep the original input and retry the
same options after resolving I/O, or look up the returned ID. If the process was
interrupted before returning a handle, `--list` finds retained reports even after
the world snapshot changes; retrieve candidate IDs to inspect their input hash/profile.
Listing checks repository ownership and filename syntax, paginates deterministic
ID order and limits directory enumeration to 100000 entries. It reads zero report
bodies regardless of offset/limit. Results explicitly declare
`scope=import-repository`, `contentsValidated=false`, `freshness=not-checked`.
Use `--import-id` for each selected handle to validate its complete report, world
binding and input hash/profile; a custom state root can contain other worlds'
handles, which fail the normal world check on lookup. No raw input or report
metadata is read or exposed by listing; missing/corrupt bodies fail on lookup.

`Mapped`, `Retained`, `Conflict`, `RescanRequired`, `Unsupported`, `Rejected` are
report decisions, not world-apply outcomes. Retention success does not confirm
agent settings, merge glossary/notes, or authorize a write. Old settings and quality
commitments stay available for explicit reconfirmation using the new questionnaire.
Old caches require a rescan. Unsupported/missing shape, dimension, ZIP identity or
alignment never becomes a guessed target. The automatic mapping remains the
[identified single-slot bossbar shape](legacy-mapping-contract.md), with complete
current coverage, exact type/context/path/original and unique candidates.

`--acknowledge-world-rebind` explicitly selects this target world for reconstruction;
the old WorldPath alone is not authority. Missing acknowledgement yields
RescanRequired. A changed original/hash or ambiguous candidate is a conflict.
Rejected records use exit 5, conflicts exit 3, unsupported/rescan-required exit 4,
resource budgets exit 6. Counts include every decision even when records are paged.
Candidate pool/fanout limits return ResourceLimit before any partial report is
published. Retained/mapped reports use success only for the stated import scope.

`--create-session` is accepted only when every record is mapped. It revalidates
the retained report and current snapshot, then uses normal session start/submit
and the shared reply validator. It does not apply translations to the world.
One imported record occupies one page; deterministic operation/session identities
allow reentry without counting again. Records without a translation remain pending.
Reentry validates each imported reply's exact bytes and kind through the normal
session service; an operation ID alone never proves that its translation exists.
Use the returned session ID with normal translate status/get/export/next commands.

A projection I/O/validation failure retains the report and returns its import ID,
`projectedSessionId`, a diagnostic code and
`repair=inspect-or-resume-import-session`. `sessionId`/status are confirmed only
after successful projection. Inspect the projected session if it exists, resolve
the condition and retry `--import-id <same-id> --create-session`. Later user
operations cause refusal rather than silently overwriting/rebasing their work.
Changed world snapshots conflict and require a new import report. Source files,
old reports and any accepted session operations remain intact.

Stored report lookup without projection marks `freshness=not-checked`; complete
means complete as of its retained snapshot, not fresh world validation. New reports
or projection queries also expose selected-world coverage/freshness. None of these
reports replace the planner's current preimage checks. Actual world apply/key/pack
work, legacy batch operations, performance/RSS, Minecraft and release gates remain
separate required work.

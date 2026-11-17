# File-based translation sessions

`translate <action> <world>` exposes the durable session service. These commands
write tool state, never world content. Direct world translation uses
[session plan, existing apply/recover and seal](cli-session-write-contract.md);
key conversion and pack generation remain integration work. Agent preferences use the
[agent contract](cli-agent-contract.md).

All non-help actions accept `--session-id <guid>`, `--state-root <directory>`, `--output
<new-file>`, `--help` and the global `--ui-locale`. Session ID is required except
for `start`, which can generate one. Retain the returned ID. State and exports
must be outside the world; existing output files are preserved. The default state
root is the same world-scoped root as the editing commands, with separate owned
repositories for sessions and cache. No legacy executable or state is loaded.

| Action | Additional options and behavior |
| --- | --- |
| `help` | No world required; lists implemented actions. `help translate` also works. |
| `start` | `source-language`, `target-language` are required unless confirmed agent defaults exist; optional `operation-id`, `page-size`, `page-bytes`, `types`, `path`, `dimension`, `region`, `match-pattern`, `standalone`, `no-group`. |
| `status` | Returns progress; unsealed work compares the current scoped world snapshot with the session snapshot. Sealed work reports history with current freshness explicitly not checked. |
| `export` | Optional zero-based `page`, reply-template `operation-id`, `no-group`. Returns page JSON, reply template, prompt and context-group projection. |
| `next` | Optional `reply <file-or-dash>`, `no-group`; persists a valid reply before returning the next pending page. |
| `submit` | Required `reply <file-or-dash>`; same import and next-page result as `next`. Optional `no-group`. |
| `get` | Required `slot-id`; returns text, validation format, current state and reply. |
| `jump` | Required `request <file-or-dash>` containing a versioned Jump command. |
| `note`, `glossary` | Read metadata with optional `limit`/`offset`; mutate with a versioned `request`. Pagination cannot accompany a mutation. |
| `reset` | Required versioned `request`; previews scope and retained history by default. `confirm` executes a command whose own `confirmed` field is true. |
| `groups` | Read context-group projections with optional `limit`/`offset`. |
| `memory` | Required `slot-id`, comma-separated `source-sessions`, `glossary-version`; optional `limit`/`offset`. Returns validated local candidates and provenance. |
| `plan` | Required `direct`, exact `revision`; optional plan `operation-id`, explicit `synchronize-references`. Exports a PatchPlan for existing `apply`; name groups require complete global authority. |
| `seal` | Required original `revision`, retained `operation-id`, `plan-hash`. Records completion only from full durable world proof. |

`start` requires complete selected-source coverage. It uses the shared verified
query service, preserving exact physical occurrences and explicit component roots.
Regex selection uses the existing 100 ms match timeout and precedes the selected
session's 100000-slot limit; over-budget selections return ResourceLimit instead
of a truncated session. Page size is 1–100000 slots;
the serialized prompt budget is 1024–67108864 UTF-8 bytes (default 65536). A single
slot that cannot fit is ResourceLimit, not an empty or shortened page. Source and
target language values belong to the session and are independent of UI locale.
Confirmed agent languages/preferred page size supply omitted options; explicit
flags override them. Updating preferences does not rewrite existing sessions.

Every occurrence has its own slot even when source text is identical. This is now
the default; `--standalone` acknowledges the former opt-in behavior. No plaintext
deduplication merges writable identities. Context groups remain available through
`groups`. At start, `--no-group` retains the display preference in a separate
owned, immutable version-1 tool-state record. Exact start replay retains it;
changing it with the same start identity conflicts. Older sessions without this
record default to showing groups. Export/next can additionally suppress an
appendix for one invocation. Preferences never change stored context groups, slot
context, page identity or validation policy. A preference-publication failure
returns the already durable start receipt/session ID and a retry instruction;
retry with the same session/operation identities rather than starting over.

Stdout always uses the common CLI JSON envelope. For `export`, `--output` writes
the UTF-8 **text prompt**, containing a `nbtcli.next.session-page` JSON document and
a `nbtcli.next.session-reply` template. It is not a standalone JSON file. An agent
returns only the reply JSON to a file or stdin. It preserves session ID, revision,
snapshot, page/pageId, locale and the exact slot set, changing only translated
state/text or explicitly retaining unchanged/null. Version-1 exchange schemas are
unchanged; they contain no writable AST or physical addresses.

`next`/`submit` return `data.receipt`, `data.status` and `data.nextPage`; nextPage
is null when no pending/conflicting slots remain. Reply publication completes
before the next page is selected. Exact input resends retain the same operation
receipt without increasing revision/counts. `CurrentPage` is the durable manual
navigation hint set by `jump`; `next` finds pending pages without rewriting it.
Use the returned next-page number for an explicit subsequent export. A newly
exported page reflects the current session revision, including metadata edits.

Metadata requests use `nbtcli.next.session-command`, schemaVersion 1, with all data
fields: `operationId`, `sessionId`, `revision`, `kind`, `page`, `slotId`, `text`,
`source`, `origin`, `confirmed`, `resetSlots`. Include null for inapplicable fields
and an empty resetSlots array for other actions. Kind is jump/note/glossary/reset
and must match the invoked action. IDs are nonempty GUIDs; mutations use exact
revision CAS. Reusing an operation ID with different input conflicts. A reset keeps
the original reply/command history and affects only the declared slots; it neither
deletes world outputs nor rolls back an apply.

Missing slots and stale revision/hash/locale are conflicts (exit 3). Duplicate
slot IDs and malformed reply structure are rejected by the codec (exit 5).
Unsupported schema/profile uses exit 4; resource/page budgets and regex timeout use
exit 6; unreadable sources keep their read-error classification. Rejected imports
do not change session work. New replies additionally compare the current world
snapshot with the retained session snapshot; a warm Fresh cache alone is not
session freshness. Source changes return Conflict with `repair=start-new-session`,
retaining the old session for lookup/export. Exact historical input replay does
not gain new write authority or require another import.

Slot/group/TM projections expose opaque digests and decoded text. Full canonical
identities, physical addresses and AST remain in local session/query state. TM
candidates require explicit source sessions, context, languages, placeholder and
glossary/profile compatibility; lookup does not submit or accept a translation.
Notes/glossary and group presentation never authorize a world rename/write.

Agent setup/submit/status and [legacy import/report](cli-legacy-contract.md) have
their own contracts. Key conversion, convert, language export and pack management remain integration work.
Those capabilities remain required. Minecraft load, RSS/performance, power loss
and release acceptance are later gates.

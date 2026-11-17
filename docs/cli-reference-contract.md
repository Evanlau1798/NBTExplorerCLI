# Explicit typed name and reference synchronization

`translate plan <world> --session-id <guid> --revision <n> --direct
--synchronize-references --output <new-plan.json>` extends the
[session world workflow](cli-session-write-contract.md). Without this explicit
flag, identifying entity/armor-stand name changes remain refused. The flag is
valid only for planning, takes no value and never applies a world write by itself.
Use the same retained plan with the ordinary offline `apply`/`recover` and `seal`.

The session must come from a complete unfiltered world snapshot. A text selection
regex can narrow requested slots while retaining that global scan scope; dimension,
region, type and artifact-path scopes cannot establish global reference authority.
Source freshness and exact session revision remain mandatory. The local service
reads actual native entity types and complete visible name parts, then creates one
ReferenceSynchronization operation per typed old/new identifier group. Fragments
of one name stay together; an unchanged sibling is retained as part of that name.
Other validated text uses normal LiteralTranslation operations.

The service changes only explicitly requested name targets and their proven typed
references. It never adds another entity just because its display text matches.
Selectors addressing unselected same-name targets, untyped/execution-dependent
domains, dynamic/unknown grammar and unsupported encoded command references refuse
the affected plan. A filtered or incomplete source set cannot supply a guessed
Complete claim. Separate typed groups cannot merge distinct old identifiers into
one final name or collide with an existing target. The common planner enforces
this even when groups were constructed independently through the application API.

Raw TypedEdit operations cannot share a plan with semantic reference groups:
their structural mutations could invalidate the verified reference namespace.
Use the generic edit workflow separately, then rescan before a reference plan.
This does not restrict ordinary TypedEdit/literal mixtures without reference
groups. Legacy raw-typed/reference combined journals have the same forward/seal
refusal and verified rollback path described below.

Direct command selectors and literal-free native JSON/NBT selector components
join the same staged physical output set. Name style, hover/event data, UUIDs,
unselected entities, ordinary same-spelled literal text and player selectors keep
their existing protection. Global string replacement is never an authority.
See [static reference analysis](reference-analysis.md) for the exact supported
grammar and conservative refusal conditions.

Member count and retained member bytes are bounded across the whole group set:
at most 100000 members and the snapshot's decoded-byte budget. Bulk construction
and common plan validation both enforce this before staging. A grouped operation
counts once in the world receipt even if it covers multiple slots/artifacts;
session Applied counts still describe completed slot decisions. These counters
serve different purposes and are not added together.

Multiple-group plans use policy `world-prepare-name-groups-v2`, binding the new
joint namespace check into the plan hash and immutable journal header. Existing
non-reference/single-group policies and their identities remain unchanged.
An older multiple-group `world-prepare-v1` plan must be regenerated; pending old
multiple-group journals cannot be rolled forward or replayed into new writes.
They return Unsupported/exit 4, the existing journal projection when available,
and `repair=rollback-and-create-new-reference-plan`.

Retain the old journal, plan and backups. Explicit `recover --rollback
--world-offline` remains available and verifies the old raw preimages; then create
a fresh session plan with current authority. Rollback retains exit 8. Completed
old receipts remain immutable historical records, and a repeat read/recovery does
not rewrite them. An old multiple-group receipt cannot issue a new session seal
under the current policy. Exact replay of an already retained completion remains
historical replay, with no new world or session write.

This versioned admission check prevents legacy recovery from bypassing the new
common guard. It does not silently migrate an old journal or discard work. New
multiple-group prepared journals retain normal forward recovery and completion.

Evidence covers actual CLI single/multiple typed groups, duplicate targets,
fragmented names, native/direct references and protected fields; common-planner
collision and aggregate-budget controls use the application API. Legacy journal
conditions are constructed from real durable prepared objects and raw images,
with a controlled stop and materialized first postimage. They are not actual
historical-process kills or Minecraft runs. RSS/performance, arbitrary kill,
power-loss and release acceptance remain separate gates.

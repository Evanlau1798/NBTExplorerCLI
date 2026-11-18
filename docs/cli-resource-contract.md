# Standalone language and pack commands

`translate export-lang <world> --session-id <guid> --revision <n>` and
`translate create-pack <world> --session-id <guid> --revision <n>` default to
preview. `--apply --world-offline` commits through the same prepared candidate,
backup, journal, readback and recovery engine as other writes.

`--output DIR` is a resource directory. `--plan-output FILE` exclusively exports
the canonical PatchPlan, including when `--apply` is selected. It never exports
a receipt in place of a plan. Existing export files are refused. Machine stdout
remains a single JSON envelope; `--ui-locale` is independent of resource locales.

The session must belong to the selected physical world, match the supplied
revision and have no pending or conflicting slots. Translated, unchanged and
already applied slots can supply frozen values. Resource export does not
revalidate the session's gameplay snapshot and does not modify, apply or seal the
session; the result retains its source snapshot identity. Original and translated
values use the same `prefix + SlotId` keys as `translate plan --keys`.

`--prefix` defaults to `map.` and has the same ASCII/128-character restriction.
`--original-lang` and `--target-lang` override the session's locale labels for
resource filenames. Hyphens become underscores and letters become lowercase;
aliases that collapse the two locales are refused. Values preserve exact UTF-16
and empty strings, and use the shared literal-percent language adapter.

Language files default to `<world>/.nbtcli_translate`. Packs default to
`<world>/resources.zip` for legacy layout and
`<world>/resourcepacks/resources.zip` for the supported modern layout.
Missing resource parents remain absent during preview and staging; only commit
creates them. The plan is bound to the nearest existing physical ancestor, and
the result distinguishes the requested `outputRoot` from `physicalRoot`.
Use the returned `commitArguments` with that physical root to apply a retained
plan, even after another operation has created the requested directory.

Tool state and plan exports must be outside the source world and physical
output anchor. If an overly broad existing anchor contains either, create the
requested output directory first or choose separate state/export locations.
Output paths cannot cross metadata or gameplay/dimension trees of any ancestor
world, including worlds embedded below the physical anchor.

`create-pack` accepts `--name`, `--format zip|directory`, `--pack-format major[.minor]`
and `--merge PATH` (alias `--merge-existing PATH`). Supplying both merge options is
an error. Without `--format`, an explicit name selects a directory; otherwise
the default is ZIP. ZIP names receive `.zip` when missing. The default name is
`resources`. Without an explicit pack format, live known world metadata selects
the format; unknown versions are refused.

An explicit external output directory together with an explicit pack format uses
the session's frozen values without requiring current source-world metadata.
Defaults still require that metadata. Outputs inside a source recorded as a
world also require its valid live metadata for scope checks, even if its primary
marker has since disappeared; this grants no gameplay snapshot freshness claim.

Merge inputs may be ZIPs or directories. Capture validates the complete resource
manifest and saves immutable verified objects before returning the plan. Later
source-path changes cannot alter that plan. Tool state and plan exports cannot
be placed inside a directory merge input. Existing target metadata and opaque
bytes survive; differing opaque bytes or existing language values are conflicts.
`pack.png` is retained when present in the target or merge input.

For a retained resource plan, `apply <physical-root> --plan FILE --world-offline
--state-root STATE` uses resource preparation without a gameplay scan.
`status` and `recover` use the same physical root, plan hash and state root;
rollback restores original raw images and has exit 8. A successful resource
operation counts once and proves every output. Partial failure retains normal
recovery identity and never marks the source translation session applied.

These commands do not establish Minecraft loading, power-loss behavior, peak
RSS or release acceptance. The [shared recovery engine](apply-recovery.md)
describes eligible candidate reclamation and retention of immutable history.

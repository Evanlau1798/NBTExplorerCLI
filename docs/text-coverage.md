# Structured text sources

`WorldTextExtraction` discovers a world and streams per-source extraction results through
`IWorldInventory`. It never stages or commits world files. `Discovery.Complete` covers the
source catalog; consumers must also inspect every extraction outcome and pending syntax.
An empty enumeration is not proof of success when discovery failed.

`SourceAddress` always retains the actual artifact and ZIP entry. A legacy JSON component
inside an NBT String has a physical `NodePath` and a separate `ComponentPath`. Native JSON
and inline NBT components point directly to their String leaves. Physical heterogeneous-list
wrapper keys remain in NBT paths. Structure templates have no invented dimension or chunk.

## Profiles

| Game evidence | DataVersion | Data / resource pack formats | Item and text representation |
| --- | --- | --- | --- |
| Java 1.20.4 | 3700 | 26 / 22 | Legacy item tags, embedded JSON, plural pack folders |
| Java 1.21 | 3953 | 48 / 34 | Item components, embedded JSON, singular pack folders |
| Java 1.21.5 | 4325 | 71 / 55 | Item components, inline NBT, singular pack folders |
| Java 26.1 Snapshot 6 | 4774 | 99.0 / 80.0 | Namespaced world layout, inline NBT and object components |

World layout, artifact DataVersion, declared pack format and text schema are separate inputs.
An older known artifact can retain its own item/text schema in a newer world layout. Pack
creation format or an exact known minimum minor selects the pack schema; declared ranges do
not grant support for unknown future formats. Unknown versions have explicit outcomes.

## Content mapping

The 21 legacy categories map to these roles. Components extract String literals, styled
`extra`, translation `with` arguments, fallback text and explicitly permitted `show_text`
hover content. Numeric arguments and styling fields retain their original types.

| Legacy category | Roles / locations | Protected or separately processed data |
| --- | --- | --- |
| Sign | `sign.front.line`, `sign.back.line`; messages and filtered messages | Color, glow, positions and block state |
| HangingSign | `hanging_sign.front.line`, `hanging_sign.back.line` | Same constraints as sign |
| Book | `book.page`; written / writable, raw / filtered and empty pages | Generation, resolved state and item identity |
| LecternBook | `lectern.book.page`, title and author | Lectern page index and outer block data |
| Title | `book.title`, `lectern.book.title`; cover strings | A cover title is a Filterable String, not a component object |
| Author | `book.author`, `lectern.book.author` | Unrelated names / authors in arbitrary data |
| ItemName | `item.name`; legacy display Name, custom/item name components, loot modifiers | IDs, custom model data, predicates and arbitrary custom data |
| ItemLore | `item.lore`; ordered legacy/component/loot lore | Styling and modifier mode |
| ArmorStand | `armor_stand.name` | UUID, pose, tags and equipment metadata |
| EntityName | `entity.name`; entities, passengers and embedded entity data | Entity identity, tags and other attributes |
| ContainerName | `container.name`; block entities and embedded block data | Inventory slots, loot IDs and unrelated fields |
| CommandBlock | `command.block` syntax candidate; `command_block.name` component | Command body requires grammar processing, never whole-string translation |
| McFunction | `command.function` syntax candidate | Raw hash, UTF8/BOM and LF/CRLF/CR/mixed spans |
| TextDisplay | `text_display.text`; scalar, array or compound components | Transform, UUID and object resource identifiers |
| BossBar | `bossbar.name`; level, saved DAT or declared namespaced JSON | Boss ID, players, value, color and overlay |
| ScoreboardTeam | `scoreboard.team.name`, `.prefix`, `.suffix` | Team identifier, membership and rules |
| ScoreboardObjective | `scoreboard.objective.name` | Objective identifier and criteria |
| Advancement | `advancement.title`, `.description` | Criteria, rewards, icon IDs and arbitrary text-like keys |
| CommandStorage | Explicit `command_storage.literal` / `.component` rules | Unclassified Strings stay separate read-only candidates |
| LangKey | `language.value`; every String value, including item/block prefixes | `language.key` definitions identify property names; keys are not translation slots |
| TranslationKey | Exact `translation.key` references | A pure translate component contains no literal slot |

Container, bundle, projectile, equipment, lectern, embedded block/entity and structure-template
routes retain complete nested paths. Loot pools, entries, children, modifier sequences and
filtered modifiers use explicit function schemas. Their known IDs are checked against pinned
official registry identifiers; an unknown function does not become empty successful text.

Native JSON `set_components` recurses through container, bundle, projectile, block-entity and
entity payloads. Raw NBT fields in JSON use their own profile's embedded JSON String or native
component representation. Block/entity data also accepts the official SNBT String alternative.
Legacy loot `set_nbt` uses the known loot item type for book/bundle/crossbow fields; without a
known item identity, ambiguous item-specific fields produce `UnknownLegacyItemType`.
SNBT extraction reuses the bounded format parser and logical list adapter. Addresses retain
the real outer String path, the syntax leaf's byte span/hash and stable logical `syntax.nodePath`,
plus a separate `componentPath` for JSON inside an SNBT String. `utf8-java-string` means spans
are relative to strict UTF8 encoding of that decoded outer Java String, not raw JSON-file bytes.
Commands remain pending grammar candidates. These are incomplete sources, not writable text.
Other unimplemented JSON/source schemas remain explicit
unsupported results. This extraction layer is not a complete Minecraft gameplay validator.

## Rules and completeness

Type filters reach the field adapter before component parsing; native codec corruption and
metadata failures remain visible. Region/dimension/exact-artifact filters come from the same
`ScanRequest`. Nontext POI and declared vanilla system data keep protected identifiers.

CommandStorage permits up to 256 distinct, nonoverlapping explicit paths per artifact within
`data.contents`. The reader freezes caller policies, with at most 100,000 policy artifacts /
total rules. Rule-less Strings are not inferred from names such as `value` or `text`.

JSON readers preserve Java UTF16 code units, reject invalid UTF8 and duplicate decoded
property names, and retain existing codec byte/depth/node/container/string limits. Extraction
also bounds retained text/address records by `MaxDecodedBytes`. A changed raw source hash
or directory-pack metadata hash fails that source; it is not silently re-discovered mid-read.
Directory members retain their metadata dependency hash even when the exact artifact scope
excludes `pack.mcmeta`. Native component identifiers accept the default `minecraft` namespace
while keeping literal key addresses; conflicting aliases are incomplete. Vanilla saved-data
rules require the exact layout, dimension root and namespace, not only a matching basename.

The root `.nbtcli-writer.lock` is internal tool state with no world-content hash. A cache
manifest must exclude it instead of inventing a digest. Reparse points and reserved-path
protection remain enforced. Directory and ZIP containers are read once per extraction group;
per-source outcomes and item counts are separate. Session placeholder and reply policy belong
to the session layer; decoded text is never decorated with private markers here.

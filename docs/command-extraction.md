# Command extraction

Commands use the exact artifact or pack profile's grammar and text schema. Their display
text, translation keys and entity-name references are separate results. Extraction never
falls back to scanning every brace or replacing matching words throughout a command.

## Supported argument routes

The initial profiles are Java 1.20.4, 1.21, 1.21.5 and 26.1 Snapshot 6. An input syntax
binding must match the chosen command grammar and its preimage. JSON text applies to the
first two; the latter profiles use modern SNBT components and logical heterogeneous lists.

| Form | Text source |
| --- | --- |
| `tellraw <targets> <component>` | Scalar, array or compound component |
| `title <targets> title/subtitle/actionbar <component>` | Component argument |
| `bossbar add <id> <component>` / `set <id> name <component>` | Display component |
| `team add <id> <component>` / `modify <id> displayName/prefix/suffix <component>` | Display component |
| `scoreboard objectives add <id> <criteria> <component>` / `modify <id> displayname <component>` | Display component |
| `give <targets> <item> [count]` | Legacy tag or separate component assignment fragments |
| `item replace entity/block ... with <item>` | Item argument through the same rules |
| `summon <entity> <x> <y> <z> <compound>` | Explicit entity identity supplies its NBT schema |
| `setblock <x> <y> <z> <block[state]{compound}> ...` | Recognized vanilla sign/container/command-block/lectern schema |
| `data merge entity <selector-with-one-positive-type> <compound>` | Explicit selector type supplies the entity schema |

`execute ... run` supports chained `as`, `at`, `in`, `anchored`, `align`, `on`, `summon`,
coordinate positioning/rotation/facing, positioning/rotation `as`, and `if/unless entity`.
Unknown forms have a diagnostic and unresolved reference coverage. This is an extraction
grammar, not a complete Minecraft command execution validator.

Item components reuse the item/book/container/block/entity text rules. Unknown CustomData
and arbitrary storage `value` fields do not become literal translation slots. Multiple
component assignments retain independent syntax paths and spans.

## Locations and references

A function address keeps its real artifact and ZIP entry; an NBT command keeps its real
physical `Command` path, dimension and chunk. `syntax.nodePath` identifies the statement,
argument and logical leaf independently from its content. Byte spans and the SHA256
preimage are revision checks. CR, LF, CRLF and mixed newlines are retained; UTF8 BOM bytes
remain in the preimage and outside command tokens.

Direct JSON and SNBT text slots point to their exact quoted leaf tokens. JSON inside a
legacy SNBT String retains a separate `componentPath`. The syntax locator's grammar
determines how to parse and re-encode that token; callers must not splice decoded text
directly into raw command bytes.

Selector `name=` and `name=!` values are `NameReferenceCandidate` records, including
escaped quotes and Unicode. They are not translation slots or implicit rename authority.
Selectors in component `selector` fields also produce candidates. Those candidates bind
the whole encoded selector String token plus the stable selector option path, so a later
resolver must decode the String and locate the declared option before editing it.

Macro commands, unknown grammar, NBT selector predicates, dynamic NBT/score components,
click events and unresolved nested commands keep reference coverage unresolved. Known item
name/lore selectors produce candidates; references inside deeper item/book or structured NBT
arguments currently retain explicit unresolved coverage. A source
with unresolved references is unsupported/incomplete even when some display slots were
extracted. No result here authorizes a `ReferenceSynchronization` or a world write.

## Resource and adapter contract

Input bytes, decoded UTF16 text, token nesting/count and retained text/address records are
bounded. Combining multiple commands in one artifact applies an additional shared retention
budget. Hash mismatches, invalid component syntax and unsupported grammar have explicit
source outcomes. Functions and NBT command fields share this reader through
`WorldTextExtraction`; item counts remain separate from source counts.

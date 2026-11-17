# Public read queries

The registered query commands are `get`, `list` (`ls`) and `search` (`find`).
The product also registers `help`, `version`, `scan`, `extract`, `validate` and
`capabilities`; see [world text queries](cli-scan-contract.md) for their contracts.
`help <command>` lists that command's accepted options. Editing and session CLI
entry points remain planned.

Every invocation emits one `nbtcli.next.cli`, version 1 JSON envelope on stdout.
`success`, canonical `command`, `outcome`, `data` and `error` have stable meanings.
Error details may contain the codec's technical diagnostic. `--ui-locale zh-TW`
(default) or `en-US` selects the user message independently of data and machine codes.
It can precede or follow the command. The former global `--lang` UI usage is rejected.

| Exit | Meaning |
| --- | --- |
| 0 | The requested query completed, including a genuine zero-result search. |
| 1 | Unexpected internal failure. |
| 2 | Unknown command/option, invalid arguments or a required chunk is missing. |
| 3 | Ambiguous address or conflicting source binding. |
| 4 | Unsupported source or incomplete world discovery. |
| 5 | Missing target, malformed data, denied access or I/O failure; inspect `error.code`. |
| 6 | A declared input/output resource bound was exceeded. |

Unknown options, missing values, repeated options (including aliases), conflicting
node selectors and out-of-range pagination fail before a successful result.
Value options accept `--option value` or `--option=value`. Boolean flags take no
value. `--` ends option parsing. `-q`/`--q` explicitly alias `--query`; other short
value aliases are not guessed from their first letter.

## Selecting a source and node

```text
nbtcli-next get "C:\world copy\level.dat" --types
nbtcli-next get "C:\world copy" --path "datapacks/p/data/test/advancement/a.json#display.title"
nbtcli-next get "C:\world copy" --path "level.dat" --pointer /Data/LevelName
nbtcli-next get file.dat --segments '[{"kind":"key","key":""}]'
nbtcli-next get pack.zip --entry data/test/advancement/a.json --pointer /display/title
nbtcli-next get r.0.0.mca --chunk 1,0 --dimension minecraft:overworld --types
```

For a world directory, `--path` names an exact relative artifact, optionally followed
by `#` and a legacy node path. Relative artifacts and archive entries cannot traverse
outside their binding. A standalone file uses its parent as the selected root scope.
Use the world-directory form when addresses will be reused in world workflows.
The scope ID binds the chosen NTFS directory while its native identity persists;
it is not a portable Minecraft world UUID or a permanent identity across root deletion
or volume restoration. Recreated/restored roots require fresh scans and plans.

Legacy `Data.Inventory[0]` paths are accepted when unambiguous. Keys containing dots
or brackets require an exact selector if their spelling could name another structure.
`--pointer` uses JSON Pointer escaping (`~0`, `~1`); `""` selects the root and `/`
selects an empty key. Array indices cannot have leading zeroes. `--segments` accepts
an array of at most 256 exact `{"kind":"key","key":"..."}` or
`{"kind":"index","index":0}` segments. Choose one node path format.
Segment JSON rejects raw invalid Unicode source instead of replacing key units;
valid JSON escapes can represent the original Java UTF-16 key units.

`source` retains the file, outer raw hash, artifact/entry and exact source address.
Archived values also carry an entry hash. Queries confer no write authority; a later
write must revalidate the live source through the planner.

## Values and collections

`get --types` returns a `typed-nbt` value containing the existing reversible typed-NBT
envelope. Numeric widths, array kinds, float bits, physical duplicate entries and
Java UTF-16 strings are retained. For a selected node, the document name remains the
source document's root name. Without `--types`, NBT uses labelled `display-json`;
non-finite floats carry their bits, and non-scalar string values use the existing
`utf16be` object. Non-scalar compound keys require `--types`. Duplicate keys cannot
be resolved as unique node addresses. Native JSON uses `native-json`, preserving
the selected original JSON value, number spelling and escaped UTF-16 units.
Display JSON must not be imported as reversible typed interchange.

NBT compression can be selected with `--compression raw|gzip|zlib|auto`. Auto
recognizes the usual raw tag/GZip/Zlib prefixes; use an explicit codec where byte
prefixes overlap. This does not change the world metadata/profile rules.
This override applies to NBT payloads; region headers select their own codecs.
JSON and an unselected region index reject inapplicable compression/dimension options.

`list <file>` lists immediate children; `search <file>` visits typed nodes, including
typed-array elements. At least one of `--query`, `--name`, `--type` is required.
Filters combine, use ordinal case-insensitive text matching, and never substitute
an empty result for failed input. Results include exact addresses and bounded previews.
Subtree searches retain the selected node's name or array index. A query stops with
ResourceLimit when a full node address would exceed 256 segments.
`--limit` is 1–10000 (default 100), `--offset` is 0–2000000; totals precede pagination.

`list <world>` uses the shared profile-aware discovery for world name, declared
dimensions, terrain regions and entity regions. A missing LevelName is null.
Incomplete discovery returns nonzero instead of an apparently empty world.
Its `world-catalog` representation and `catalogComplete` cover metadata, enumeration
and container headers; they do not claim decoded payload or text/reference coverage.
`list <region>` without `--chunk` lists populated physical chunks. `get` requires
an exact global chunk coordinate; standalone regions also require `--dimension`.
World-directory queries verify dimensions against the declared layout.

All reads retain existing source bytes and create no world files. Physical artifacts
are limited to 64 MiB, codecs keep their existing decode budgets, and command output
is bounded to 64 MiB before it is emitted. Cache/scanning, extraction and validation
are described in the world-query contract. Session operations and edits are separate
integration work still to be registered.
Typed interchange also retains its existing depth/node/token budgets; an output
limit returns ResourceLimit for the valid source, and a narrower subtree can be queried.

# World text queries and validation

These commands appear in the product's `help` registry and use the new services.

`scan` (`scan-world`), `extract` (`extract-text`) and full `validate` share
`WorldQueryService`, source discovery, hash freshness, bounded decoding and the
atomic scan-cache repository. Their snapshots have identical identities for the
same world root, source scope and interpretation policy. Telemetry is separate
from semantic results. `--refresh` reparses every selected artifact; `--no-cache`
uses a disposable repository outside the world and does not read or publish the
persistent cache. Persistent caches live beneath the product state directory;
`--cache-root` selects a dedicated repository directory. Nonempty unowned
directories, reparse points and a cache directory inside the input world are
rejected.

| Command | Options |
| --- | --- |
| `scan <world>` | `dimension`, `region`, `types`, `path`, `limit`, `offset`, `summary`, `show-items`, `refresh`, `no-cache`, `cache-root`, `workers` |
| `extract <world>` | Shared scope/cache/pagination options, plus `all`, `unique`, `match-pattern`, `output` |
| `validate <world>` | Shared scope/cache/pagination options, plus `quick`, `verbose` |
| `capabilities` | Lists the pinned profile declarations and actual registered commands. |

`--ui-locale` remains the global display-language option. `--types` takes a
comma-separated set of text roles or the documented legacy category names for
these commands; it remains a boolean typed-output flag for `get`/`list`/`search`.
`--path` is an exact relative physical artifact path, not a node selector.
Dimension filters use exact namespaced IDs. Region filters accept `x,z` or an
exact region filename/path. Unknown types, malformed filters, unsafe paths and
out-of-range worker/pagination options fail before a cache write.

`--summary-only` and `--items` alias scan's `--summary` and `--show-items`.
`--no-limit` and `--deduplicate` alias extract's `--all` and `--unique`.
Aliases share duplicate detection with their canonical options. `--all` cannot
be combined with explicit pagination. Quick validation accepts scope and
pagination, but rejects decoding/cache controls because it does not decode.

`scan` returns paginated per-source outcomes and aggregate counts. `--show-items`
adds a separately counted page of decoded text occurrences; `--summary` omits
both pages, without skipping the scan. `extract` returns decoded text with exact
source addresses. `--unique` groups equal decoded strings while retaining all
instances and addresses; it never creates a writable merged slot. `--all` removes
pagination but not input, retained-result or output byte limits. Regex matching
uses a 100 ms timeout; invalid patterns and timeouts are explicit failures.

`--output` creates a new export outside the world, preserving any existing file.
It stores the same versioned machine envelope emitted on stdout. Serialization
finishes before staging, and the file is flushed, verified and atomically
published. Failure must not truncate an existing export or change world bytes.

`coverageComplete` describes the selected decoded-text sources, not an unfiltered
world, gameplay validity or the full native reference graph. Every response uses
`referenceCoverage: "not-assessed"`; known command references alone cannot prove
world reference completeness. Unsupported and failed sources retain diagnostics
and readable sibling results, with nonzero exit codes. Resource limits use exit
6, unsupported/incomplete text uses exit 4, and malformed or unreadable sources
use exit 5. Genuine zero matches are successful.

Full validation reports `validationScope: "decoded-text-sources"`; quick validation
reports `validationScope: "catalog"` and never claims it decoded payloads. Verbose
does not change the validation scope, outcomes or source bytes. Neither mode is
a Minecraft load test. `capabilities` reports each profile facet's declared read,
extract, plan, write and recover support independently; readable does not imply
writable, and missing declarations remain unknown.

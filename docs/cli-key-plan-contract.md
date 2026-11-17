# Session key plans and required language outputs

`translate plan <world> --session-id <guid> --revision <n> --keys
--language-output <relative-path[#entry]> --prefix <prefix> --output <new-plan>`
creates a preview from validated session replies. Use the retained plan with the
ordinary offline `apply`/`recover`, then `translate seal` with the original session
revision and completed plan hash. Keep the same state root throughout. The
[session completion contract](cli-session-write-contract.md) still governs proof,
retries, I/O errors and preserved replies.

Canonical `plan` requires exactly one of `--keys` or `--direct`. Key mode cannot
also request reference synchronization. `prefix` defaults to `map.` and accepts
1–128 ASCII letters, digits, dot, underscore or dash. Each key appends the stable
slot digest, keeping different physical/contextual occurrences distinct. Key
generation does not group identical display strings or use enumeration counters.
Existing different content under that key is a conflict; choose a new prefix or
resolve the explicit language conflict before applying.

`language-output` names a world-relative language artifact. Examples:

- `generated.zip#assets/example/lang/zh_tw.json` creates a resource ZIP if absent.
- `resources.zip#assets/example/lang/zh_tw.json` merges the declared entry of a known resource pack.
- `resourcepacks/client/assets/example/lang/zh_tw.json` uses a known directory resource pack and may create its missing language file.

The namespace/locale path and target locale must agree. Resource metadata must
declare support for the exact source component profiles. A newly generated pack
uses its declared component profile; mixed incompatible resource formats require
an existing pack that declares the needed range. Incompatible or unidentified
metadata is refused before commit. Existing different language keys and all
unselected ZIP entries remain protected; the workflow does not overwrite an
arbitrary ZIP or infer a pack from its filename.

The local service reads actual native/encoded component-root callbacks and binds
each Translated slot to one exact root, preimage, profile and LanguageDependency.
It handles native JSON, physical NBT, known encoded JSON/SNBT and command roots
through their existing adapters. Unchanged decisions remain literal no-ops.
Identifying entity names cannot borrow key authority; retain those decisions for
the explicit typed reference workflow. Text without a unique legal literal
component is unsupported. Logical root lookup has the existing TypedPath bound
of 256 combined segments; deeper recipes are rejected during planning.

Native parent/child components compose only after every original component
preimage is checked. Child conversions are retained when the parent literal
becomes a key. Conflict validation includes literal fields and the generated
`translate`/percent `with` fields; an explicit TypedEdit cannot compete for those
positions. Independent explicitly authorized edits still compose. The original
root, encoding rules and protected data remain verified. Existing legal plan
hashes retain their canonical encoding.

A physical homogeneous String list cannot promote one scalar element to a
compound while preserving its physical list type. That shape returns Unsupported;
it requires an explicit supported whole-list conversion first. Encoded JSON
arrays retain their physical string wrapper and use their existing grammar.
No list header or unselected item is silently coerced. Input recipes and retained
expected roots are bounded by the snapshot budget; lazy inputs are charged before
requesting another recipe.

Language files and packs are part of the same prepared physical set as their
components. All candidates must stage and verify before the first world commit.
Preparation failure keeps every original and accepted reply; partial commits use
the original durable journal and verified recovery. A language-only export or
standalone pack command is separate work; this workflow creates dependencies for
actual component conversions.

The old names `translate apply` and `translate apply-keys` are planning aliases in
this major version. They return `data.preview=true`, the canonical projection
under `data.result`, and machine-readable `data.migration` with canonical/commit/
completion commands. They never imply a world write. `apply` defaults to key
planning and also accepts explicit `--direct`; `apply-keys` always selects keys.
Their plan `--output` remains the raw PatchPlan accepted by global `apply`.
Migration includes complete `commitCommand`/`completeCommand` templates and
separate `commitArguments`/`completeArguments` arrays with actual world, session,
revision, retained operation ID, plan hash and any explicit state root. Paths
are absolute, and argv preserves spaces without shell reconstruction. With
`--output`, the returned argv can be passed directly to the CLI. Without it,
save `data.result.plan` and replace the sole `<retained-plan>` argument first.
Canonical preview data also exposes `planHash` without reparsing the plan.

Verification covers actual CLI pure datapack/NBT/mixed plan→pack→apply→seal,
existing ZIP merge, native nested and encoded command roots, mode/name refusals,
and application controls for lazy budgets and competing/generated fields. It
does not establish RSS/performance, arbitrary kill, power loss, Minecraft or
release acceptance.

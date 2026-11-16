# Static name reference analysis

`SelectorNames.Parse` reads the supported selector name syntax shared by command
and native text-component analysis. Each name retains its decoded Java string,
negation flag, option index and exact UTF-16 range in the original selector string.
Selector kind and an unambiguous positive entity type remain available to the
reference graph; a player selector must not be treated as an entity CustomName.

Unknown options, NBT-dependent selectors, multiple name options and malformed
syntax do not claim complete coverage. Input, nesting, member count and retained
results obey the supplied decode limits. This is reference analysis, not a general
Minecraft selector evaluator or a guarantee that every option value is executable.

`NativeComponentReferences` supports legacy embedded JSON strings, native JSON
components and modern NBT components. It uses the existing text extractor's exact
component-root callbacks, retaining real artifact, entry, dimension, chunk, typed
node path and embedded component path. It never invents command syntax addresses.
Plain text that resembles a selector stays plain text. Translation arguments,
extras, separators and supported text hovers follow the text profile's traversal.
Java UTF-16 strings, including isolated surrogate code units, remain lossless.

Dynamic NBT/score content, click events, unsupported hover actions and malformed
event shapes keep coverage unresolved. Existing component diagnostics also block
coverage. This conservative policy does not authorize rewriting these values.

These format-level results are not world-wide write authority. A subsequent service
must bind a live source snapshot, identify named targets and typed static references,
validate complete group membership, reject unknown or dynamic coverage, and stage
the entire declared group through the single world preparation/apply path. Neither
a caller-supplied Complete claim nor matching display strings proves that group.

Automatic reference synchronization is not yet connected to `WorldPlanBuilder`.
Ordinary literal/key operations continue to protect identifying names.

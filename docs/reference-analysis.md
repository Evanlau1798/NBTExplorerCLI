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

These format-level results are not world-wide write authority. Neither a caller's
Complete claim nor matching display strings proves an authorized group.

`WorldReferenceService.Create` accepts explicitly selected identifying-name slot
translations from a complete, unfiltered world snapshot. It rereads the live source
set, binds native entity types and composes visible plain names from literal parts
and extras. Hover text remains separate. Names using dynamic content are unresolved.
The service never adds another same-spelled entity to the requested edits. It rejects
unselected potential targets, collisions and references to the replacement name.

The currently supported synchronization domain consists of static NBT entity
CustomNames, direct typed entity selectors in functions or stored command fields,
and typed selectors in native JSON/NBT components. Region chunks and exact ZIP
entries retain their full addresses; each outer artifact has one candidate. Player
selectors are separate from entity CustomName. Untyped/contextual selectors,
unknown or dynamic grammar, encoded command selectors and named command/JSON
templates block affected synchronization until their explicit domain is supported.

`WorldPlanBuilder` recomputes the complete typed membership, including unchanged
visible name parts, before accepting a ReferencePatch. The plan binds every relevant
source preimage; discovery checks the source set before preparation and each commit.
A newly introduced source causes conflict or an incomplete recovery receipt, never
continued stale writes or success. Native private GUID candidates are excluded from
semantic discovery; reserved-prefix lookalikes still fail closed.

All group outputs use the existing stage/verify/apply/recovery path and immutable
receipts. Ordinary literal/key operations continue to protect identifying names.
This internal service is not a public CLI or Minecraft runtime acceptance.

# Plugin Tags Phase 1 — Platform-neutral foundation

## Purpose

Phase 1 establishes a reusable authoring core before any WinForms controls or complete YEP syntax catalog are introduced. It supports all future surfaces: database notes, maps, tilesets, events, page comments, plugin commands, movement routes, action sequences, text codes, switch/variable metadata, parameters, and script calls.

## Architectural boundary

`Playroma.JRPG.PluginTags` targets `net10.0`, has no UI dependencies, and knows no Yanfly syntax. Provider assemblies contribute immutable manifests and feature definitions. The Yanfly Phase 0 project now references this core only to expose its provider identity.

## Lossless parsing contract

A parsed document is an ordered, contiguous set of nodes whose spans cover the complete source exactly. Construction rejects gaps, overlaps, mismatched raw text, and incomplete coverage.

Node types are:

- Raw text
- Blank line
- Recognized single-line tag
- Recognized paired block
- Action-sequence block
- Script block
- Unknown tag
- Malformed tag

The parser preserves original spelling, casing, indentation, blank lines, Unicode, and LF/CRLF/CR line endings. Missing closers, unmatched closers, and nested same-type blocks are represented as recoverable malformed nodes.

## Catalog contract

A feature definition specifies:

- Provider and plugin identity
- Stable feature identity
- Authoring surface and applicable document kinds
- Syntax kind, canonical templates, aliases/patterns, and block closers
- Typed arguments, ranges, choices, and project-reference types
- Multiplicity and conflict groups
- Required and recommended plugins
- Specialized editor identity and complexity
- Version and source-fingerprint compatibility
- Search metadata and deterministic match priority

Catalog construction validates provider ownership, duplicate identities, regex syntax, block completeness, argument uniqueness, ranges, and template placeholders.

## Editing contract

`PluginTextEditingSession` binds raw and visual editing to one working string. It supports:

- Complete raw-text replacement
- Feature insertion or occurrence-specific replacement
- Feature removal
- Revert and commit
- Dirty-state and parse-change events
- Context guards
- Preferred line-ending detection
- Preservation of unrelated source nodes

A deliberate recognized-feature edit may canonicalize that feature only. No other source is reformatted.

## Validation contract

Built-in diagnostics cover:

- Malformed syntax
- Wrong authoring surface
- Wrong document type
- Duplicate singleton features
- Conflict groups
- Missing or invalid arguments
- Choice and numeric-range violations
- Missing, disabled, or recommended plugins
- Version and source-fingerprint compatibility

Additional provider validators can be composed without changing the core.

## Reference contract

Typed feature arguments can expose strong project references, including actors, classes, skills, items, weapons, armors, enemies, states, animations, common events, switches, variables, regions, icons, tilesets, and troops. Lists and bounded ranges are expanded deterministically.

## Specialized editor contract

The core stores editor descriptors only. A UI layer can register a specialized editor ID, supported surfaces, supported feature keys, and raw-fallback capability. No WinForms type crosses the platform-neutral boundary.

## Deferred to later phases

- Complete generated YEP semantic catalog
- Installed-plugin manifest integration
- WinForms Plugin Tags tab
- Typed WinForms argument controls
- AI, action-sequence, item, state, enemy, map, and event specialized editors
- Project-wide audit UI

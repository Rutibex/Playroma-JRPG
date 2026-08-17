# ADR-0013 — Raw-backed reusable database editor framework

## Status

Accepted for Phase 8.

## Decision

Use a platform-neutral registry of database definitions and field descriptors with specialized reusable controls and optional custom editor bodies. Bind records by stable numeric ID and raw JSON path. Route every mutation through editor commands and transactions.

Use a virtual summary index for record lists. Do not regenerate and parse entire database files for selection, filtering or display. Do not use a general-purpose property grid as the primary editor.

## Consequences

- Unknown properties survive known-field edits.
- Sorting cannot accidentally renumber database records.
- Multi-record edits share one transaction and undo entry.
- Common controls are implemented once for Phase 9.
- Troops, Animations, Tilesets and System may supply custom bodies without abandoning shared infrastructure.
- Descriptor definitions become compatibility-critical and require contract tests.

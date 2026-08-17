# ADR-0014 — Raw-backed event authoring with a structural projection

- Status: Accepted
- Phase: 10

## Context

RPG Maker MV stores map, common, and troop event programs as flat arrays of command objects. Branches and continuations are encoded through command codes, indentation, and marker rows. Plugin-heavy projects can also contain unknown command codes, extra object properties, unusual parameter shapes, and malformed but still valuable data.

Regenerating an entire event from a proprietary tree would risk changing unrelated commands, losing unknown data, or accidentally reindenting complex lists.

## Decision

The flat MV command array remains authoritative.

Each command is captured with its complete raw JSON object. `EventStructureProjector` builds a tolerant tree for navigation and structural editing, while every node retains the exact raw span from which it was projected. Edits replace only the affected span through the normal project transaction system. Unknown and malformed rows become opaque nodes rather than being discarded.

Compound commands receive explicit synchronization where the opening parameters duplicate information in marker rows. In particular:

- Show Choices keeps codes 402, 403, and 404 aligned with its choice and cancel parameters.
- Battle Processing keeps codes 601, 602, 603, and 604 aligned with escape and defeat handling.
- Set Movement Route keeps the route object in code 205 aligned with code 505 continuation rows.

Editor-only bookmarks and annotations are stored outside MV JSON.

## Consequences

- Untouched event lists can round-trip exactly.
- Structural operations can be undoable as one transaction.
- Unknown commands and extra properties survive.
- Malformed lists remain inspectable and repairable.
- Typed editors must always retain a raw fallback.
- Runtime/plugin-specific semantic interpretation remains separate from the compatibility-critical authoring model.

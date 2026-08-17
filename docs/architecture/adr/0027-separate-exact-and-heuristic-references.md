# Separate exact and heuristic references

## Status

Accepted for Phase 3.

## Context

RPG Maker MV stores many references in known numeric fields and event-command
parameters. Projects also embed possible IDs in JavaScript, formulas, notes,
plugin commands, and string-encoded plugin parameters. Treating both forms as
equally certain would make automatic refactoring unsafe.

## Decision

Every indexed reference carries a `ReferenceConfidence` value:

- `Exact` for a documented MV field or command parameter whose meaning is known;
- `Heuristic` for a candidate discovered in free-form or plugin-owned text.

Dependency reports and validation expose the two sets independently. Automatic
fixes and future refactoring may operate on exact references only unless the user
explicitly reviews and approves heuristic matches.

## Consequences

- Safe deletion can block on exact inbound dependencies.
- Script and note matches remain visible without being silently rewritten.
- Plugin-heavy projects can be inspected even when Playroma has no schema for a
  plugin.
- Extractors must document why each exact parameter is considered authoritative.

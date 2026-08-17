# Regenerate the smallest changed subtree

- Status: Accepted
- Date: 2026-07-19

## Context

Structural edits cannot always be represented as a single scalar token replacement.

## Decision

Insert or remove at the containing object or array boundary and reject ambiguous overlapping edits. Large map data is replaced only when the map buffer is committed.

## Consequences

Generated output stays localized. Callers must generate and reopen after an overlapping structural workflow.

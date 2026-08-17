# 0002 — Use raw-backed typed documents

- Status: Accepted
- Date: 2026-07-19

## Context

MV projects can contain plugin-defined JSON properties and unusual values unknown to Playroma. Reconstructing documents from incomplete C# models would destroy data.

## Decision

Typed views will wrap mutable raw JSON nodes. Unknown properties remain in the raw node and survive edits. Original bytes and document fingerprints are retained by the future document layer.

## Consequences

Code is slightly more complex than ordinary serialization, but lossless compatibility becomes an architectural invariant rather than a best effort.

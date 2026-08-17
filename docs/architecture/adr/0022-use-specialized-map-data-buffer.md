# Use a specialized map data buffer

- Status: Accepted
- Date: 2026-07-19

## Context

MV map data arrays can contain hundreds of thousands of integers and should not create one heavyweight editable object per tile.

## Decision

Decode map data lazily into a compact integer array, track dirty indices, and replace the data subtree only when committed.

## Consequences

Map editing remains memory-efficient and does not affect events or other map properties.

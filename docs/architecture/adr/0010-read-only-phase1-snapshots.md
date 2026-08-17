# Read-only Phase 1 snapshots

- Status: Accepted
- Date: 2026-07-19

## Context

Phase 1 must inspect projects without accidentally introducing mutable state or save behavior.

## Decision

Return an immutable `MvProjectSnapshot` and use disposable read-only JSON leases that retain original bytes.

## Consequences

The loader is safe to use before Phase 2 exists; later mutable documents must be built as a separate layer.

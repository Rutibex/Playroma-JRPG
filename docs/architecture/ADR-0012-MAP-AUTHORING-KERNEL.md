# ADR-0012: Platform-neutral map-authoring kernel

## Status

Accepted.

## Decision

Map gestures operate in `Playroma.JRPG.Mapping` against a compact six-layer buffer. WinForms supplies pointer input and renders the temporary overlay, but it does not directly mutate JSON. Completion creates one compressed reversible patch and commits it through the project transaction system.

## Consequences

- Mapping logic is testable without WinForms.
- Large fills and strokes remain one undo entry.
- Cancellation is safe.
- The Phase 6 renderer is reused for exact previews.
- Alternative front ends can reuse the authoring kernel.
- MV's raw map data remains authoritative and unknown data remains preserved.

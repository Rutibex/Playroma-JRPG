# ADR-0011 — Immutable Render Plans and a Deterministic Reference Renderer

## Status

Accepted for Phase 6.

## Context

MV maps are mutable raw-backed JSON documents, while WinForms paint callbacks require fast, stable data. Directly traversing JSON or decoding autotiles in `OnPaint` would couple UI, serialization, and rendering and make regression testing difficult.

## Decision

Playroma copies rendering fields into immutable snapshots, compiles them into ordered render instructions, and executes those instructions through a platform-neutral RGBA reference renderer. Windows image decoding and bitmap presentation live in a separate adapter project.

## Consequences

- paint callbacks do not access mutable JSON;
- unknown data remains in the authoritative document;
- tile decoding can be tested independently;
- headless golden output is deterministic after images have been decoded;
- other front ends may reuse the renderer;
- plugin-specific layers require explicit providers in a later phase rather than hidden JavaScript execution.

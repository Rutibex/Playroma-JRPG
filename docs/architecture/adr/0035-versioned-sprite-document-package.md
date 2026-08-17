# ADR-0015: Versioned native sprite package

- Status: Accepted
- Date: 2026-08-05

## Context

PNG cannot preserve editable layers, cels, frame timing, tags, slices, tilemaps, palettes, metadata, or export bindings.

## Decision

Use a versioned `.playromasprite` ZIP package with `document.json` and separately addressable future payload directories. UUID references permit linked cels and migrations. Runtime PNG files remain generated assets.

## Consequences

Users gain lossless authoring documents and atomic saves. The project must maintain migrations, reject unsupported newer versions safely, and keep runtime export separate from authoring persistence.

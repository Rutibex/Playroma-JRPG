# Tileset Construction Studio

## Workspace

The Construction Studio contains four principal pages:

1. **Construction** — A1–E sheet tabs, the source library, slot properties, and metadata.
2. **Test Map** — a complete layered map painter and movable character test.
3. **Coverage** — construction-template requirements and completion scores.
4. **Analysis** — validation, palette/style statistics, duplicate detection, and dependency status.

## Asset assignment

A selected logical slot can receive:

- a saved Phase 2 autotile compatible with that slot;
- a Phase 3 material converted through the material-to-autotile adapter;
- an ordinary A5 or B–E object;
- manually imported or edited artwork already present in the sheet.

Assignments are non-destructive until saved and participate in per-sheet undo and redo.

## Construction templates

Templates describe semantic coverage rather than artwork. They define required, recommended, and optional categories with matching tags and preferred sheets. The supplied catalogue covers outdoor, interior, dungeon, world-map, science-fiction, modern-city, horror, winter, desert, swamp, ship, castle, village, cave, laboratory, and custom construction.

The completeness analyser compares names, categories, and tags from bindings and objects with the active template and reports both category counts and an overall weighted score.

## Assisted build

The assisted builder can fill empty compatible slots from saved autotiles and materials. It does not overwrite locked bindings. The operation reports every assignment and any unmet requirements so the result remains inspectable rather than opaque.

## Packing

The B–E packer supports:

- compact area-first placement;
- category grouping;
- variation-family grouping;
- existing-layout preservation;
- commercial presentation ordering;
- manual-only mode.

It preserves sheet B cell 0, respects occupied sheet pixels, supports multi-cell rectangles, records a binding for every occupied cell, and reports objects that do not fit.

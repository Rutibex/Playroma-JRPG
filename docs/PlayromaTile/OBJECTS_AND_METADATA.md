# Ordinary Objects and Gameplay Metadata

## Ordinary object model

A `TileObjectDefinition` records a project's reusable ordinary tile or multi-cell object. Its source image is copied into the project and padded to the 48×48 cell grid when requested.

Stored properties include:

- grid dimensions;
- visual bounds;
- anchor cell;
- collision rectangle;
- primary and shadow images;
- frame list and durations;
- category, tags, notes, and lock state;
- source material and Style Profile references;
- variation-group membership;
- shared gameplay metadata.

## Variation families

A variation group can use equal, weighted, seasonal, condition-based, manual, adjacency-aware, or biome-dependent selection semantics. RPG Maker MV does not consume these Playroma-only selection rules directly; they are retained for test scenes, documentation, and later generation phases.

## Metadata

Per-cell metadata supports:

- passable;
- impassable;
- four-direction passage blocking;
- above-character/star rendering;
- ladder;
- bush;
- counter;
- damage floor;
- terrain tag 0–7;
- semantic type;
- footstep type;
- notes.

Metadata recommendations infer common settings from names, categories, and semantic tags. They are suggestions unless automatic application is explicitly enabled.

## Test map

The complete test-map renderer draws lower layers, ordinary upper layers, above-character tiles, a player marker, grid lines, collision overlays, and damage-floor overlays. Movement checks the active metadata at the target cell and reports blocked or damaging locations.

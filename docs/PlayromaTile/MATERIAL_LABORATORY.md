# Material Laboratory Guide

Open the laboratory with **F7**.

## Quick Generator

Choose a category, preset, style, condition, seed, candidate count, work resolution, and mutation amount.

- **Generate** produces deterministic alternatives.
- **Surprise Me** selects a new seed and preset.
- **Keep** promotes the selected candidate into the current recipe.
- **Make Similar** mutates around the selected candidate.
- **Breed with #1** averages exposed controls from candidate 1 and the selected candidate.
- **Lock palette**, **Lock structure**, and **Lock detail** prevent those exposed controls from mutating.
- **Open in Graph** promotes the candidate and exposes its full construction.

The selected candidate is shown as a 3×3 tiled preview so visible seams are immediately apparent.

## Advanced Graph

Double-click a node in the library to add it. Drag nodes to arrange the graph. Select a node to edit its parameters and input connections.

The graph editor supports:

- adding and deleting nodes;
- duplicating nodes;
- automatic dependency layout;
- parameter changes;
- source-connection changes;
- direct final-quality rendering.

Connections are selected by input name and source node. A graph cannot render when it contains a cycle, missing required input, unavailable node, or missing output.

## Imported exemplars

**Import Exemplar** creates a ready-to-edit graph:

```text
Image Input → Image Quilting → Seam Repair → Pixel Art Finish → Material Output
```

Absolute source paths are copied into the saved recipe's `Inputs` folder and rewritten as portable project-relative paths.

## Autotile conversion

**Send to Autotile** presents conversion modes for ordinary floors, overlays, hard or soft terrain, shores, cliffs, roofs, walls, animated water, and waterfalls.

The generated source remains an editable Phase 2 definition. It is not written directly into a sheet until inspected and compiled in the F6 workshop.

## Wang variation preview

**Wang Preview** extracts edge-classified tile variants and creates a compatible randomized preview. This demonstrates reduced repetition. RPG Maker MV does not automatically choose arbitrary Wang variants, so they remain a Playroma authoring and future map-generation feature.

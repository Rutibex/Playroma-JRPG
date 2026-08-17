# Procedural Methods

## Periodicity

Procedural noise is sampled in explicit periods derived from material frequency. Filters and transforms use toroidal sampling when wrapping is enabled. This preserves seamlessness through most graph branches rather than attempting to conceal every seam at the end.

## Noise

Value and gradient noise interpolate deterministic lattice values. Cellular noise measures distances to jittered feature points. Fractal modes combine several frequencies with configurable lacunarity and persistence. Domain warping offsets the sampling coordinates with lower-frequency noise.

## Patterns

Pattern nodes produce scalar structure rather than final colours. The same field can therefore drive colour, height, mortar, wear, decoration placement, or masks.

## Reaction diffusion

The implementation uses two double-buffered scalar fields and wrapped Laplacian sampling. Draft and interactive quality modes cap iteration counts while preserving the recipe's final settings.

## Erosion and weathering

Thermal erosion moves material from cells whose local drop exceeds a talus threshold. Additional local comparisons derive edge wear, cavities, and moisture-like masks.

## Quilting

The quilter places overlapping exemplar patches. Several random source candidates are scored against already written overlap pixels; the lowest-error candidate is blended into the result. A seam-repair pass reconciles opposite boundaries.

## PatchMatch-style inpainting

Masked pixels repeatedly sample known source coordinates and keep the best local neighbourhood match among randomized candidates. The implementation prioritizes deterministic, dependency-free authoring over a research-grade optimized nearest-neighbour field.

## Pixel-art reduction

Area sampling reduces high-resolution results. K-means clustering creates a controlled palette. Ordered or error-diffusion dithering distributes colour error. Optional cleanup removes isolated pixels and snaps alpha to stable levels.

## Wang tiles

The baker creates all combinations of north, east, south, and west edge classes, then verifies compatibility during randomized preview placement. The current baker is intended for variation authoring and preview rather than direct MV runtime selection.

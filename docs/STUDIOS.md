# Integrated Studios

Playroma jRPG includes several content-authoring environments that share the same overall goal: create or edit game-ready assets while keeping RPG Maker MV compatibility visible to the author.

## Text Studio

Text Studio is a deterministic procedural-text workbench. Its libraries and editor cover:

- lists, weighted tables, templates, and ad-lib generation;
- cultural and phonotactic name generation;
- Tracery-compatible grammars implemented natively in C#;
- Markov/corpus workflows;
- candidate scoring, semantic metadata, constraints, and ranked generation;
- project-aware recipes;
- database, map, event, and message-oriented imports;
- character voice profiles and dialogue realization;
- storylets and procedural narrative;
- quest grammar/planning;
- batch content generation;
- deterministic seed-space search and advanced candidate selection;
- an optional process-based external AI-provider boundary.

Authoritative Text Studio project assets live under `data/PlayromaText/`; disposable derived caches belong under `.playroma/cache/text/`.

The optional AI bridge is disabled/provider-free by default and does not bundle models, API keys, provider SDKs, or cloud clients. Treat any configured external executable as trusted software with the permissions of the current user.

## Sprite Studio

Sprite Studio is a clean-room pixel-art and RPG Maker MV asset editor. Major capabilities include:

- drawing tools, custom/pattern brushes, symmetry, wrap/tiled drawing, dithering, outlines, guides, and snapping;
- layers, selections, transformations, animation timelines, cels, and playback;
- RPG Maker MV asset profiles/templates and validation;
- color/palette workflows and effects;
- specialized MV system-asset editors;
- tilemaps, linked tile cels, slices, pivots, and nine-slice metadata;
- automation, batch processing, export presets, atlases, scripting/pipelines, and CLI support;
- recovery/resilience features and lossless `.playromasprite` packages.

Aseprite is a behavioral reference only. No Aseprite code or bundled resources are redistributed.

## Tile Studio

Tile Studio provides MV-oriented tile authoring and tileset construction, including:

- sheet editing and import/export;
- RPG Maker MV autotile compilation;
- procedural material generation;
- A1-A5 and B-E tileset construction;
- ordinary and multi-cell objects;
- passability/directional flags and other MV tile metadata;
- construction previews and test-map rendering;
- packing, catalogues, snapshots, and publication packages;
- safe project patching with backups.

## Sound Studio

Sound Studio provides procedural sound-effect authoring with synthesis/generation, DSP, authoring graphs, analysis, preview/playback, reusable templates, and MV-oriented export. See `docs/PlayromaSound/` for detailed sound documentation.

## Music Studio

Music Studio provides procedural composition and arrangement with reusable style packs, MIDI-oriented generation, SoundFont playback/rendering, interactive preview surfaces, and audio export for RPG Maker MV workflows.

The repository includes a GeneralUser GS SoundFont under its own licence. See `MusicAssets/SoundFonts/GENERALUSER_GS_LICENSE.md` and `THIRD-PARTY-NOTICES.md`.

## Working with a live RPG Maker project

Generated content should be reviewed before import. Where a studio offers project integration, prefer its preview/transaction workflow over manually overwriting project files. Third-party plugins and custom runtimes can impose constraints that Playroma cannot infer automatically.

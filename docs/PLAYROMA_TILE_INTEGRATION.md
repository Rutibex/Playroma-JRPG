# Playroma Tile integration

Playroma jRPG embeds the Playroma Tile WinForms suite as **Tools → Playroma Tile Studio**.

## Project storage

Each RPG Maker MV project receives an editable authoring project at:

`data/PlayromaTile/ProjectTiles.playromatile.json`

Its private sheet copies, autotile definitions, procedural materials, object metadata, tileset constructions, snapshots, and catalogues are stored beside that file.

## Database bridge

The integrated File menu can load selected records from `data/Tilesets.json`, refresh linked records, export the selected tileset, or export every tileset. Loading copies the exact A1–E images from `img/tilesets` and imports MV passage flags into the construction metadata. Export creates a timestamped `PlayromaBackups` directory before updating `Tilesets.json` and PNG files.

## Included authoring systems

The original sheet editor, region editor, autotile compiler workshop, procedural material laboratory, tileset construction studio, object editor, test maps, validation, publication packages, standalone project import/export, and sample assets remain available.

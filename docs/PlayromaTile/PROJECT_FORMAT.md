# Project Format Version 4

The root `.playromatile.json` stores portable metadata and references. Binary images and detailed definitions remain in project-relative folders.

## Root additions

```json
{
  "formatVersion": 4,
  "tilesetConstructions": [
    {
      "id": "...",
      "tileSetId": "...",
      "name": "Dungeon",
      "relativeDefinitionPath": "Tilesets/.../tileset.tileset.json",
      "revision": 8,
      "modifiedUtc": "..."
    }
  ]
}
```

Each `TileSetDefinition` may also contain `constructionId`, linking it to its Phase 4 definition.

Versions 1–3 are migrated in memory to version 4 when opened. Save the project to persist the new version.

## Construction definition

A `.tileset.json` definition contains:

- theme, active template, and Style Profile;
- source bindings for every assigned slot;
- ordinary object definitions;
- variation groups;
- per-cell gameplay and semantic metadata;
- packing and export settings;
- layered test map;
- snapshot records;
- revision and timestamps.

## Project directories

```text
Sheets/
Catalogues/
Autotiles/
Materials/
Objects/<object-guid>/
Tilesets/<construction-guid>/
Snapshots/<construction-guid>/<snapshot-guid>/
```

## Save As

Project Save As copies `Sheets`, `Catalogues`, `Autotiles`, `Materials`, `Objects`, `Tilesets`, and `Snapshots` into the new project directory while retaining portable relative references.

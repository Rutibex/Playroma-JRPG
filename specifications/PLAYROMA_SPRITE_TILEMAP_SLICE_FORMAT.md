# Playroma Sprite Tilemap and Slice Format

## Package schema

Format version 2 extends the normal `document.json` inside `.playromasprite` packages. Pixel payload entries remain `pixels/{guid}.rgba` and retain their existing SHA-256 manifest coverage.

## Tilemaps

A tilemap `SpriteLayer` has `kind = tilemap`, a `tilesetId`, `tileEditMode`, no image cels, and zero or more `tilemapCels`. Each tilemap cel associates one animation `frameId` with one `tilemapContentId`, position, opacity, and z-index.

A tilemap content payload contains columns, rows, an empty-tile index, and exactly `columns × rows` cells. A cell contains a tile index, transform flags, and opacity.

## Tilesets

A tileset defines tile size, source grid, margin, spacing, revision, optional source paths and external Tile Studio ID, reusable tile definitions, automatic rules, and user data. Non-empty tiles reference an ordinary pixel-content descriptor.

## Slice keys

A slice retains base bounds for backward compatibility and an ordered list of frame keys. Each key contains a frame number, bounds, optional pivot, and optional nine-slice centre. The centre must be contained by the corresponding bounds.

## Compatibility

Readers supporting version 2 must accept version 1 documents whose tilemap collections and slice-key collections are absent. Writers initialize missing collections to empty lists. Version 1 readers are not expected to understand version 2 tilemap data.

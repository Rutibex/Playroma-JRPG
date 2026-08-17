# RPG Maker MV Project Integration

## Inspection

Select an MV project directory containing `data/Tilesets.json`. Playroma Tile reads the existing tileset records and displays their IDs, names, modes, and image-sheet names.

## Safe patch operation

A patch operation:

1. verifies `data/Tilesets.json`;
2. creates a timestamped backup directory;
3. backs up the database and any destination images that already exist;
4. copies the nine current sheet images into `img/tilesets`;
5. creates a new tileset record or updates the selected record;
6. converts Phase 4 metadata into MV tile flags;
7. writes the database through a temporary file and atomic replacement.

Unknown fields in an existing record are retained because the record is edited as a JSON object rather than reconstructed from a narrow DTO.

## Tile flag addressing

The integration layer maps ordinary B–E and A5 cells to their MV tile IDs and expands each A1–A4 logical autotile slot across its 48 runtime shape IDs. Passage, directional flags, star rendering, ladder, bush, counter, damage floor, and terrain tags are encoded into the flag array.

## Recovery

Backups are written under:

```text
PlayromaBackups/<timestamp>_<tileset-name>/
```

Restoration is intentionally manual in Phase 4: close RPG Maker MV, restore the backed-up `Tilesets.json`, and copy any backed-up PNG files to `img/tilesets`.

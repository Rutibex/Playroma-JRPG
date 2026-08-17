# RPG Maker MV tilesheet layouts used by Playroma Tile

All logical map tiles are 48×48 pixels.

| Slot | Image size | Phase 1 logical regions | Use |
|---|---:|---:|---|
| A1 | 768×576 | 16 irregular blocks | Animated ocean, water, decorations, and waterfalls |
| A2 | 768×576 | 32 blocks of 96×144 | Ground and floor autotile sources |
| A3 | 768×384 | 32 blocks of 96×96 | Building and roof group-pattern sources |
| A4 | 768×720 | 48 variable-height blocks | Walls, cliffs, and dungeon walls |
| A5 | 384×768 | 128 tiles | Ordinary lower-layer tiles |
| B | 768×768 | 256 tiles | Ordinary upper-layer tiles; tile 1 must be transparent |
| C | 768×768 | 256 tiles | Ordinary upper-layer tiles |
| D | 768×768 | 256 tiles | Ordinary upper-layer tiles |
| E | 768×768 | 256 tiles | Ordinary upper-layer tiles |

## A1 order

A1 uses four 144-pixel-high bands. Its logical catalogue order is not simple row-major order. Playroma Tile maps the sixteen engine slots explicitly:

1. Ocean A, three horizontal animation frames
2. Deep ocean B, three horizontal animation frames
3. Ocean decoration C
4. Ocean decoration C
5. Water D, three horizontal animation frames
6. Waterfall E, three vertical animation frames
7–16. Remaining D/E water and waterfall pairs

## A4 order

A4 contains three repeated pairs:

```text
144 px high basic-autotile row, 8 across
 96 px high group-pattern row, 8 across
```

The six logical rows total 720 pixels and correspond to 48 catalogue entries.

## Raw source-cell mode

Every sheet can also be viewed as 48×48 source cells. These cells are editing units, not necessarily complete runtime tiles. A single A1 or A2 logical asset occupies several raw source cells.

## Phase 2 quarter rendering

A displayed MV autotile is assembled from four 24×24 quarters. Playroma Tile exposes three immutable source-table families:

| Family | Source quarter grid | Rendered shapes |
|---|---:|---:|
| Floor/basic | 4×6 | 48 |
| Wall/group pattern | 4×4 | 16 |
| Waterfall | 4×2 per frame | 4 |

Automatic floor painting resolves shapes 0–46. Shape 47 is the fixed representative pattern used by Shift-style placement.

Animated A1 floor-family sources place three 96×144 frames horizontally. A1 waterfall places three 96×48 frames vertically within its 96×144 logical source region.

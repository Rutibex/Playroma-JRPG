# Autotile compiler technical reference

## Quarter tables

RPG Maker MV builds a displayed 48×48 autotile from four 24×24 source quarters. Playroma Tile stores the exact floor, wall, and waterfall lookup tables as immutable coordinate data in `MvAutotileTables`.

`MvAutotileRenderer` is independent from `MvAutotileCompiler`:

- the compiler writes a source block into an MV sheet region;
- the renderer reads the source block and reconstructs displayed map tiles.

This separation prevents the compiler from validating itself with the same write path.

## Neighbour masks

```csharp
[Flags]
public enum NeighborMask
{
    None = 0,
    NorthWest = 1 << 0,
    North = 1 << 1,
    NorthEast = 1 << 2,
    West = 1 << 3,
    East = 1 << 4,
    SouthWest = 1 << 5,
    South = 1 << 6,
    SouthEast = 1 << 7
}
```

For floor shapes, diagonal flags are normalized away unless both touching cardinals are present. The four desired output quarters are then derived and matched against the immutable MV table.

## Source dimensions

The source block is always exact; the compiler never stretches it.

```csharp
Size AutotileKindInfo.GetSourceSize(AutotileKind kind)
```

A mismatched source is rejected before any sheet modification.

## Rendering API

```csharp
Bitmap RenderShape(Bitmap source, AutotileKind kind, int shape, int frame = 0);
Bitmap RenderShapeAtlas(Bitmap source, AutotileKind kind, int frame = 0, int columns = 8);
Bitmap RenderMap(Bitmap source, AutotileKind kind, bool[,] cells, int frame = 0, bool drawGrid = false);
```

Frame origins are:

- A1 ocean/deep-ocean/water: `(frame × 96, 0)`;
- A1 waterfall: `(0, frame × 48)`;
- static source blocks: `(0, 0)`.

## Compiler API

```csharp
AutotileCompileResult CompileIntoSheet(
    Bitmap sheet,
    TileRegionMetadata regionMetadata,
    MvSheetSlot slot,
    int regionIndex,
    AutotileDefinition definition,
    Bitmap source);
```

The compiler verifies:

- exact source dimensions;
- target region index;
- source kind/slot compatibility;
- exact target rectangle dimensions.

It uses `CompositingMode.SourceCopy` so transparent pixels replace existing pixels rather than blending with them.

## Inverse importer

```csharp
(AutotileDefinition Definition, Bitmap Source) Extract(
    Bitmap sheet,
    MvSheetSlot slot,
    int regionIndex,
    string name);
```

The inverse importer crops the logical region exactly. It performs no scaling, colour conversion, rotation, or reconstruction.

## Material composer API

```csharp
Bitmap Compose(
    AutotileKind kind,
    Image material,
    int boundaryDarkening = 30,
    int boundaryWidth = 2,
    bool preserveMaterialAlpha = false);
```

The composer uses a generated topology mask, repeats the supplied exemplar, darkens pixels near transparent boundaries, and introduces deterministic A1 frame offsets.

## Definition storage

Each source definition is JSON plus a 32-bit ARGB PNG. JSON contains semantic metadata and hashes; pixel data remains in the PNG.

## Validation codes

- `AT001` — incorrect source dimensions.
- `AT002` — missing name.
- `AT010` — rendered shape is fully transparent.
- `AT020` — working pixels differ from the last saved source hash.
- `AT030` — strong horizontal source seam.
- `AT031` — strong vertical source seam.

Warnings are advisory unless marked as errors. Transparent overlay definitions can legitimately produce transparent regions.

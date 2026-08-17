namespace Playroma.JRPG.Rendering;

/// <summary>
/// Exact RPG Maker MV tile identifiers, classification rules, and quarter-tile lookup tables.
/// The tables are transcribed from the MIT-licensed MV CoreScript Tilemap implementation.
/// </summary>
public static class MvTileRules
{
    public const int TileWidth = 48;
    public const int TileHeight = 48;
    public const int QuarterWidth = 24;
    public const int QuarterHeight = 24;
    public const int TileIdB = 0;
    public const int TileIdC = 256;
    public const int TileIdD = 512;
    public const int TileIdE = 768;
    public const int TileIdA5 = 1536;
    public const int TileIdA1 = 2048;
    public const int TileIdA2 = 2816;
    public const int TileIdA3 = 4352;
    public const int TileIdA4 = 5888;
    public const int TileIdMax = 8192;

    public static bool IsVisible(int tileId) => tileId > 0 && tileId < TileIdMax;
    public static bool IsAutotile(int tileId) => tileId >= TileIdA1 && tileId < TileIdMax;
    public static int GetAutotileKind(int tileId) => (tileId - TileIdA1) / 48;
    public static int GetAutotileShape(int tileId) => (tileId - TileIdA1) % 48;
    public static int MakeAutotileId(int kind, int shape) => checked(TileIdA1 + kind * 48 + shape);
    public static bool IsTileA1(int tileId) => tileId >= TileIdA1 && tileId < TileIdA2;
    public static bool IsTileA2(int tileId) => tileId >= TileIdA2 && tileId < TileIdA3;
    public static bool IsTileA3(int tileId) => tileId >= TileIdA3 && tileId < TileIdA4;
    public static bool IsTileA4(int tileId) => tileId >= TileIdA4 && tileId < TileIdMax;
    public static bool IsTileA5(int tileId) => tileId >= TileIdA5 && tileId < TileIdA1;

    public static bool IsWaterTile(int tileId) =>
        IsTileA1(tileId) && !(tileId >= TileIdA1 + 96 && tileId < TileIdA1 + 192);

    public static bool IsWaterfallTile(int tileId) =>
        tileId >= TileIdA1 + 192 && tileId < TileIdA2 && GetAutotileKind(tileId) % 2 == 1;

    public static bool IsGroundTile(int tileId) => IsTileA1(tileId) || IsTileA2(tileId) || IsTileA5(tileId);
    public static bool IsShadowingTile(int tileId) => IsTileA3(tileId) || IsTileA4(tileId);
    public static bool IsRoofTile(int tileId) => IsTileA3(tileId) && GetAutotileKind(tileId) % 16 < 8;
    public static bool IsWallTopTile(int tileId) => IsTileA4(tileId) && GetAutotileKind(tileId) % 16 < 8;
    public static bool IsWallSideTile(int tileId) =>
        (IsTileA3(tileId) || IsTileA4(tileId)) && GetAutotileKind(tileId) % 16 >= 8;
    public static bool IsWallTile(int tileId) => IsWallTopTile(tileId) || IsWallSideTile(tileId);
    public static bool IsFloorTypeAutotile(int tileId) =>
        (IsTileA1(tileId) && !IsWaterfallTile(tileId)) || IsTileA2(tileId) || IsWallTopTile(tileId);
    public static bool IsWallTypeAutotile(int tileId) => IsRoofTile(tileId) || IsWallSideTile(tileId);
    public static bool IsWaterfallTypeAutotile(int tileId) => IsWaterfallTile(tileId);

    public static bool IsSameKindTile(int first, int second) =>
        IsAutotile(first) && IsAutotile(second)
            ? GetAutotileKind(first) == GetAutotileKind(second)
            : first == second;

    public static readonly QuarterCoordinate[][] FloorAutotileTable =
    [
        Q(2,4,1,4,2,3,1,3), Q(2,0,1,4,2,3,1,3), Q(2,4,3,0,2,3,1,3), Q(2,0,3,0,2,3,1,3),
        Q(2,4,1,4,2,3,3,1), Q(2,0,1,4,2,3,3,1), Q(2,4,3,0,2,3,3,1), Q(2,0,3,0,2,3,3,1),
        Q(2,4,1,4,2,1,1,3), Q(2,0,1,4,2,1,1,3), Q(2,4,3,0,2,1,1,3), Q(2,0,3,0,2,1,1,3),
        Q(2,4,1,4,2,1,3,1), Q(2,0,1,4,2,1,3,1), Q(2,4,3,0,2,1,3,1), Q(2,0,3,0,2,1,3,1),
        Q(0,4,1,4,0,3,1,3), Q(0,4,3,0,0,3,1,3), Q(0,4,1,4,0,3,3,1), Q(0,4,3,0,0,3,3,1),
        Q(2,2,1,2,2,3,1,3), Q(2,2,1,2,2,3,3,1), Q(2,2,1,2,2,1,1,3), Q(2,2,1,2,2,1,3,1),
        Q(2,4,3,4,2,3,3,3), Q(2,4,3,4,2,1,3,3), Q(2,0,3,4,2,3,3,3), Q(2,0,3,4,2,1,3,3),
        Q(2,4,1,4,2,5,1,5), Q(2,0,1,4,2,5,1,5), Q(2,4,3,0,2,5,1,5), Q(2,0,3,0,2,5,1,5),
        Q(0,4,3,4,0,3,3,3), Q(2,2,1,2,2,5,1,5), Q(0,2,1,2,0,3,1,3), Q(0,2,1,2,0,3,3,1),
        Q(2,2,3,2,2,3,3,3), Q(2,2,3,2,2,1,3,3), Q(2,4,3,4,2,5,3,5), Q(2,0,3,4,2,5,3,5),
        Q(0,4,1,4,0,5,1,5), Q(0,4,3,0,0,5,1,5), Q(0,2,3,2,0,3,3,3), Q(0,2,1,2,0,5,1,5),
        Q(0,4,3,4,0,5,3,5), Q(2,2,3,2,2,5,3,5), Q(0,2,3,2,0,5,3,5), Q(0,0,1,0,0,1,1,1)
    ];

    public static readonly QuarterCoordinate[][] WallAutotileTable =
    [
        Q(2,2,1,2,2,1,1,1), Q(0,2,1,2,0,1,1,1), Q(2,0,1,0,2,1,1,1), Q(0,0,1,0,0,1,1,1),
        Q(2,2,3,2,2,1,3,1), Q(0,2,3,2,0,1,3,1), Q(2,0,3,0,2,1,3,1), Q(0,0,3,0,0,1,3,1),
        Q(2,2,1,2,2,3,1,3), Q(0,2,1,2,0,3,1,3), Q(2,0,1,0,2,3,1,3), Q(0,0,1,0,0,3,1,3),
        Q(2,2,3,2,2,3,3,3), Q(0,2,3,2,0,3,3,3), Q(2,0,3,0,2,3,3,3), Q(0,0,3,0,0,3,3,3)
    ];

    public static readonly QuarterCoordinate[][] WaterfallAutotileTable =
    [
        Q(2,0,1,0,2,1,1,1), Q(0,0,1,0,0,1,1,1), Q(2,0,3,0,2,1,3,1), Q(0,0,3,0,0,1,3,1)
    ];

    private static QuarterCoordinate[] Q(params int[] values)
    {
        if (values.Length != 8) throw new ArgumentException("A quarter table row requires eight coordinates.", nameof(values));
        return
        [
            new(values[0], values[1]),
            new(values[2], values[3]),
            new(values[4], values[5]),
            new(values[6], values[7])
        ];
    }
}

public readonly record struct QuarterCoordinate(int X, int Y);

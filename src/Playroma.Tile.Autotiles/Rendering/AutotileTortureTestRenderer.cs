using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Services;

namespace Playroma.Tile.Autotiles.Rendering;

public sealed class AutotileTortureTestRenderer
{
    private readonly MvAutotileRenderer _renderer = new();

    public Bitmap Render(Bitmap source, AutotileKind kind, int frame = 0)
    {
        var cells = CreatePattern(kind);
        return _renderer.RenderMap(source, kind, cells, frame, true);
    }

    public static bool[,] CreatePattern(AutotileKind kind)
    {
        var family = AutotileKindInfo.GetFamily(kind);
        var map = new bool[20, 15];
        FillRect(map, 1, 1, 1, 1);
        FillRect(map, 3, 1, 2, 2);
        FillRect(map, 7, 1, 5, 1);
        FillRect(map, 14, 1, 1, 5);
        FillRect(map, 1, 5, 5, 4);
        map[3, 6] = false;
        map[4, 7] = false;
        for (var i = 0; i < 5; i++) { map[8 + i, 5 + i] = true; map[8 + i, 6 + i] = true; }
        FillRect(map, 14, 7, 5, 5);
        FillRect(map, 15, 8, 3, 3, false);
        for (var x = 1; x < 12; x++) if (x % 2 == 0) map[x, 12] = true;
        if (family == AutotileFamily.Waterfall)
        {
            Array.Clear(map, 0, map.Length);
            FillRect(map, 1, 1, 6, 3);
            FillRect(map, 9, 1, 1, 8);
            FillRect(map, 12, 2, 7, 6);
        }
        return map;
    }

    private static void FillRect(bool[,] map, int x, int y, int width, int height, bool value = true)
    {
        for (var py = y; py < y + height && py < map.GetLength(1); py++)
        for (var px = x; px < x + width && px < map.GetLength(0); px++)
            if (px >= 0 && py >= 0) map[px, py] = value;
    }
}

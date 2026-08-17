using System.Drawing;
using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;

namespace Playroma.Tile.Autotiles.Services;

public static class AutotileTemplateFactory
{
    private const int Q = 24;

    public static Bitmap CreateDefault(AutotileKind kind)
    {
        var size = AutotileKindInfo.GetSourceSize(kind);
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var colors = GetColors(kind);
        switch (AutotileKindInfo.GetFamily(kind))
        {
            case AutotileFamily.Floor:
                for (var frame = 0; frame < AutotileKindInfo.GetFrameCount(kind); frame++)
                {
                    var x = kind is AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water
                        ? frame * 96 : 0;
                    DrawFloorBlock(bitmap, new Point(x, 0), Shift(colors.Primary, frame * 5), colors.Edge, frame);
                }
                break;
            case AutotileFamily.Wall:
                DrawWallBlock(bitmap, Point.Empty, colors.Primary, colors.Edge);
                break;
            case AutotileFamily.Waterfall:
                for (var frame = 0; frame < 3; frame++)
                    DrawWaterfallFrame(bitmap, new Point(0, frame * 48), Shift(colors.Primary, frame * 7), colors.Edge, frame);
                break;
        }
        return bitmap;
    }

    private static void DrawFloorBlock(Bitmap bitmap, Point origin, Color fill, Color edge, int phase)
    {
        var cells = new Dictionary<Point, CellRole>
        {
            [new(0,0)] = CellRole.Full, [new(1,0)] = CellRole.Full,
            [new(0,1)] = CellRole.Full, [new(1,1)] = CellRole.Full,
            [new(2,0)] = CellRole.InnerTopLeft, [new(3,0)] = CellRole.InnerTopRight,
            [new(2,1)] = CellRole.InnerBottomLeft, [new(3,1)] = CellRole.InnerBottomRight,
            [new(0,2)] = CellRole.OuterTopLeft, [new(1,2)] = CellRole.TopEdge,
            [new(2,2)] = CellRole.TopEdge, [new(3,2)] = CellRole.OuterTopRight,
            [new(0,3)] = CellRole.LeftEdge, [new(1,3)] = CellRole.Full,
            [new(2,3)] = CellRole.Full, [new(3,3)] = CellRole.RightEdge,
            [new(0,4)] = CellRole.LeftEdge, [new(1,4)] = CellRole.Full,
            [new(2,4)] = CellRole.Full, [new(3,4)] = CellRole.RightEdge,
            [new(0,5)] = CellRole.OuterBottomLeft, [new(1,5)] = CellRole.BottomEdge,
            [new(2,5)] = CellRole.BottomEdge, [new(3,5)] = CellRole.OuterBottomRight
        };
        foreach (var pair in cells)
            PaintCell(bitmap, origin.X + pair.Key.X * Q, origin.Y + pair.Key.Y * Q, pair.Value, fill, edge, phase);
    }

    private static void DrawWallBlock(Bitmap bitmap, Point origin, Color fill, Color edge)
    {
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
        {
            var role = (x, y) switch
            {
                (0,0) => CellRole.OuterTopLeft, (1,0) => CellRole.TopEdge,
                (2,0) => CellRole.TopEdge, (3,0) => CellRole.OuterTopRight,
                (0,1) => CellRole.LeftEdge, (1,1) => CellRole.Full,
                (2,1) => CellRole.Full, (3,1) => CellRole.RightEdge,
                (0,2) => CellRole.LeftEdge, (1,2) => CellRole.Full,
                (2,2) => CellRole.Full, (3,2) => CellRole.RightEdge,
                (0,3) => CellRole.OuterBottomLeft, (1,3) => CellRole.BottomEdge,
                (2,3) => CellRole.BottomEdge, _ => CellRole.OuterBottomRight
            };
            PaintCell(bitmap, origin.X + x * Q, origin.Y + y * Q, role, fill, edge, 0);
        }
    }

    private static void DrawWaterfallFrame(Bitmap bitmap, Point origin, Color fill, Color edge, int phase)
    {
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 4; x++)
        {
            var role = x == 0 ? CellRole.LeftEdge : x == 3 ? CellRole.RightEdge : CellRole.Full;
            PaintCell(bitmap, origin.X + x * Q, origin.Y + y * Q, role, fill, edge, phase);
            for (var stripe = -Q; stripe < Q * 2; stripe += 8)
            {
                var sx = origin.X + x * Q + stripe + phase * 2;
                for (var py = 0; py < Q; py++)
                {
                    var px = sx + py / 3;
                    if (px >= origin.X + x * Q && px < origin.X + (x + 1) * Q)
                    {
                        var c = bitmap.GetPixel(px, origin.Y + y * Q + py);
                        if (c.A > 0) bitmap.SetPixel(px, origin.Y + y * Q + py, Shift(c, 22));
                    }
                }
            }
        }
    }

    private static void PaintCell(Bitmap bitmap, int ox, int oy, CellRole role, Color fill, Color edge, int phase)
    {
        for (var y = 0; y < Q; y++)
        for (var x = 0; x < Q; x++)
        {
            var inside = IsInside(role, x, y);
            if (!inside)
            {
                bitmap.SetPixel(ox + x, oy + y, Color.Transparent);
                continue;
            }
            var boundary = IsBoundary(role, x, y);
            var checker = ((x + phase * 3) / 6 + (y + phase * 2) / 6) % 2 == 0;
            bitmap.SetPixel(ox + x, oy + y, boundary ? edge : checker ? fill : Shift(fill, 9));
        }
    }

    private static bool IsInside(CellRole role, int x, int y) => role switch
    {
        CellRole.Full => true,
        CellRole.LeftEdge => x >= 4,
        CellRole.RightEdge => x <= 19,
        CellRole.TopEdge => y >= 4,
        CellRole.BottomEdge => y <= 19,
        CellRole.OuterTopLeft => x >= 4 && y >= 4,
        CellRole.OuterTopRight => x <= 19 && y >= 4,
        CellRole.OuterBottomLeft => x >= 4 && y <= 19,
        CellRole.OuterBottomRight => x <= 19 && y <= 19,
        CellRole.InnerTopLeft => !(x < 5 && y < 5),
        CellRole.InnerTopRight => !(x > 18 && y < 5),
        CellRole.InnerBottomLeft => !(x < 5 && y > 18),
        CellRole.InnerBottomRight => !(x > 18 && y > 18),
        _ => true
    };

    private static bool IsBoundary(CellRole role, int x, int y)
    {
        if (!IsInside(role, x, y)) return false;
        return role switch
        {
            CellRole.LeftEdge => x is 4 or 5,
            CellRole.RightEdge => x is 18 or 19,
            CellRole.TopEdge => y is 4 or 5,
            CellRole.BottomEdge => y is 18 or 19,
            CellRole.OuterTopLeft => x is 4 or 5 || y is 4 or 5,
            CellRole.OuterTopRight => x is 18 or 19 || y is 4 or 5,
            CellRole.OuterBottomLeft => x is 4 or 5 || y is 18 or 19,
            CellRole.OuterBottomRight => x is 18 or 19 || y is 18 or 19,
            CellRole.InnerTopLeft => (x is 4 or 5 && y < 6) || (y is 4 or 5 && x < 6),
            CellRole.InnerTopRight => (x is 18 or 19 && y < 6) || (y is 4 or 5 && x > 17),
            CellRole.InnerBottomLeft => (x is 4 or 5 && y > 17) || (y is 18 or 19 && x < 6),
            CellRole.InnerBottomRight => (x is 18 or 19 && y > 17) || (y is 18 or 19 && x > 17),
            _ => false
        };
    }

    private static (Color Primary, Color Edge) GetColors(AutotileKind kind) => kind switch
    {
        AutotileKind.A1Ocean => (Color.FromArgb(255, 45, 120, 188), Color.FromArgb(255, 190, 225, 245)),
        AutotileKind.A1DeepOcean => (Color.FromArgb(255, 31, 70, 135), Color.FromArgb(255, 86, 150, 205)),
        AutotileKind.A1Water or AutotileKind.A1Waterfall => (Color.FromArgb(255, 53, 152, 198), Color.FromArgb(255, 205, 239, 247)),
        AutotileKind.A3Building => (Color.FromArgb(255, 139, 77, 58), Color.FromArgb(255, 78, 42, 34)),
        AutotileKind.A4Wall => (Color.FromArgb(255, 126, 126, 131), Color.FromArgb(255, 69, 69, 73)),
        AutotileKind.A4Floor => (Color.FromArgb(255, 106, 96, 83), Color.FromArgb(255, 60, 53, 45)),
        AutotileKind.A1OceanDecoration => (Color.FromArgb(220, 75, 166, 112), Color.FromArgb(240, 214, 239, 197)),
        _ => (Color.FromArgb(255, 83, 155, 73), Color.FromArgb(255, 43, 92, 39))
    };

    private static Color Shift(Color color, int amount) => Color.FromArgb(color.A,
        Math.Clamp(color.R + amount, 0, 255), Math.Clamp(color.G + amount, 0, 255), Math.Clamp(color.B + amount, 0, 255));

    private enum CellRole
    {
        Full, LeftEdge, RightEdge, TopEdge, BottomEdge,
        OuterTopLeft, OuterTopRight, OuterBottomLeft, OuterBottomRight,
        InnerTopLeft, InnerTopRight, InnerBottomLeft, InnerBottomRight
    }
}

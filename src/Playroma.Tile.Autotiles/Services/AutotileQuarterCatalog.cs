using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;

namespace Playroma.Tile.Autotiles.Services;

/// <summary>
/// Converts source-block pixel positions into stable, human-readable 24x24
/// quarter components. The catalogue is deliberately independent of the UI.
/// </summary>
public static class AutotileQuarterCatalog
{
    public const int QuarterSize = 24;

    public static AutotileQuarterSelection Locate(AutotileKind kind, Point sourcePixel)
    {
        var sourceSize = AutotileKindInfo.GetSourceSize(kind);
        if (sourcePixel.X < 0 || sourcePixel.Y < 0 ||
            sourcePixel.X >= sourceSize.Width || sourcePixel.Y >= sourceSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePixel));
        }

        var frame = 0;
        var localX = sourcePixel.X;
        var localY = sourcePixel.Y;
        if (kind is AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water)
        {
            frame = sourcePixel.X / 96;
            localX %= 96;
        }
        else if (kind == AutotileKind.A1Waterfall)
        {
            frame = sourcePixel.Y / 48;
            localY %= 48;
        }

        var quarterX = localX / QuarterSize;
        var quarterY = localY / QuarterSize;
        return Create(kind, frame, quarterX, quarterY);
    }

    public static AutotileQuarterSelection Create(AutotileKind kind, int frame, int quarterX, int quarterY)
    {
        var frameCount = AutotileKindInfo.GetFrameCount(kind);
        if (frame < 0 || frame >= frameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        var family = AutotileKindInfo.GetFamily(kind);
        var columns = 4;
        var rows = family switch
        {
            AutotileFamily.Floor => 6,
            AutotileFamily.Wall => 4,
            AutotileFamily.Waterfall => 2,
            _ => throw new ArgumentOutOfRangeException()
        };
        if (quarterX < 0 || quarterX >= columns) throw new ArgumentOutOfRangeException(nameof(quarterX));
        if (quarterY < 0 || quarterY >= rows) throw new ArgumentOutOfRangeException(nameof(quarterY));

        var frameOrigin = MvAutotileRenderer.GetFrameOrigin(kind, frame);
        var bounds = new Rectangle(
            frameOrigin.X + quarterX * QuarterSize,
            frameOrigin.Y + quarterY * QuarterSize,
            QuarterSize,
            QuarterSize);
        return new AutotileQuarterSelection(frame, quarterX, quarterY, bounds,
            GetLabel(family, quarterX, quarterY));
    }

    public static IReadOnlyList<AutotileQuarterSelection> GetAll(AutotileKind kind, int frame = 0)
    {
        var family = AutotileKindInfo.GetFamily(kind);
        var rows = family switch
        {
            AutotileFamily.Floor => 6,
            AutotileFamily.Wall => 4,
            _ => 2
        };
        var result = new List<AutotileQuarterSelection>(4 * rows);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < 4; x++)
            result.Add(Create(kind, frame, x, y));
        return result;
    }

    private static string GetLabel(AutotileFamily family, int x, int y) => family switch
    {
        AutotileFamily.Floor => GetFloorLabel(x, y),
        AutotileFamily.Wall => GetWallLabel(x, y),
        AutotileFamily.Waterfall => GetWaterfallLabel(x, y),
        _ => $"Quarter {x}, {y}"
    };

    private static string GetFloorLabel(int x, int y) => (x, y) switch
    {
        (0, 0) => "Fixed representative · top-left",
        (1, 0) => "Fixed representative · top-right",
        (0, 1) => "Fixed representative · bottom-left",
        (1, 1) => "Fixed representative · bottom-right",
        (2, 0) => "Inner corner · north-west",
        (3, 0) => "Inner corner · north-east",
        (2, 1) => "Inner corner · south-west",
        (3, 1) => "Inner corner · south-east",
        (0, 2) => "Outer corner · north-west",
        (1, 2) or (2, 2) => "North edge",
        (3, 2) => "Outer corner · north-east",
        (0, 3) or (0, 4) => "West edge",
        (1, 3) or (2, 3) or (1, 4) or (2, 4) => "Interior",
        (3, 3) or (3, 4) => "East edge",
        (0, 5) => "Outer corner · south-west",
        (1, 5) or (2, 5) => "South edge",
        (3, 5) => "Outer corner · south-east",
        _ => $"Floor quarter {x}, {y}"
    };

    private static string GetWallLabel(int x, int y) => (x, y) switch
    {
        (0, 0) => "Outer corner · north-west",
        (1, 0) or (2, 0) => "North edge",
        (3, 0) => "Outer corner · north-east",
        (0, 1) or (0, 2) => "West edge",
        (1, 1) or (2, 1) or (1, 2) or (2, 2) => "Wall interior / repeat",
        (3, 1) or (3, 2) => "East edge",
        (0, 3) => "Outer corner · south-west",
        (1, 3) or (2, 3) => "South edge",
        (3, 3) => "Outer corner · south-east",
        _ => $"Wall quarter {x}, {y}"
    };

    private static string GetWaterfallLabel(int x, int y) => (x, y) switch
    {
        (0, 0) => "Exposed west edge · top",
        (0, 1) => "Exposed west edge · bottom",
        (1, 0) => "Connected east half · top",
        (1, 1) => "Connected east half · bottom",
        (2, 0) => "Connected west half · top",
        (2, 1) => "Connected west half · bottom",
        (3, 0) => "Exposed east edge · top",
        (3, 1) => "Exposed east edge · bottom",
        _ => $"Waterfall quarter {x}, {y}"
    };
}

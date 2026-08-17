using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Services;

namespace Playroma.Tile.Autotiles.Rendering;

public sealed class MvAutotileRenderer
{
    public const int TileSize = 48;
    public const int QuarterSize = 24;
    private readonly AutotileShapeResolver _resolver = new();

    public Bitmap RenderShape(Bitmap source, AutotileKind kind, int shape, int frame = 0)
    {
        ValidateSource(source, kind);
        var family = AutotileKindInfo.GetFamily(kind);
        var table = family switch
        {
            AutotileFamily.Floor => MvAutotileTables.Floor,
            AutotileFamily.Wall => MvAutotileTables.Wall,
            AutotileFamily.Waterfall => MvAutotileTables.Waterfall,
            _ => throw new ArgumentOutOfRangeException()
        };
        if (shape < 0 || shape >= table.Length) throw new ArgumentOutOfRangeException(nameof(shape));
        frame = Math.Clamp(frame, 0, AutotileKindInfo.GetFrameCount(kind) - 1);

        var result = new Bitmap(TileSize, TileSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var origin = GetFrameOrigin(kind, frame);
        for (var i = 0; i < 4; i++)
        {
            var quarter = table[shape][i];
            var sourceRectangle = new Rectangle(
                origin.X + quarter.X * QuarterSize,
                origin.Y + quarter.Y * QuarterSize,
                QuarterSize,
                QuarterSize);
            var destination = new Rectangle(
                i % 2 * QuarterSize,
                i / 2 * QuarterSize,
                QuarterSize,
                QuarterSize);
            graphics.DrawImage(source, destination, sourceRectangle, GraphicsUnit.Pixel);
        }
        return result;
    }

    public Bitmap RenderShapeAtlas(Bitmap source, AutotileKind kind, int frame = 0, int columns = 8)
    {
        ValidateSource(source, kind);
        var shapeCount = AutotileKindInfo.GetFamily(kind) switch
        {
            AutotileFamily.Floor => 48,
            AutotileFamily.Wall => 16,
            _ => 4
        };
        columns = Math.Clamp(columns, 1, shapeCount);
        var rows = (int)Math.Ceiling(shapeCount / (double)columns);
        var output = new Bitmap(columns * TileSize, rows * TileSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var shape = 0; shape < shapeCount; shape++)
        {
            using var tile = RenderShape(source, kind, shape, frame);
            graphics.DrawImageUnscaled(tile, shape % columns * TileSize, shape / columns * TileSize);
        }
        return output;
    }

    public Bitmap RenderMap(Bitmap source, AutotileKind kind, bool[,] cells, int frame = 0, bool drawGrid = false)
    {
        var width = cells.GetLength(0);
        var height = cells.GetLength(1);
        var output = new Bitmap(Math.Max(1, width) * TileSize, Math.Max(1, height) * TileSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!cells[x, y]) continue;
            var mask = GetNeighborMask(cells, x, y);
            var shape = _resolver.Resolve(AutotileKindInfo.GetFamily(kind), mask);
            using var tile = RenderShape(source, kind, shape, frame);
            graphics.DrawImageUnscaled(tile, x * TileSize, y * TileSize);
        }

        if (drawGrid)
        {
            using var pen = new Pen(Color.FromArgb(90, Color.Black));
            for (var x = 0; x <= width; x++) graphics.DrawLine(pen, x * TileSize, 0, x * TileSize, height * TileSize);
            for (var y = 0; y <= height; y++) graphics.DrawLine(pen, 0, y * TileSize, width * TileSize, y * TileSize);
        }
        return output;
    }

    public static NeighborMask GetNeighborMask(bool[,] cells, int x, int y)
    {
        var mask = NeighborMask.None;
        if (Get(cells, x - 1, y - 1)) mask |= NeighborMask.NorthWest;
        if (Get(cells, x, y - 1)) mask |= NeighborMask.North;
        if (Get(cells, x + 1, y - 1)) mask |= NeighborMask.NorthEast;
        if (Get(cells, x - 1, y)) mask |= NeighborMask.West;
        if (Get(cells, x + 1, y)) mask |= NeighborMask.East;
        if (Get(cells, x - 1, y + 1)) mask |= NeighborMask.SouthWest;
        if (Get(cells, x, y + 1)) mask |= NeighborMask.South;
        if (Get(cells, x + 1, y + 1)) mask |= NeighborMask.SouthEast;
        return mask;
    }

    public static Point GetFrameOrigin(AutotileKind kind, int frame) => kind switch
    {
        AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water => new(frame * 96, 0),
        AutotileKind.A1Waterfall => new(0, frame * 48),
        _ => Point.Empty
    };

    public static void ValidateSource(Bitmap source, AutotileKind kind)
    {
        var expected = AutotileKindInfo.GetSourceSize(kind);
        if (source.Width != expected.Width || source.Height != expected.Height)
            throw new InvalidDataException($"{kind} requires a {expected.Width} × {expected.Height} source image, but the image is {source.Width} × {source.Height}.");
    }

    private static bool Get(bool[,] cells, int x, int y) =>
        x >= 0 && y >= 0 && x < cells.GetLength(0) && y < cells.GetLength(1) && cells[x, y];
}

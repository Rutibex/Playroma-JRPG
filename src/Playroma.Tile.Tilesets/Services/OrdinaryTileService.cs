using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class OrdinaryTileService
{
    public TileObjectDefinition ImportObject(
        TileProject project,
        string imagePath,
        string? name = null,
        TileObjectCategory category = TileObjectCategory.Uncategorized,
        bool cleanAlpha = true,
        bool padToGrid = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        using var source = BitmapFile.LoadEditable(imagePath);
        using var prepared = Prepare(source, cleanAlpha, padToGrid);
        var definition = new TileObjectDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(imagePath) : name.Trim(),
            Category = category,
            GridWidth = Math.Max(1, prepared.Width / 48),
            GridHeight = Math.Max(1, prepared.Height / 48),
            AnchorCellX = Math.Max(0, prepared.Width / 48 / 2),
            AnchorCellY = Math.Max(0, prepared.Height / 48 - 1),
            OccupiedPixelWidth = prepared.Width,
            OccupiedPixelHeight = prepared.Height,
            CollisionCellX = Math.Max(0, prepared.Width / 48 / 2),
            CollisionCellY = Math.Max(0, prepared.Height / 48 - 1)
        };
        var relative = ProjectPath.ToPortableRelativePath(Path.Combine("Objects", definition.Id.ToString("N"), "object.png"));
        var destination = ProjectPath.Resolve(project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        BitmapFile.SavePngAtomic(prepared, destination);
        definition.RelativeImagePath = relative;
        definition.Frames.Add(new TileObjectFrame { Name = "Default", RelativeImagePath = relative });
        definition.Tags.Add(category.ToString());
        return definition;
    }

    public Bitmap Prepare(Bitmap source, bool cleanAlpha, bool padToGrid)
    {
        var width = padToGrid ? Math.Max(48, ((source.Width + 47) / 48) * 48) : source.Width;
        var height = padToGrid ? Math.Max(48, ((source.Height + 47) / 48) * 48) : source.Height;
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        if (cleanAlpha) CleanAlpha(result, 8, 247);
        return result;
    }

    public static void CleanAlpha(Bitmap bitmap, byte transparentThreshold = 8, byte opaqueThreshold = 247)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = row + x * 4;
                        var alpha = pixel[3];
                        if (alpha <= transparentThreshold)
                        {
                            pixel[0] = pixel[1] = pixel[2] = pixel[3] = 0;
                        }
                        else if (alpha >= opaqueThreshold)
                        {
                            pixel[3] = 255;
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public IReadOnlyList<Bitmap> SliceCells(Bitmap source)
    {
        var cells = new List<Bitmap>();
        for (var y = 0; y < source.Height; y += 48)
        for (var x = 0; x < source.Width; x += 48)
        {
            var cell = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(cell);
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, new Rectangle(0, 0, 48, 48), new Rectangle(x, y, 48, 48), GraphicsUnit.Pixel);
            cells.Add(cell);
        }
        return cells;
    }

    public Bitmap CreateVariation(Bitmap source, float hueShiftDegrees, float saturationScale, float brightnessScale,
        bool addWear, int seed)
    {
        var result = (Bitmap)source.Clone();
        var random = new Random(seed);
        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var color = result.GetPixel(x, y);
            if (color.A == 0) continue;
            ColorToHsv(color, out var h, out var s, out var v);
            h = (h + hueShiftDegrees + 360f) % 360f;
            s = Math.Clamp(s * saturationScale, 0f, 1f);
            v = Math.Clamp(v * brightnessScale, 0f, 1f);
            var alpha = color.A;
            if (addWear && random.NextDouble() < 0.035)
            {
                v *= 0.65f;
                s *= 0.7f;
            }
            result.SetPixel(x, y, ColorFromHsv(alpha, h, s, v));
        }
        return result;
    }

    private static void ColorToHsv(Color color, out float h, out float s, out float v)
    {
        h = color.GetHue();
        s = color.GetSaturation();
        v = color.GetBrightness();
    }

    private static Color ColorFromHsv(byte alpha, float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60f % 2) - 1));
        var m = value - c;
        (float r, float g, float b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return Color.FromArgb(alpha,
            (int)Math.Clamp((r + m) * 255, 0, 255),
            (int)Math.Clamp((g + m) * 255, 0, 255),
            (int)Math.Clamp((b + m) * 255, 0, 255));
    }
}

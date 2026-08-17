using System.Drawing;
using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;

namespace Playroma.Tile.Autotiles.Services;

/// <summary>
/// Creates a structurally valid MV source block from a material exemplar.
/// Phase 2 intentionally keeps this deterministic and simple; advanced texture
/// synthesis belongs to Phase 3.
/// </summary>
public sealed class AutotileMaterialComposer
{
    public Bitmap Compose(
        AutotileKind kind,
        Image material,
        int boundaryDarkening = 30,
        int boundaryWidth = 2,
        bool preserveMaterialAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Width < 1 || material.Height < 1)
            throw new ArgumentException("The material image is empty.", nameof(material));
        boundaryDarkening = Math.Clamp(boundaryDarkening, 0, 180);
        boundaryWidth = Math.Clamp(boundaryWidth, 0, 6);

        using var texture = AutotileBitmap.CloneArgb(material);
        using var mask = AutotileTemplateFactory.CreateDefault(kind);
        var result = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < result.Height; y++)
        for (var x = 0; x < result.Width; x++)
        {
            var maskAlpha = mask.GetPixel(x, y).A;
            if (maskAlpha == 0)
            {
                result.SetPixel(x, y, Color.Transparent);
                continue;
            }

            var samplePoint = GetMaterialSample(kind, x, y, texture.Size);
            var sample = texture.GetPixel(samplePoint.X, samplePoint.Y);
            var alpha = preserveMaterialAlpha ? sample.A * maskAlpha / 255 : maskAlpha;
            if (alpha == 0)
            {
                result.SetPixel(x, y, Color.Transparent);
                continue;
            }

            var darken = boundaryWidth > 0 && IsBoundary(mask, x, y, boundaryWidth)
                ? boundaryDarkening
                : 0;
            result.SetPixel(x, y, Color.FromArgb(alpha,
                Math.Max(0, sample.R - darken),
                Math.Max(0, sample.G - darken),
                Math.Max(0, sample.B - darken)));
        }
        return result;
    }

    private static Point GetMaterialSample(AutotileKind kind, int x, int y, Size materialSize)
    {
        var frame = 0;
        var localX = x;
        var localY = y;
        if (kind is AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water)
        {
            frame = x / 96;
            localX %= 96;
        }
        else if (kind == AutotileKind.A1Waterfall)
        {
            frame = y / 48;
            localY %= 48;
        }

        // Small deterministic offsets make the three A1 frames visibly animate
        // while preserving a perfect loop and identical source dimensions.
        var offsetX = kind == AutotileKind.A1Waterfall ? frame * 3 : frame * 2;
        var offsetY = kind == AutotileKind.A1Waterfall ? frame * 5 : frame;
        return new Point(
            PositiveModulo(localX + offsetX, materialSize.Width),
            PositiveModulo(localY + offsetY, materialSize.Height));
    }

    private static bool IsBoundary(Bitmap mask, int x, int y, int radius)
    {
        for (var oy = -radius; oy <= radius; oy++)
        for (var ox = -radius; ox <= radius; ox++)
        {
            if (Math.Abs(ox) + Math.Abs(oy) > radius) continue;
            var px = x + ox;
            var py = y + oy;
            if (px < 0 || py < 0 || px >= mask.Width || py >= mask.Height || mask.GetPixel(px, py).A == 0)
                return true;
        }
        return false;
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Playroma.Tile.RpgMakerMV.Imaging;

public static class RegionImageEditor
{
    public static Bitmap Extract(Bitmap source, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRegion(source, region);
        return source.Clone(region, PixelFormat.Format32bppArgb);
    }

    public static void Clear(Bitmap target, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateRegion(target, region);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var transparent = new SolidBrush(Color.Transparent);
        graphics.FillRectangle(transparent, region);
    }

    public static void Paste(Bitmap target, Rectangle region, Image source, bool scaleToFit)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ValidateRegion(target, region);

        if (!scaleToFit && (source.Width != region.Width || source.Height != region.Height))
        {
            throw new ArgumentException(
                $"The source image is {source.Width}x{source.Height}, but the selected region is {region.Width}x{region.Height}.",
                nameof(source));
        }

        using var graphics = Graphics.FromImage(target);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var transparent = new SolidBrush(Color.Transparent);
        graphics.FillRectangle(transparent, region);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.DrawImage(source, region);
    }

    public static bool IsFullyTransparent(Bitmap bitmap, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ValidateRegion(bitmap, region);
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ValidateRegion(Bitmap bitmap, Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0 ||
            region.Left < 0 || region.Top < 0 ||
            region.Right > bitmap.Width || region.Bottom > bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "The region is outside the image bounds.");
        }
    }
}

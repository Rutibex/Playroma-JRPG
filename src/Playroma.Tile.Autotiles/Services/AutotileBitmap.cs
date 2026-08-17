using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace Playroma.Tile.Autotiles.Services;

public static class AutotileBitmap
{
    public static Bitmap LoadEditable(string path)
    {
        using var source = new Bitmap(path);
        return CloneArgb(source);
    }

    public static Bitmap CloneArgb(Image source)
    {
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(source.HorizontalResolution > 0 ? source.HorizontalResolution : 96,
            source.VerticalResolution > 0 ? source.VerticalResolution : 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return bitmap;
    }

    public static Bitmap Crop(Image source, Rectangle bounds)
    {
        if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > source.Width || bounds.Bottom > source.Height)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImage(source, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
        return result;
    }

    public static void SavePngAtomic(Image image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        image.Save(temporary, ImageFormat.Png);
        File.Move(temporary, path, true);
    }

    public static string ComputeSha256(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

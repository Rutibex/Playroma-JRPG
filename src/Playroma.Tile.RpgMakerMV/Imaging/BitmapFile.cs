using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace Playroma.Tile.RpgMakerMV.Imaging;

public static class BitmapFile
{
    public static Bitmap LoadEditable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var source = new Bitmap(path);
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(copy);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    public static Size ReadSize(string path)
    {
        using var image = Image.FromFile(path);
        return image.Size;
    }

    public static void SavePngAtomic(Bitmap bitmap, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp.png";
        bitmap.Save(temporaryPath, ImageFormat.Png);
        File.Move(temporaryPath, fullPath, true);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

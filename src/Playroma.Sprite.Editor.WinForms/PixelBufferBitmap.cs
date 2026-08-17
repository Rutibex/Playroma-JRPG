using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

internal static class PixelBufferBitmap
{
    public static Bitmap ToBitmap(PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Bitmap bitmap = new(pixels.Width, pixels.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                {
                    Rgba32 color = pixels.GetPixel(x, y);
                    int offset = y * data.Stride + x * 4;
                    bytes[offset] = color.B; bytes[offset + 1] = color.G; bytes[offset + 2] = color.R; bytes[offset + 3] = color.A;
                }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    public static Bitmap Crop(PixelBuffer pixels, IntRect source)
    {
        PixelBuffer cropped = new(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int sx = source.X + x, sy = source.Y + y;
                if (sx >= 0 && sx < pixels.Width && sy >= 0 && sy < pixels.Height) cropped.SetPixel(x, y, pixels.GetPixel(sx, sy));
            }
        return ToBitmap(cropped);
    }
}

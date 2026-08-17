using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Playroma.JRPG.Rendering.Windows;

public static class BitmapPixelConverter
{
    public static RgbaImage ToRgbaImage(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using Bitmap normalized = new(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(normalized))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(bitmap, 0, 0);
        }

        Rectangle bounds = new(0, 0, normalized.Width, normalized.Height);
        BitmapData data = normalized.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] raw = GC.AllocateUninitializedArray<byte>(checked(stride * normalized.Height));
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            byte[] rgba = GC.AllocateUninitializedArray<byte>(checked(normalized.Width * normalized.Height * 4));
            for (int y = 0; y < normalized.Height; y++)
            {
                int sourceRow = data.Stride >= 0 ? y * stride : (normalized.Height - 1 - y) * stride;
                int destinationRow = y * normalized.Width * 4;
                for (int x = 0; x < normalized.Width; x++)
                {
                    int source = sourceRow + x * 4;
                    int destination = destinationRow + x * 4;
                    rgba[destination] = raw[source + 2];
                    rgba[destination + 1] = raw[source + 1];
                    rgba[destination + 2] = raw[source];
                    rgba[destination + 3] = raw[source + 3];
                }
            }
            return new RgbaImage(normalized.Width, normalized.Height, rgba, takeOwnership: true);
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }

    public static Bitmap ToBitmap(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
        Rectangle bounds = new(0, 0, image.Width, image.Height);
        BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] raw = new byte[checked(stride * image.Height)];
            ReadOnlySpan<byte> rgba = image.Pixels.Span;
            for (int y = 0; y < image.Height; y++)
            {
                int destinationRow = data.Stride >= 0 ? y * stride : (image.Height - 1 - y) * stride;
                int sourceRow = y * image.Width * 4;
                for (int x = 0; x < image.Width; x++)
                {
                    int source = sourceRow + x * 4;
                    int destination = destinationRow + x * 4;
                    raw[destination] = rgba[source + 2];
                    raw[destination + 1] = rgba[source + 1];
                    raw[destination + 2] = rgba[source];
                    raw[destination + 3] = rgba[source + 3];
                }
            }
            Marshal.Copy(raw, 0, data.Scan0, raw.Length);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

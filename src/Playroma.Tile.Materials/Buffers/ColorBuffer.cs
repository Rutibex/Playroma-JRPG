using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Buffers;

public sealed class ColorBuffer
{
    private readonly Rgba32[] _pixels;
    public int Width { get; }
    public int Height { get; }
    public Span<Rgba32> Pixels => _pixels;
    public ReadOnlySpan<Rgba32> ReadOnlyPixels => _pixels;

    public ColorBuffer(int width, int height, Rgba32 initial = default)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; _pixels = new Rgba32[checked(width * height)];
        if (initial != default) Array.Fill(_pixels, initial);
    }

    public Rgba32 this[int x, int y] { get => _pixels[y * Width + x]; set => _pixels[y * Width + x] = value; }

    public Rgba32 Sample(int x, int y, bool wrapX = true, bool wrapY = true)
    {
        x = wrapX ? Mod(x, Width) : Math.Clamp(x, 0, Width - 1);
        y = wrapY ? Mod(y, Height) : Math.Clamp(y, 0, Height - 1);
        return this[x, y];
    }

    public Rgba32 SampleBilinear(float x, float y, bool wrapX = true, bool wrapY = true)
    {
        var x0 = (int)MathF.Floor(x); var y0 = (int)MathF.Floor(y);
        var tx = x - x0; var ty = y - y0;
        var a = Rgba32.Lerp(Sample(x0, y0, wrapX, wrapY), Sample(x0 + 1, y0, wrapX, wrapY), tx);
        var b = Rgba32.Lerp(Sample(x0, y0 + 1, wrapX, wrapY), Sample(x0 + 1, y0 + 1, wrapX, wrapY), tx);
        return Rgba32.Lerp(a, b, ty);
    }

    public ColorBuffer Clone() { var result = new ColorBuffer(Width, Height); _pixels.CopyTo(result._pixels, 0); return result; }

    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, Width, Height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * Height];
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var p = this[x, y]; var i = y * data.Stride + x * 4;
                bytes[i] = p.B; bytes[i + 1] = p.G; bytes[i + 2] = p.R; bytes[i + 3] = p.A;
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    public static ColorBuffer FromBitmap(Image image)
    {
        using var bitmap = image as Bitmap is { } direct ? new Bitmap(direct) : new Bitmap(image);
        var result = new ColorBuffer(bitmap.Width, bitmap.Height);
        using var argb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(argb)) g.DrawImageUnscaled(bitmap, 0, 0);
        var rect = new Rectangle(0, 0, argb.Width, argb.Height);
        var data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * argb.Height]; Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < argb.Height; y++)
            for (var x = 0; x < argb.Width; x++)
            {
                var i = y * data.Stride + x * 4;
                result[x, y] = new Rgba32(bytes[i + 2], bytes[i + 1], bytes[i], bytes[i + 3]);
            }
        }
        finally { argb.UnlockBits(data); }
        return result;
    }

    public ColorBuffer Resize(int width, int height, bool nearest)
    {
        var result = new ColorBuffer(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = (x + 0.5f) * Width / width - 0.5f;
            var sy = (y + 0.5f) * Height / height - 0.5f;
            result[x, y] = nearest ? Sample((int)MathF.Round(sx), (int)MathF.Round(sy)) : SampleBilinear(sx, sy);
        }
        return result;
    }

    private static int Mod(int value, int modulus) { var r = value % modulus; return r < 0 ? r + modulus : r; }
}

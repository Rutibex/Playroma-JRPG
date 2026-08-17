using System.Security.Cryptography;

namespace Playroma.Sprite.Pixel;

public sealed class PixelBuffer
{
    private readonly byte[] pixels;

    public PixelBuffer(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        pixels = new byte[checked(width * height * 4)];
    }

    private PixelBuffer(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        this.pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Bytes => pixels;
    public Span<byte> WritableBytes => pixels;

    public static PixelBuffer FromRgbaBytes(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int expected = checked(width * height * 4);
        if (rgba.Length != expected) throw new ArgumentException($"Expected {expected} RGBA bytes.", nameof(rgba));
        return new PixelBuffer(width, height, rgba.ToArray());
    }

    public Rgba32 GetPixel(int x, int y)
    {
        int offset = Offset(x, y);
        return new(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    public bool TryGetPixel(int x, int y, out Rgba32 color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            color = default;
            return false;
        }
        color = GetPixel(x, y);
        return true;
    }

    public void SetPixel(int x, int y, Rgba32 color)
    {
        int offset = Offset(x, y);
        pixels[offset] = color.R;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.B;
        pixels[offset + 3] = color.A;
    }

    public bool TrySetPixel(int x, int y, Rgba32 color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
        SetPixel(x, y, color);
        return true;
    }

    public void Clear(Rgba32 color)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                SetPixel(x, y, color);
    }

    public PixelBuffer Clone() => new(Width, Height, (byte[])pixels.Clone());

    public void CopyFrom(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width != Width || source.Height != Height) throw new ArgumentException("Pixel buffer dimensions do not match.", nameof(source));
        source.pixels.CopyTo(pixels, 0);
    }

    public string ComputeSha256() => Convert.ToHexString(SHA256.HashData(pixels));

    private int Offset(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return checked((y * Width + x) * 4);
    }
}

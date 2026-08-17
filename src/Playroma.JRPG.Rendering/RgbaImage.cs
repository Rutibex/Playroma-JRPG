using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Playroma.JRPG.Rendering;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public static Rgba32 Transparent { get; } = new(0, 0, 0, 0);
    public static Rgba32 Black { get; } = new(0, 0, 0, 255);
    public static Rgba32 White { get; } = new(255, 255, 255, 255);
}

/// <summary>A deterministic, platform-neutral, straight-alpha RGBA pixel surface.</summary>
public sealed class RgbaImage
{
    private readonly byte[] _pixels;

    public RgbaImage(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        Array.Clear(_pixels);
    }

    public RgbaImage(int width, int height, byte[] pixels, bool takeOwnership = false)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("The pixel buffer length does not match the image dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        _pixels = takeOwnership ? pixels : pixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public PixelSize Size => new(Width, Height);
    public PixelRect Bounds => new(0, 0, Width, Height);
    public ReadOnlyMemory<byte> Pixels => _pixels;

    public Rgba32 GetPixel(int x, int y)
    {
        int offset = GetOffset(x, y);
        return new Rgba32(_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]);
    }

    public void SetPixel(int x, int y, Rgba32 value)
    {
        int offset = GetOffset(x, y);
        _pixels[offset] = value.R;
        _pixels[offset + 1] = value.G;
        _pixels[offset + 2] = value.B;
        _pixels[offset + 3] = value.A;
    }

    public void Clear(Rgba32 colour)
    {
        for (int offset = 0; offset < _pixels.Length; offset += 4)
        {
            _pixels[offset] = colour.R;
            _pixels[offset + 1] = colour.G;
            _pixels[offset + 2] = colour.B;
            _pixels[offset + 3] = colour.A;
        }
    }

    public void Fill(PixelRect rectangle, Rgba32 colour, bool blend = true)
    {
        PixelRect clipped = PixelRect.Intersect(Bounds, rectangle);
        if (clipped.IsEmpty) return;
        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (int x = clipped.Left; x < clipped.Right; x++)
            {
                if (blend) BlendPixel(x, y, colour);
                else SetPixel(x, y, colour);
            }
        }
    }

    public void DrawCheckerboard(PixelRect rectangle, int cellSize, Rgba32 first, Rgba32 second)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        PixelRect clipped = PixelRect.Intersect(Bounds, rectangle);
        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (int x = clipped.Left; x < clipped.Right; x++)
            {
                bool alternate = (((x - rectangle.X) / cellSize) + ((y - rectangle.Y) / cellSize)) % 2 != 0;
                SetPixel(x, y, alternate ? second : first);
            }
        }
    }

    public void Blit(RgbaImage source, PixelRect sourceRectangle, PixelPoint destination, byte opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (opacity == 0 || sourceRectangle.IsEmpty) return;
        PixelRect sourceBounds = PixelRect.Intersect(source.Bounds, sourceRectangle);
        if (sourceBounds.IsEmpty) return;

        int destinationX = destination.X + (sourceBounds.X - sourceRectangle.X);
        int destinationY = destination.Y + (sourceBounds.Y - sourceRectangle.Y);
        PixelRect destinationRect = new(destinationX, destinationY, sourceBounds.Width, sourceBounds.Height);
        PixelRect clippedDestination = PixelRect.Intersect(Bounds, destinationRect);
        if (clippedDestination.IsEmpty) return;

        int sourceStartX = sourceBounds.X + (clippedDestination.X - destinationRect.X);
        int sourceStartY = sourceBounds.Y + (clippedDestination.Y - destinationRect.Y);
        for (int y = 0; y < clippedDestination.Height; y++)
        {
            int sy = sourceStartY + y;
            int dy = clippedDestination.Y + y;
            for (int x = 0; x < clippedDestination.Width; x++)
            {
                Rgba32 pixel = source.GetPixel(sourceStartX + x, sy);
                if (opacity != 255)
                {
                    pixel = pixel with { A = checked((byte)((pixel.A * opacity + 127) / 255)) };
                }
                BlendPixel(clippedDestination.X + x, dy, pixel);
            }
        }
    }

    public RgbaImage Clone(PixelRect region)
    {
        PixelRect clipped = PixelRect.Intersect(Bounds, region);
        if (clipped.IsEmpty) throw new ArgumentOutOfRangeException(nameof(region));
        RgbaImage clone = new(clipped.Width, clipped.Height);
        clone.Blit(this, clipped, PixelPoint.Zero);
        return clone;
    }

    public byte[] ToPng() => DeterministicPngEncoder.Encode(this);

    private void BlendPixel(int x, int y, Rgba32 source)
    {
        if (source.A == 0) return;
        int offset = GetOffset(x, y);
        if (source.A == 255)
        {
            _pixels[offset] = source.R;
            _pixels[offset + 1] = source.G;
            _pixels[offset + 2] = source.B;
            _pixels[offset + 3] = 255;
            return;
        }

        int destinationAlpha = _pixels[offset + 3];
        int sourceAlpha = source.A;
        int inverseSource = 255 - sourceAlpha;
        int outputAlpha = sourceAlpha + ((destinationAlpha * inverseSource + 127) / 255);
        if (outputAlpha <= 0)
        {
            _pixels[offset] = 0;
            _pixels[offset + 1] = 0;
            _pixels[offset + 2] = 0;
            _pixels[offset + 3] = 0;
            return;
        }

        int destinationWeight = (destinationAlpha * inverseSource + 127) / 255;
        _pixels[offset] = BlendChannel(source.R, sourceAlpha, _pixels[offset], destinationWeight, outputAlpha);
        _pixels[offset + 1] = BlendChannel(source.G, sourceAlpha, _pixels[offset + 1], destinationWeight, outputAlpha);
        _pixels[offset + 2] = BlendChannel(source.B, sourceAlpha, _pixels[offset + 2], destinationWeight, outputAlpha);
        _pixels[offset + 3] = checked((byte)outputAlpha);
    }

    private int GetOffset(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return checked((y * Width + x) * 4);
    }

    private static byte BlendChannel(int source, int sourceAlpha, int destination, int destinationWeight, int outputAlpha) =>
        checked((byte)((source * sourceAlpha + destination * destinationWeight + outputAlpha / 2) / outputAlpha));
}

public static class DeterministicPngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using MemoryStream output = new();
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), checked((uint)image.Height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ReadOnlySpan<byte> pixels = image.Pixels.Span;
            int stride = checked(image.Width * 4);
            byte[] row = GC.AllocateUninitializedArray<byte>(stride + 1);
            row[0] = 0;
            for (int y = 0; y < image.Height; y++)
            {
                pixels.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in first) crc = Table[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
            foreach (byte value in second) crc = Table[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint index = 0; index < (uint)table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
                table[(int)index] = value;
            }
            return table;
        }
    }
}

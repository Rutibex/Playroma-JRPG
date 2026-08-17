using System.Buffers.Binary;
using System.IO.Compression;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public static partial class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static PixelBuffer Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream);
    }

    public static PixelBuffer Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature)) throw new InvalidDataException("File is not a PNG image.");

        int width = 0, height = 0;
        byte bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, transparency = null;
        using MemoryStream compressed = new();
        while (true)
        {
            Span<byte> lengthBytes = stackalloc byte[4];
            stream.ReadExactly(lengthBytes);
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(lengthBytes));
            Span<byte> typeBytes = stackalloc byte[4];
            stream.ReadExactly(typeBytes);
            string type = System.Text.Encoding.ASCII.GetString(typeBytes);
            byte[] data = new byte[length];
            stream.ReadExactly(data);
            Span<byte> crc = stackalloc byte[4];
            stream.ReadExactly(crc);
            switch (type)
            {
                case "IHDR":
                    if (length != 13) throw new InvalidDataException("Invalid PNG IHDR length.");
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4)));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)));
                    bitDepth = data[8]; colorType = data[9];
                    if (data[10] != 0 || data[11] != 0) throw new NotSupportedException("Unsupported PNG compression or filter method.");
                    interlace = data[12];
                    break;
                case "PLTE": palette = data; break;
                case "tRNS": transparency = data; break;
                case "IDAT": compressed.Write(data); break;
                case "IEND": goto Decode;
            }
        }

    Decode:
        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG dimensions are invalid.");
        if (bitDepth != 8) throw new NotSupportedException("The current PNG codec supports 8-bit channels.");
        if (interlace != 0) throw new NotSupportedException("Interlaced PNG import is not supported by the current codec.");
        int bytesPerPixel = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG color type {colorType} is not supported.") };
        int stride = checked(width * bytesPerPixel);
        byte[] inflated = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        using (ZLibStream zlib = new(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            int read = 0;
            while (read < inflated.Length)
            {
                int count = zlib.Read(inflated, read, inflated.Length - read);
                if (count == 0) break;
                read += count;
            }
            if (read != inflated.Length) throw new InvalidDataException("PNG pixel stream ended unexpectedly.");
        }
        byte[] scanlines = Unfilter(inflated, width, height, bytesPerPixel);
        PixelBuffer result = new(width, height);
        int source = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Rgba32 color = colorType switch
            {
                0 => new Rgba32(scanlines[source], scanlines[source], scanlines[source], 255),
                2 => new Rgba32(scanlines[source], scanlines[source + 1], scanlines[source + 2], 255),
                3 => ReadPalette(scanlines[source], palette, transparency),
                4 => new Rgba32(scanlines[source], scanlines[source], scanlines[source], scanlines[source + 1]),
                6 => new Rgba32(scanlines[source], scanlines[source + 1], scanlines[source + 2], scanlines[source + 3]),
                _ => throw new InvalidDataException("Unsupported PNG color type.")
            };
            result.SetPixel(x, y, color);
            source += bytesPerPixel;
        }
        return result;
    }

    public static void Save(PixelBuffer buffer, string path)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) Save(buffer, stream);
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Save(PixelBuffer buffer, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(Signature);
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)buffer.Width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)buffer.Height));
        ihdr[8] = 8; ihdr[9] = 6;
        WriteChunk(stream, "IHDR", ihdr);
        byte[] raw = new byte[checked((buffer.Width * 4 + 1) * buffer.Height)];
        ReadOnlySpan<byte> rgba = buffer.Bytes.Span;
        int source = 0, destination = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            raw[destination++] = 0;
            rgba.Slice(source, buffer.Width * 4).CopyTo(raw.AsSpan(destination));
            source += buffer.Width * 4;
            destination += buffer.Width * 4;
        }
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] Unfilter(byte[] data, int width, int height, int bpp)
    {
        int stride = checked(width * bpp);
        byte[] output = new byte[checked(stride * height)];
        int source = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = data[source++];
            int row = y * stride;
            for (int x = 0; x < stride; x++)
            {
                byte raw = data[source++];
                byte left = x >= bpp ? output[row + x - bpp] : (byte)0;
                byte up = y > 0 ? output[row - stride + x] : (byte)0;
                byte upLeft = y > 0 && x >= bpp ? output[row - stride + x - bpp] : (byte)0;
                output[row + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + up)),
                    3 => unchecked((byte)(raw + ((left + up) >> 1))),
                    4 => unchecked((byte)(raw + Paeth(left, up, upLeft))),
                    _ => throw new InvalidDataException($"Unsupported PNG filter {filter}.")
                };
            }
        }
        return output;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static Rgba32 ReadPalette(byte index, byte[]? palette, byte[]? transparency)
    {
        if (palette is null || palette.Length % 3 != 0) throw new InvalidDataException("Indexed PNG is missing a valid palette.");
        int offset = index * 3;
        if (offset + 2 >= palette.Length) throw new InvalidDataException("Indexed PNG references a missing palette entry.");
        return new(palette[offset], palette[offset + 1], palette[offset + 2], transparency is not null && index < transparency.Length ? transparency[index] : (byte)255);
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        stream.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        byte[] crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput.AsSpan(typeBytes.Length));
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}

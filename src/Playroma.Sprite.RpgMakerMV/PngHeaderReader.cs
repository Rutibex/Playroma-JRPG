using System.Buffers.Binary;

namespace Playroma.Sprite.RpgMakerMV;

public sealed record PngImageInfo(int Width, int Height, byte BitDepth, byte ColorType);

public static class PngHeaderReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static PngImageInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    public static PngImageInfo Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[29];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(Signature)) throw new InvalidDataException("File is not a PNG image.");
        if (!header[12..16].SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG IHDR chunk is missing.");
        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[16..20]));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[20..24]));
        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG dimensions must be positive.");
        return new(width, height, header[24], header[25]);
    }
}

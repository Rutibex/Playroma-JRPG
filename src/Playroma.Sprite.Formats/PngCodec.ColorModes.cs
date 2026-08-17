using System.Buffers.Binary;
using System.IO.Compression;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public enum PngExportColorMode { Rgba, Grayscale, Indexed }

public sealed record PngExportOptions(
    PngExportColorMode ColorMode = PngExportColorMode.Rgba,
    IReadOnlyList<Rgba32>? Palette = null,
    PaletteDitherMode Dither = PaletteDitherMode.None,
    int TransparentIndex = -1,
    byte AlphaThreshold = 0,
    bool WriteSrgbChunk = true,
    double Gamma = 2.2);

public static partial class PngCodec
{
    public static void Save(PixelBuffer buffer, string path, PngExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer); ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(options);
        string fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!); string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try { using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None); Save(buffer, stream, options); File.Move(temporary, fullPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Save(PixelBuffer buffer, Stream stream, PngExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer); ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(options);
        stream.Write(Signature);
        byte colorType = options.ColorMode switch { PngExportColorMode.Grayscale => (byte)4, PngExportColorMode.Indexed => (byte)3, _ => (byte)6 };
        byte[] ihdr = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)buffer.Width)); BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)buffer.Height)); ihdr[8] = 8; ihdr[9] = colorType; WriteChunk(stream, "IHDR", ihdr);
        if (options.WriteSrgbChunk) WriteChunk(stream, "sRGB", [(byte)0]);
        if (options.Gamma > 0d)
        {
            byte[] gamma = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(gamma, checked((uint)Math.Round(100000d / options.Gamma))); WriteChunk(stream, "gAMA", gamma);
        }
        byte[] raw;
        if (options.ColorMode == PngExportColorMode.Indexed)
        {
            IReadOnlyList<Rgba32> palette = options.Palette ?? PaletteQuantizer.Quantize(buffer).Palette;
            int transparentIndex = options.TransparentIndex >= 0 && options.TransparentIndex < palette.Count && palette[options.TransparentIndex].A <= options.AlphaThreshold
                ? options.TransparentIndex : FindTransparentIndex(palette, options.AlphaThreshold);
            if (ContainsTransparentPixel(buffer, options.AlphaThreshold) && transparentIndex < 0)
            {
                if (palette.Count >= 256) throw new InvalidOperationException("Indexed PNG export requires a transparent palette entry for transparent pixels.");
                palette = [Rgba32.Transparent, .. palette]; transparentIndex = 0;
            }
            PaletteQuantizationResult mapped = PaletteQuantizer.Remap(buffer, palette, options.Dither, transparentIndex, options.AlphaThreshold);
            byte[] plte = new byte[checked(palette.Count * 3)], trns = new byte[palette.Count];
            for (int index = 0; index < palette.Count; index++) { plte[index * 3] = palette[index].R; plte[index * 3 + 1] = palette[index].G; plte[index * 3 + 2] = palette[index].B; trns[index] = palette[index].A; }
            WriteChunk(stream, "PLTE", plte); if (trns.Any(alpha => alpha != 255)) WriteChunk(stream, "tRNS", trns);
            raw = AddFilterBytes(mapped.Indices, buffer.Width, buffer.Height, 1);
        }
        else if (options.ColorMode == PngExportColorMode.Grayscale)
        {
            byte[] values = new byte[checked(buffer.Width * buffer.Height * 2)]; int offset = 0;
            for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++) { Rgba32 c = buffer.GetPixel(x, y); values[offset++] = ColorMath.Grayscale(c); values[offset++] = c.A; }
            raw = AddFilterBytes(values, buffer.Width, buffer.Height, 2);
        }
        else
        {
            raw = AddFilterBytes(buffer.Bytes.ToArray(), buffer.Width, buffer.Height, 4);
        }
        using MemoryStream compressed = new(); using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
        WriteChunk(stream, "IDAT", compressed.ToArray()); WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static int FindTransparentIndex(IReadOnlyList<Rgba32> palette, byte threshold)
    {
        for (int index = 0; index < palette.Count; index++) if (palette[index].A <= threshold) return index;
        return -1;
    }

    private static bool ContainsTransparentPixel(PixelBuffer buffer, byte threshold)
    {
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++) if (buffer.GetPixel(x, y).A <= threshold) return true;
        return false;
    }

    private static byte[] AddFilterBytes(ReadOnlySpan<byte> pixels, int width, int height, int bytesPerPixel)
    {
        int stride = checked(width * bytesPerPixel); byte[] raw = new byte[checked((stride + 1) * height)]; int source = 0, destination = 0;
        for (int y = 0; y < height; y++) { raw[destination++] = 0; pixels.Slice(source, stride).CopyTo(raw.AsSpan(destination)); source += stride; destination += stride; }
        return raw;
    }
}

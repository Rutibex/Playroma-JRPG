using System.Buffers.Binary;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed record PngColorMetadata(PngExportColorMode ColorMode, IReadOnlyList<Rgba32> Palette, double? Gamma, bool IsSrgb)
{
    public SpritePalette? CreatePalette(string name) => Palette.Count == 0 ? null : PaletteDocumentOperations.FromColors(name, Palette);
}

public static class PngColorMetadataReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static PngColorMetadata Read(string path)
    {
        using FileStream stream = File.OpenRead(path); return Read(stream);
    }

    public static PngColorMetadata Read(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8]; stream.ReadExactly(signature); if (!signature.SequenceEqual(Signature)) throw new InvalidDataException("File is not a PNG image.");
        byte colorType = 6; byte[]? plte = null, trns = null; double? gamma = null; bool srgb = false;
        while (true)
        {
            Span<byte> lengthBytes = stackalloc byte[4]; stream.ReadExactly(lengthBytes); int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(lengthBytes));
            Span<byte> typeBytes = stackalloc byte[4]; stream.ReadExactly(typeBytes); string type = System.Text.Encoding.ASCII.GetString(typeBytes); byte[] data = new byte[length]; stream.ReadExactly(data); Span<byte> crc = stackalloc byte[4]; stream.ReadExactly(crc);
            if (type == "IHDR") colorType = data[9];
            else if (type == "PLTE") plte = data;
            else if (type == "tRNS") trns = data;
            else if (type == "gAMA" && data.Length == 4)
            {
                uint encodedGamma = BinaryPrimitives.ReadUInt32BigEndian(data);
                gamma = encodedGamma == 0 ? null : 100000d / encodedGamma;
            }
            else if (type == "sRGB") srgb = true;
            else if (type == "IEND") break;
        }
        List<Rgba32> palette = [];
        if (plte is not null)
        {
            for (int offset = 0, index = 0; offset + 2 < plte.Length; offset += 3, index++) palette.Add(new(plte[offset], plte[offset + 1], plte[offset + 2], trns is not null && index < trns.Length ? trns[index] : (byte)255));
        }
        PngExportColorMode mode = colorType == 3 ? PngExportColorMode.Indexed : colorType is 0 or 4 ? PngExportColorMode.Grayscale : PngExportColorMode.Rgba;
        return new(mode, palette, gamma, srgb);
    }
}

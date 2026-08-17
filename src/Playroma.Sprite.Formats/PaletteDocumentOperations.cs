using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public static class PaletteDocumentOperations
{
    public static SpritePalette GeneratePalette(SpritePackageData package, string name, int maximumColors, PaletteDitherMode previewDither = PaletteDitherMode.None)
    {
        ArgumentNullException.ThrowIfNull(package);
        PixelBuffer samples = BuildSampleBuffer(package.PixelBuffers.Values, 262_144);
        PaletteQuantizationResult quantized = PaletteQuantizer.Quantize(samples, new PaletteQuantizationOptions(maximumColors, Dither: previewDither));
        SpritePalette palette = FromColors(name, quantized.Palette);
        package.Document.Palettes.Add(palette); package.Document.ActivePaletteId = palette.Id;
        package.Document.TransparentPaletteIndex = Math.Max(0, quantized.TransparentIndex);
        return palette;
    }

    public static SpritePalette FromColors(string name, IReadOnlyList<Rgba32> colors)
    {
        if (colors.Count is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(colors));
        SpritePalette palette = new() { Name = string.IsNullOrWhiteSpace(name) ? "Palette" : name.Trim() };
        for (int index = 0; index < colors.Count; index++) palette.Entries.Add(new SpritePaletteEntry { Index = index, Color = new(colors[index].R, colors[index].G, colors[index].B, colors[index].A) });
        return palette;
    }

    public static IReadOnlyList<Rgba32> ToColors(SpritePalette palette) => palette.Entries.OrderBy(entry => entry.Index).Select(entry => new Rgba32(entry.Color.R, entry.Color.G, entry.Color.B, entry.Color.A)).ToArray();

    public static int ApplyPalette(SpritePackageData package, SpritePalette palette, PaletteDitherMode dither, bool allPixelContents = true)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(palette);
        KeyValuePair<Guid, PixelBuffer>[] targets = (allPixelContents ? package.PixelBuffers : package.PixelBuffers.Take(1)).ToArray();
        bool requiresTransparency = targets.Any(pair => ContainsTransparentPixel(pair.Value));
        int transparent = palette.Entries.FindIndex(entry => entry.Color.A == 0);
        if (requiresTransparency && transparent < 0)
        {
            if (palette.Entries.Count >= 256) throw new InvalidOperationException("The selected 256-color palette has no transparent entry. Remove a color or add transparency before applying it to transparent artwork.");
            foreach (SpritePaletteEntry entry in palette.Entries) entry.Index++;
            palette.Entries.Insert(0, new SpritePaletteEntry { Index = 0, Name = "Transparent", Color = new SpriteColorValue(0, 0, 0, 0) }); transparent = 0;
        }
        IReadOnlyList<Rgba32> colors = ToColors(palette); int changed = 0;
        foreach ((Guid id, PixelBuffer buffer) in targets)
        {
            PaletteQuantizationResult mapped = PaletteQuantizer.Remap(buffer, colors, dither, transparent);
            package.PixelBuffers[id] = mapped.Remapped; changed++;
        }
        package.Document.ColorMode = SpriteColorMode.Indexed; package.Document.ActivePaletteId = palette.Id; package.Document.TransparentPaletteIndex = Math.Max(0, transparent);
        return changed;
    }

    public static int ConvertToGrayscale(SpritePackageData package, GrayscaleMethod method = GrayscaleMethod.Luminance)
    {
        ArgumentNullException.ThrowIfNull(package); int changed = 0;
        foreach (PixelBuffer buffer in package.PixelBuffers.Values) { AdvancedPixelEffects.ConvertToGrayscale(buffer, method); changed++; }
        package.Document.ColorMode = SpriteColorMode.Grayscale; package.Document.ActivePaletteId = null; return changed;
    }

    public static void ConvertToRgba(SpritePackageData package)
    {
        ArgumentNullException.ThrowIfNull(package); package.Document.ColorMode = SpriteColorMode.Rgba;
    }

    private static bool ContainsTransparentPixel(PixelBuffer buffer)
    {
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++) if (buffer.GetPixel(x, y).A == 0) return true;
        return false;
    }

    private static PixelBuffer BuildSampleBuffer(IEnumerable<PixelBuffer> buffers, int maximumSamples)
    {
        PixelBuffer[] source = buffers.ToArray(); if (source.Length == 0) throw new InvalidDataException("No pixel content is loaded.");
        long totalPixels = source.Sum(buffer => (long)buffer.Width * buffer.Height); int count = (int)Math.Min(maximumSamples, totalPixels); PixelBuffer samples = new(Math.Max(1, count), 1);
        double step = totalPixels / (double)Math.Max(1, count); double cursor = 0d; int destination = 0; long start = 0;
        foreach (PixelBuffer buffer in source)
        {
            long end = start + (long)buffer.Width * buffer.Height;
            while (destination < count && cursor < end)
            {
                long local = Math.Max(0, (long)cursor - start); int x = (int)(local % buffer.Width), y = (int)(local / buffer.Width);
                samples.SetPixel(destination++, 0, buffer.GetPixel(x, y)); cursor += step;
            }
            start = end;
        }
        while (destination < samples.Width) samples.SetPixel(destination++, 0, Rgba32.Transparent);
        return samples;
    }
}

using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed record BuiltInColorProfile(string Id, string DisplayName, string Kind, double Gamma);

public static class ColorProfileOperations
{
    public static IReadOnlyList<BuiltInColorProfile> BuiltIns { get; } =
    [
        new("srgb", "sRGB", "srgb", 2.2),
        new("linear-srgb", "Linear sRGB", "linear-srgb", 1.0),
        new("gamma18", "Gamma 1.8 RGB", "gamma", 1.8),
        new("gamma24", "Gamma 2.4 RGB", "gamma", 2.4),
        new("unspecified", "Unspecified RGB", "unspecified", 2.2)
    ];

    public static BuiltInColorProfile GetRequired(string id) => BuiltIns.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Color profile '{id}' is not available.");

    public static void Assign(SpriteDocument document, BuiltInColorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(profile);
        document.ColorProfile = profile.DisplayName;
        document.ColorProfileMetadata.Name = profile.DisplayName;
        document.ColorProfileMetadata.Kind = profile.Kind;
        document.ColorProfileMetadata.Gamma = profile.Gamma;
        document.ColorProfileMetadata.EmbeddedProfileEntryName = null;
    }

    public static int Convert(SpritePackageData package, BuiltInColorProfile destination)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(destination);
        double sourceGamma = Math.Max(.01d, package.Document.ColorProfileMetadata.Gamma);
        double targetGamma = Math.Max(.01d, destination.Gamma);
        if (Math.Abs(sourceGamma - targetGamma) < .000001d)
        {
            Assign(package.Document, destination);
            return 0;
        }
        HashSet<Guid> processed = [];
        foreach ((Guid id, PixelBuffer buffer) in package.PixelBuffers)
        {
            if (!processed.Add(id)) continue;
            for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
            {
                Rgba32 color = buffer.GetPixel(x, y);
                if (color.A == 0) continue;
                buffer.SetPixel(x, y, new(ConvertChannel(color.R), ConvertChannel(color.G), ConvertChannel(color.B), color.A));
            }
        }
        foreach (SpritePalette palette in package.Document.Palettes)
        {
            foreach (SpritePaletteEntry entry in palette.Entries)
            {
                SpriteColorValue color = entry.Color;
                if (color.A == 0) continue;
                entry.Color = new SpriteColorValue(ConvertChannel(color.R), ConvertChannel(color.G), ConvertChannel(color.B), color.A);
            }
        }
        Assign(package.Document, destination); return processed.Count;

        byte ConvertChannel(byte channel)
        {
            double encoded = channel / 255d;
            double linear = Math.Pow(encoded, sourceGamma);
            return ColorMath.ToByte(Math.Pow(linear, 1d / targetGamma));
        }
    }
}

public enum PaletteSortMode { Index, Hue, Saturation, Lightness, Luminance, Red, Green, Blue, Alpha, Name }

public static class PaletteSorting
{
    public static void Sort(SpritePalette palette, PaletteSortMode mode, bool descending = false)
    {
        ArgumentNullException.ThrowIfNull(palette);
        IEnumerable<SpritePaletteEntry> ordered = mode switch
        {
            PaletteSortMode.Hue => palette.Entries.OrderBy(entry => Hsl(entry).H),
            PaletteSortMode.Saturation => palette.Entries.OrderBy(entry => Hsl(entry).S),
            PaletteSortMode.Lightness => palette.Entries.OrderBy(entry => Hsl(entry).L),
            PaletteSortMode.Luminance => palette.Entries.OrderBy(entry => Luminance(entry.Color)),
            PaletteSortMode.Red => palette.Entries.OrderBy(entry => entry.Color.R),
            PaletteSortMode.Green => palette.Entries.OrderBy(entry => entry.Color.G),
            PaletteSortMode.Blue => palette.Entries.OrderBy(entry => entry.Color.B),
            PaletteSortMode.Alpha => palette.Entries.OrderBy(entry => entry.Color.A),
            PaletteSortMode.Name => palette.Entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            _ => palette.Entries.OrderBy(entry => entry.Index)
        };
        SpritePaletteEntry[] result = (descending ? ordered.Reverse() : ordered).ToArray();
        palette.Entries.Clear(); palette.Entries.AddRange(result);
        for (int index = 0; index < palette.Entries.Count; index++) palette.Entries[index].Index = index;
    }

    private static (double H, double S, double L) Hsl(SpritePaletteEntry entry) => ColorMath.ToHsl(new(entry.Color.R, entry.Color.G, entry.Color.B, entry.Color.A));
    private static double Luminance(SpriteColorValue color) => color.R * .2126 + color.G * .7152 + color.B * .0722;
}

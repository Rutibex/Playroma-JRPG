namespace Playroma.Sprite.Pixel;

public enum GrayscaleMethod { Luminance, Average, Lightness, Red, Green, Blue }
public enum PaletteDitherMode { None, OrderedBayer4x4, FloydSteinberg }

public static class ColorMath
{
    public static (double H, double S, double L) ToHsl(Rgba32 color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0d, s = 0d, l = (max + min) / 2d;
        double delta = max - min;
        if (delta > 0d)
        {
            s = delta / (1d - Math.Abs(2d * l - 1d));
            if (max == r) h = 60d * (((g - b) / delta) % 6d);
            else if (max == g) h = 60d * (((b - r) / delta) + 2d);
            else h = 60d * (((r - g) / delta) + 4d);
            if (h < 0d) h += 360d;
        }
        return (h, s, l);
    }

    public static Rgba32 FromHsl(double hue, double saturation, double lightness, byte alpha = 255)
    {
        hue = ((hue % 360d) + 360d) % 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        lightness = Math.Clamp(lightness, 0d, 1d);
        double chroma = (1d - Math.Abs(2d * lightness - 1d)) * saturation;
        double h = hue / 60d;
        double x = chroma * (1d - Math.Abs(h % 2d - 1d));
        (double r, double g, double b) = h switch
        {
            < 1d => (chroma, x, 0d),
            < 2d => (x, chroma, 0d),
            < 3d => (0d, chroma, x),
            < 4d => (0d, x, chroma),
            < 5d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double m = lightness - chroma / 2d;
        return new(ToByte(r + m), ToByte(g + m), ToByte(b + m), alpha);
    }

    public static byte Grayscale(Rgba32 color, GrayscaleMethod method = GrayscaleMethod.Luminance) => method switch
    {
        GrayscaleMethod.Average => (byte)((color.R + color.G + color.B) / 3),
        GrayscaleMethod.Lightness => (byte)((Math.Max(color.R, Math.Max(color.G, color.B)) + Math.Min(color.R, Math.Min(color.G, color.B))) / 2),
        GrayscaleMethod.Red => color.R,
        GrayscaleMethod.Green => color.G,
        GrayscaleMethod.Blue => color.B,
        _ => ToByte((color.R * .2126 + color.G * .7152 + color.B * .0722) / 255d)
    };

    public static double Linearize(byte channel)
    {
        double value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }

    public static double PerceptualDistanceSquared(Rgba32 left, Rgba32 right)
    {
        if (left.A == 0 && right.A == 0) return 0d;
        double rMean = (left.R + right.R) / 2d;
        double dr = left.R - right.R, dg = left.G - right.G, db = left.B - right.B, da = left.A - right.A;
        return (2d + rMean / 256d) * dr * dr + 4d * dg * dg + (2.767d - rMean / 256d) * db * db + 2d * da * da;
    }

    public static byte ToByte(double normalized) => (byte)Math.Clamp((int)Math.Round(normalized * 255d), 0, 255);
}

namespace Playroma.JRPG.Rendering;

public sealed record ImageComparisonResult(
    bool DimensionsMatch,
    int ChangedPixels,
    int MaximumChannelDifference,
    double ChangedFraction,
    RgbaImage DifferenceImage)
{
    public bool IsMatch => DimensionsMatch && ChangedPixels == 0;
}

/// <summary>Pixel-exact and tolerance-controlled visual regression comparison.</summary>
public static class RgbaImageComparer
{
    public static ImageComparisonResult Compare(RgbaImage expected, RgbaImage actual, byte channelTolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        int width = Math.Max(expected.Width, actual.Width);
        int height = Math.Max(expected.Height, actual.Height);
        RgbaImage difference = new(width, height);
        difference.Clear(new Rgba32(16, 16, 16, 255));
        bool dimensionsMatch = expected.Width == actual.Width && expected.Height == actual.Height;
        int changed = 0;
        int maximumDifference = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool expectedPresent = x < expected.Width && y < expected.Height;
                bool actualPresent = x < actual.Width && y < actual.Height;
                if (!expectedPresent || !actualPresent)
                {
                    difference.SetPixel(x, y, new Rgba32(255, 0, 255, 255));
                    changed++;
                    maximumDifference = 255;
                    continue;
                }

                Rgba32 left = expected.GetPixel(x, y);
                Rgba32 right = actual.GetPixel(x, y);
                int red = Math.Abs(left.R - right.R);
                int green = Math.Abs(left.G - right.G);
                int blue = Math.Abs(left.B - right.B);
                int alpha = Math.Abs(left.A - right.A);
                int localMaximum = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                maximumDifference = Math.Max(maximumDifference, localMaximum);
                if (localMaximum <= channelTolerance)
                {
                    byte luminance = checked((byte)((left.R * 30 + left.G * 59 + left.B * 11) / 100));
                    byte muted = checked((byte)(24 + luminance * 3 / 8));
                    difference.SetPixel(x, y, new Rgba32(muted, muted, muted, 255));
                    continue;
                }

                changed++;
                int magnitude = Math.Min(255, 96 + localMaximum);
                difference.SetPixel(x, y, new Rgba32(checked((byte)magnitude), 0, checked((byte)Math.Min(255, blue + alpha)), 255));
            }
        }

        int totalPixels = checked(width * height);
        return new ImageComparisonResult(
            dimensionsMatch,
            changed,
            maximumDifference,
            totalPixels == 0 ? 0 : changed / (double)totalPixels,
            difference);
    }
}

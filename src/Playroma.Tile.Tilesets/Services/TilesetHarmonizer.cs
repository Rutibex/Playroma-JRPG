using System.Drawing;
using System.Drawing.Imaging;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class TilesetHarmonizer
{
    public TilesetStyleReport Analyze(IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets)
    {
        var samples = new List<(double Luminance, double Saturation, double Contrast)>();
        var fringes = 0;
        var details = new List<double>();
        foreach (var bitmap in sheets.Values)
        {
            var stats = AnalyzeBitmap(bitmap);
            samples.Add((stats.Luminance, stats.Saturation, stats.Contrast));
            fringes += stats.FringeCount;
            details.Add(stats.Detail);
        }
        if (samples.Count == 0) return new TilesetStyleReport();
        var averageDetail = details.Average();
        var detailDeviation = StandardDeviation(details);
        var report = new TilesetStyleReport
        {
            AverageLuminance = samples.Average(x => x.Luminance),
            AverageSaturation = samples.Average(x => x.Saturation),
            AverageContrast = samples.Average(x => x.Contrast),
            TransparentFringeCount = fringes,
            DetailOutlierCount = details.Count(x => Math.Abs(x - averageDetail) > Math.Max(0.08, detailDeviation * 1.75)),
            PaletteConsistency = Math.Round(Math.Clamp(100d -
                StandardDeviation(samples.Select(x => x.Luminance)) * 140d -
                StandardDeviation(samples.Select(x => x.Saturation)) * 110d, 0d, 100d), 1)
        };
        if (report.TransparentFringeCount > 0) report.Warnings.Add($"{report.TransparentFringeCount} sampled semi-transparent fringe pixels were found.");
        if (report.DetailOutlierCount > 0) report.Warnings.Add($"{report.DetailOutlierCount} sheet(s) have unusually different detail density.");
        if (report.PaletteConsistency < 70) report.Warnings.Add("The tileset palette varies substantially between sheets.");
        return report;
    }

    public void Normalize(Bitmap bitmap, float targetSaturation, float targetLuminance, float contrast,
        bool removeFringes, bool quantizeAlpha)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var p = row + x * 4;
                        var alpha = p[3];
                        if (alpha == 0) continue;
                        var color = Color.FromArgb(alpha, p[2], p[1], p[0]);
                        var hue = color.GetHue();
                        var saturation = Math.Clamp(color.GetSaturation() * 0.55f + targetSaturation * 0.45f, 0f, 1f);
                        var value = Math.Clamp((color.GetBrightness() - 0.5f) * contrast + 0.5f, 0f, 1f);
                        value = Math.Clamp(value * 0.60f + targetLuminance * 0.40f, 0f, 1f);
                        var adjusted = FromHsv(hue, saturation, value);
                        p[0] = adjusted.B;
                        p[1] = adjusted.G;
                        p[2] = adjusted.R;
                        if (removeFringes && alpha < 16) p[3] = 0;
                        else if (quantizeAlpha) p[3] = alpha < 128 ? (byte)0 : (byte)255;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static (double Luminance, double Saturation, double Contrast, int FringeCount, double Detail) AnalyzeBitmap(Bitmap bitmap)
    {
        var luminances = new List<double>();
        var saturations = new List<double>();
        var fringe = 0;
        double edge = 0;
        var edgeCount = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        for (var x = 0; x < bitmap.Width; x += 4)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.A == 0) continue;
            if (color.A is > 0 and < 250) fringe++;
            var lum = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
            luminances.Add(lum);
            saturations.Add(color.GetSaturation());
            if (x + 4 < bitmap.Width)
            {
                var neighbor = bitmap.GetPixel(x + 4, y);
                edge += Math.Abs(lum - (0.2126 * neighbor.R + 0.7152 * neighbor.G + 0.0722 * neighbor.B) / 255d);
                edgeCount++;
            }
        }
        if (luminances.Count == 0) return (0, 0, 0, fringe, 0);
        return (luminances.Average(), saturations.Average(), StandardDeviation(luminances), fringe,
            edgeCount == 0 ? 0 : edge / edgeCount);
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60f % 2) - 1));
        var m = value - c;
        (float r, float g, float b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    private static double StandardDeviation(IEnumerable<double> values)
    {
        var array = values.ToArray();
        if (array.Length < 2) return 0;
        var mean = array.Average();
        return Math.Sqrt(array.Sum(x => (x - mean) * (x - mean)) / array.Length);
    }
}

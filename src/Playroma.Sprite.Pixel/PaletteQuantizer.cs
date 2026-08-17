namespace Playroma.Sprite.Pixel;

public sealed record PaletteQuantizationOptions(
    int MaximumColors = 256,
    bool ReserveTransparentEntry = true,
    byte AlphaThreshold = 0,
    PaletteDitherMode Dither = PaletteDitherMode.None,
    bool Serpentine = true);

public sealed class PaletteQuantizationResult
{
    public required IReadOnlyList<Rgba32> Palette { get; init; }
    public required byte[] Indices { get; init; }
    public required PixelBuffer Remapped { get; init; }
    public int TransparentIndex { get; init; } = -1;
}

public static class PaletteQuantizer
{
    public static PaletteQuantizationResult Quantize(PixelBuffer source, PaletteQuantizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new();
        int maximum = Math.Clamp(options.MaximumColors, 2, 256);
        bool hasTransparent = options.ReserveTransparentEntry && Enumerate(source).Any(color => color.A <= options.AlphaThreshold);
        int opaqueBudget = maximum - (hasTransparent ? 1 : 0);
        List<WeightedColor> colors = BuildHistogram(source, options.AlphaThreshold);
        List<Rgba32> palette = colors.Count == 0 ? [] : MedianCut(colors, Math.Max(1, opaqueBudget));
        int transparentIndex = -1;
        if (hasTransparent)
        {
            transparentIndex = 0;
            palette.Insert(0, Rgba32.Transparent);
        }
        if (palette.Count == 0) palette.Add(Rgba32.Black);
        return Remap(source, palette, options.Dither, transparentIndex, options.AlphaThreshold, options.Serpentine);
    }

    public static PaletteQuantizationResult Remap(PixelBuffer source, IReadOnlyList<Rgba32> palette, PaletteDitherMode dither = PaletteDitherMode.None, int transparentIndex = -1, byte alphaThreshold = 0, bool serpentine = true)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(palette), "Indexed palettes must contain 1 to 256 colors.");
        PixelBuffer remapped = new(source.Width, source.Height);
        byte[] indices = new byte[checked(source.Width * source.Height)];
        if (dither == PaletteDitherMode.FloydSteinberg)
            RemapFloydSteinberg(source, palette, remapped, indices, transparentIndex, alphaThreshold, serpentine);
        else
            RemapDirect(source, palette, remapped, indices, transparentIndex, alphaThreshold, dither);
        return new PaletteQuantizationResult { Palette = palette.ToArray(), Indices = indices, Remapped = remapped, TransparentIndex = transparentIndex };
    }

    public static int FindNearest(Rgba32 color, IReadOnlyList<Rgba32> palette, int transparentIndex = -1, byte alphaThreshold = 0)
    {
        if (transparentIndex >= 0 && color.A <= alphaThreshold) return transparentIndex;
        int best = 0; double bestDistance = double.MaxValue;
        for (int index = 0; index < palette.Count; index++)
        {
            if (index == transparentIndex && color.A > alphaThreshold) continue;
            double distance = ColorMath.PerceptualDistanceSquared(color, palette[index]);
            if (distance < bestDistance) { bestDistance = distance; best = index; }
        }
        return best;
    }

    private static void RemapDirect(PixelBuffer source, IReadOnlyList<Rgba32> palette, PixelBuffer destination, byte[] indices, int transparentIndex, byte alphaThreshold, PaletteDitherMode dither)
    {
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
        {
            Rgba32 color = source.GetPixel(x, y);
            if (dither == PaletteDitherMode.OrderedBayer4x4 && color.A > alphaThreshold)
            {
                double bias = (PixelDrawing.DitherThreshold(x, y, DitherMatrixKind.Bayer4x4) - .5d) * 24d;
                color = new Rgba32(Clamp(color.R + bias), Clamp(color.G + bias), Clamp(color.B + bias), color.A);
            }
            int index = FindNearest(color, palette, transparentIndex, alphaThreshold);
            indices[y * source.Width + x] = (byte)index;
            destination.SetPixel(x, y, palette[index]);
        }
    }

    private static void RemapFloydSteinberg(PixelBuffer source, IReadOnlyList<Rgba32> palette, PixelBuffer destination, byte[] indices, int transparentIndex, byte alphaThreshold, bool serpentine)
    {
        int length = checked(source.Width * source.Height);
        double[] r = new double[length], g = new double[length], b = new double[length], a = new double[length];
        for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
        {
            int i = y * source.Width + x; Rgba32 c = source.GetPixel(x, y);
            r[i] = c.R; g[i] = c.G; b[i] = c.B; a[i] = c.A;
        }
        for (int y = 0; y < source.Height; y++)
        {
            bool reverse = serpentine && (y & 1) == 1;
            for (int step = 0; step < source.Width; step++)
            {
                int x = reverse ? source.Width - 1 - step : step;
                int i = y * source.Width + x;
                Rgba32 current = new(Clamp(r[i]), Clamp(g[i]), Clamp(b[i]), Clamp(a[i]));
                int paletteIndex = FindNearest(current, palette, transparentIndex, alphaThreshold);
                Rgba32 chosen = palette[paletteIndex]; indices[i] = (byte)paletteIndex; destination.SetPixel(x, y, chosen);
                double er = current.R - chosen.R, eg = current.G - chosen.G, eb = current.B - chosen.B, ea = current.A - chosen.A;
                int direction = reverse ? -1 : 1;
                Diffuse(x + direction, y, 7d / 16d);
                Diffuse(x - direction, y + 1, 3d / 16d);
                Diffuse(x, y + 1, 5d / 16d);
                Diffuse(x + direction, y + 1, 1d / 16d);
                void Diffuse(int dx, int dy, double weight)
                {
                    if (dx < 0 || dx >= source.Width || dy < 0 || dy >= source.Height) return;
                    int di = dy * source.Width + dx;
                    r[di] += er * weight; g[di] += eg * weight; b[di] += eb * weight; a[di] += ea * weight;
                }
            }
        }
    }

    private static List<WeightedColor> BuildHistogram(PixelBuffer source, byte alphaThreshold)
    {
        Dictionary<Rgba32, int> histogram = [];
        foreach (Rgba32 color in Enumerate(source))
        {
            if (color.A <= alphaThreshold) continue;
            histogram[color] = histogram.TryGetValue(color, out int count) ? count + 1 : 1;
        }
        return histogram.Select(pair => new WeightedColor(pair.Key, pair.Value)).ToList();
    }

    private static List<Rgba32> MedianCut(List<WeightedColor> source, int count)
    {
        List<ColorBox> boxes = [new ColorBox(source)];
        while (boxes.Count < count)
        {
            ColorBox? selected = boxes.Where(box => box.Colors.Count > 1).OrderByDescending(box => box.Score).FirstOrDefault();
            if (selected is null) break;
            boxes.Remove(selected);
            (ColorBox left, ColorBox right) = selected.Split(); boxes.Add(left); boxes.Add(right);
        }
        return boxes.Select(box => box.Average()).ToList();
    }

    private static IEnumerable<Rgba32> Enumerate(PixelBuffer buffer)
    {
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++) yield return buffer.GetPixel(x, y);
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private readonly record struct WeightedColor(Rgba32 Color, int Weight);

    private sealed class ColorBox
    {
        public ColorBox(List<WeightedColor> colors) { Colors = colors; Recalculate(); }
        public List<WeightedColor> Colors { get; }
        public double Score { get; private set; }
        private int channel;

        private void Recalculate()
        {
            int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0, minA = 255, maxA = 0, total = 0;
            foreach (WeightedColor item in Colors)
            {
                Rgba32 c = item.Color; total += item.Weight;
                minR = Math.Min(minR, c.R); maxR = Math.Max(maxR, c.R); minG = Math.Min(minG, c.G); maxG = Math.Max(maxG, c.G);
                minB = Math.Min(minB, c.B); maxB = Math.Max(maxB, c.B); minA = Math.Min(minA, c.A); maxA = Math.Max(maxA, c.A);
            }
            int[] ranges = [maxR - minR, maxG - minG, maxB - minB, maxA - minA];
            channel = Array.IndexOf(ranges, ranges.Max()); Score = ranges[channel] * Math.Log2(total + 1d);
        }

        public (ColorBox Left, ColorBox Right) Split()
        {
            List<WeightedColor> ordered = Colors.OrderBy(item => channel switch { 0 => item.Color.R, 1 => item.Color.G, 2 => item.Color.B, _ => item.Color.A }).ToList();
            int total = ordered.Sum(item => item.Weight), running = 0, cut = 1;
            for (int i = 0; i < ordered.Count - 1; i++) { running += ordered[i].Weight; if (running * 2 >= total) { cut = i + 1; break; } }
            return (new ColorBox(ordered.Take(cut).ToList()), new ColorBox(ordered.Skip(cut).ToList()));
        }

        public Rgba32 Average()
        {
            long r = 0, g = 0, b = 0, a = 0, total = 0;
            foreach (WeightedColor item in Colors) { long w = item.Weight; total += w; r += item.Color.R * w; g += item.Color.G * w; b += item.Color.B * w; a += item.Color.A * w; }
            return new Rgba32((byte)(r / total), (byte)(g / total), (byte)(b / total), (byte)(a / total));
        }
    }
}

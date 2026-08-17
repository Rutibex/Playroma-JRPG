namespace Playroma.Sprite.Pixel;

public readonly record struct ColorCurvePoint(byte Input, byte Output);

public static class AdvancedPixelEffects
{
    public static void ConvertToGrayscale(PixelBuffer buffer, GrayscaleMethod method = GrayscaleMethod.Luminance, SelectionMask? selection = null)
    {
        Apply(buffer, selection, color => { byte gray = ColorMath.Grayscale(color, method); return new Rgba32(gray, gray, gray, color.A); });
    }

    public static void HueSaturationLightness(PixelBuffer buffer, double hueDegrees, double saturationDelta, double lightnessDelta, SelectionMask? selection = null)
    {
        Apply(buffer, selection, color =>
        {
            if (color.A == 0) return color;
            (double h, double s, double l) = ColorMath.ToHsl(color);
            return ColorMath.FromHsl(h + hueDegrees, s + saturationDelta, l + lightnessDelta, color.A);
        });
    }

    public static void BrightnessContrast(PixelBuffer buffer, double brightness, double contrast, SelectionMask? selection = null)
    {
        brightness = Math.Clamp(brightness, -1d, 1d); contrast = Math.Clamp(contrast, -1d, 1d);
        double factor = contrast >= 0d ? 1d / Math.Max(.001d, 1d - contrast) : 1d + contrast;
        Apply(buffer, selection, color => new Rgba32(Adjust(color.R), Adjust(color.G), Adjust(color.B), color.A));
        byte Adjust(byte value) => Clamp((((value / 255d - .5d) * factor + .5d) + brightness) * 255d);
    }

    public static void Invert(PixelBuffer buffer, bool includeAlpha = false, SelectionMask? selection = null) =>
        Apply(buffer, selection, color => new Rgba32((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), includeAlpha ? (byte)(255 - color.A) : color.A));

    public static void Curves(PixelBuffer buffer, IReadOnlyList<ColorCurvePoint> points, SelectionMask? selection = null)
    {
        byte[] map = BuildCurve(points);
        Apply(buffer, selection, color => new Rgba32(map[color.R], map[color.G], map[color.B], color.A));
    }

    public static void Convolve(PixelBuffer buffer, double[,] kernel, double divisor = 0d, double bias = 0d, bool affectAlpha = false, SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(buffer); ArgumentNullException.ThrowIfNull(kernel);
        int kh = kernel.GetLength(0), kw = kernel.GetLength(1);
        if (kh == 0 || kw == 0 || (kh & 1) == 0 || (kw & 1) == 0) throw new ArgumentException("Convolution kernels must have odd non-zero dimensions.", nameof(kernel));
        if (divisor == 0d) { divisor = 0d; foreach (double item in kernel) divisor += item; if (Math.Abs(divisor) < .000001d) divisor = 1d; }
        PixelBuffer source = buffer.Clone(); int ox = kw / 2, oy = kh / 2;
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            byte coverage = selection is null || selection.IsEmpty ? (byte)255 : selection.Get(x, y);
            if (coverage == 0) continue;
            double r = 0, g = 0, b = 0, a = 0;
            for (int ky = 0; ky < kh; ky++) for (int kx = 0; kx < kw; kx++)
            {
                int sx = Math.Clamp(x + kx - ox, 0, source.Width - 1), sy = Math.Clamp(y + ky - oy, 0, source.Height - 1);
                Rgba32 c = source.GetPixel(sx, sy); double weight = kernel[ky, kx];
                r += c.R * weight; g += c.G * weight; b += c.B * weight; a += c.A * weight;
            }
            Rgba32 original = source.GetPixel(x, y);
            Rgba32 filtered = new(Clamp(r / divisor + bias), Clamp(g / divisor + bias), Clamp(b / divisor + bias), affectAlpha ? Clamp(a / divisor + bias) : original.A);
            buffer.SetPixel(x, y, coverage == 255 ? filtered : Rgba32.Lerp(original, filtered, coverage / 255d));
        }
    }

    public static void GaussianBlur(PixelBuffer buffer, int radius = 1, SelectionMask? selection = null)
    {
        radius = Math.Clamp(radius, 1, 12); int size = radius * 2 + 1; double sigma = Math.Max(.5d, radius / 2d); double[,] kernel = new double[size, size];
        double total = 0d;
        for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++) { double value = Math.Exp(-(x * x + y * y) / (2d * sigma * sigma)); kernel[y + radius, x + radius] = value; total += value; }
        Convolve(buffer, kernel, total, selection: selection);
    }

    public static void Sharpen(PixelBuffer buffer, double amount = 1d, SelectionMask? selection = null)
    {
        amount = Math.Clamp(amount, 0d, 5d);
        double[,] kernel = { { 0d, -amount, 0d }, { -amount, 1d + 4d * amount, -amount }, { 0d, -amount, 0d } };
        Convolve(buffer, kernel, 1d, selection: selection);
    }

    public static void Pixelate(PixelBuffer buffer, int blockSize, SelectionMask? selection = null)
    {
        blockSize = Math.Clamp(blockSize, 2, 256); PixelBuffer source = buffer.Clone();
        for (int by = 0; by < buffer.Height; by += blockSize) for (int bx = 0; bx < buffer.Width; bx += blockSize)
        {
            long r = 0, g = 0, b = 0, a = 0, count = 0;
            for (int y = by; y < Math.Min(buffer.Height, by + blockSize); y++) for (int x = bx; x < Math.Min(buffer.Width, bx + blockSize); x++) { Rgba32 c = source.GetPixel(x, y); r += c.R; g += c.G; b += c.B; a += c.A; count++; }
            Rgba32 average = new((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
            for (int y = by; y < Math.Min(buffer.Height, by + blockSize); y++) for (int x = bx; x < Math.Min(buffer.Width, bx + blockSize); x++)
            {
                byte coverage = selection is null || selection.IsEmpty ? (byte)255 : selection.Get(x, y); if (coverage == 0) continue;
                buffer.SetPixel(x, y, coverage == 255 ? average : Rgba32.Lerp(source.GetPixel(x, y), average, coverage / 255d));
            }
        }
    }

    public static void Silhouette(PixelBuffer buffer, Rgba32 color, SelectionMask? selection = null) => Apply(buffer, selection, source => source.A == 0 ? source : color.WithAlpha((byte)(source.A * color.A / 255)));

    public static void AlphaThreshold(PixelBuffer buffer, byte threshold, bool binary = true, SelectionMask? selection = null) =>
        Apply(buffer, selection, color => color.WithAlpha(binary ? (color.A >= threshold ? (byte)255 : (byte)0) : (color.A < threshold ? (byte)0 : color.A)));

    public static int Outline(PixelBuffer buffer, Rgba32 color, int thickness = 1, bool includeDiagonals = true, bool inside = false, SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        PixelBuffer original = buffer.Clone(), transformed = buffer.Clone();
        int changed = PixelEffects.Outline(transformed, color, thickness, includeDiagonals, inside);
        BlendDerived(buffer, original, transformed, selection); return changed;
    }

    public static void OrderedDither(PixelBuffer buffer, Rgba32 dark, Rgba32 light, DitherMatrixKind matrix = DitherMatrixKind.Bayer4x4, SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        PixelBuffer original = buffer.Clone(), transformed = buffer.Clone();
        PixelEffects.ApplyOrderedDither(transformed, dark, light, matrix);
        BlendDerived(buffer, original, transformed, selection);
    }

    public static void RemoveIsolatedPixels(PixelBuffer buffer, int minimumNeighbours = 2, SelectionMask? selection = null)
    {
        minimumNeighbours = Math.Clamp(minimumNeighbours, 0, 8); PixelBuffer source = buffer.Clone();
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            if (source.GetPixel(x, y).A == 0 || (selection is not null && !selection.IsEmpty && selection.Get(x, y) == 0)) continue;
            int neighbours = 0; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && source.TryGetPixel(x + dx, y + dy, out Rgba32 c) && c.A > 0) neighbours++;
            if (neighbours < minimumNeighbours) SetDerivedPixel(buffer, source, x, y, Rgba32.Transparent, selection);
        }
    }

    public static void ExpandOpaque(PixelBuffer buffer, int radius, Rgba32? fill = null, SelectionMask? selection = null)
    {
        radius = Math.Clamp(radius, 1, 32); PixelBuffer source = buffer.Clone();
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            if (source.GetPixel(x, y).A > 0 || (selection is not null && !selection.IsEmpty && selection.Get(x, y) == 0)) continue;
            Rgba32? nearest = null; int best = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                int distance = dx * dx + dy * dy; if (distance > radius * radius || distance >= best) continue;
                if (source.TryGetPixel(x + dx, y + dy, out Rgba32 c) && c.A > 0) { best = distance; nearest = c; }
            }
            if (nearest.HasValue) SetDerivedPixel(buffer, source, x, y, fill ?? nearest.Value, selection);
        }
    }

    public static void ContractOpaque(PixelBuffer buffer, int radius, SelectionMask? selection = null)
    {
        radius = Math.Clamp(radius, 1, 32); PixelBuffer source = buffer.Clone();
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            if (source.GetPixel(x, y).A == 0 || (selection is not null && !selection.IsEmpty && selection.Get(x, y) == 0)) continue;
            bool erase = false;
            for (int dy = -radius; dy <= radius && !erase; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                if (!source.TryGetPixel(x + dx, y + dy, out Rgba32 c) || c.A == 0) { erase = true; break; }
            }
            if (erase) SetDerivedPixel(buffer, source, x, y, Rgba32.Transparent, selection);
        }
    }

    private static void BlendDerived(PixelBuffer destination, PixelBuffer original, PixelBuffer transformed, SelectionMask? selection)
    {
        for (int y = 0; y < destination.Height; y++) for (int x = 0; x < destination.Width; x++)
            SetDerivedPixel(destination, original, x, y, transformed.GetPixel(x, y), selection);
    }

    private static void SetDerivedPixel(PixelBuffer destination, PixelBuffer original, int x, int y, Rgba32 transformed, SelectionMask? selection)
    {
        byte coverage = selection is null || selection.IsEmpty ? (byte)255 : selection.Get(x, y);
        if (coverage == 0) return;
        Rgba32 before = original.GetPixel(x, y);
        destination.SetPixel(x, y, coverage == 255 ? transformed : Rgba32.Lerp(before, transformed, coverage / 255d));
    }

    private static void Apply(PixelBuffer buffer, SelectionMask? selection, Func<Rgba32, Rgba32> transform)
    {
        ArgumentNullException.ThrowIfNull(buffer); ArgumentNullException.ThrowIfNull(transform);
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            byte coverage = selection is null || selection.IsEmpty ? (byte)255 : selection.Get(x, y); if (coverage == 0) continue;
            Rgba32 before = buffer.GetPixel(x, y), after = transform(before);
            buffer.SetPixel(x, y, coverage == 255 ? after : Rgba32.Lerp(before, after, coverage / 255d));
        }
    }

    private static byte[] BuildCurve(IReadOnlyList<ColorCurvePoint> points)
    {
        ColorCurvePoint[] ordered = points.Concat([new ColorCurvePoint(0, 0), new ColorCurvePoint(255, 255)]).GroupBy(p => p.Input).Select(group => group.Last()).OrderBy(p => p.Input).ToArray();
        byte[] result = new byte[256];
        for (int segment = 0; segment < ordered.Length - 1; segment++)
        {
            ColorCurvePoint left = ordered[segment], right = ordered[segment + 1]; int span = Math.Max(1, right.Input - left.Input);
            for (int input = left.Input; input <= right.Input; input++) result[input] = Clamp(left.Output + (right.Output - left.Output) * (input - left.Input) / (double)span);
        }
        return result;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}

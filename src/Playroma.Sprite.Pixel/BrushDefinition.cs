namespace Playroma.Sprite.Pixel;

public sealed class BrushDefinition
{
    private readonly bool[] mask;

    public BrushDefinition(string name, int width, int height, ReadOnlySpan<bool> mask, int anchorX, int anchorY, BrushShape shape = BrushShape.Custom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (mask.Length != checked(width * height)) throw new ArgumentException("Brush mask dimensions do not match.", nameof(mask));
        if (anchorX < 0 || anchorX >= width) throw new ArgumentOutOfRangeException(nameof(anchorX));
        if (anchorY < 0 || anchorY >= height) throw new ArgumentOutOfRangeException(nameof(anchorY));
        Name = name.Trim(); Width = width; Height = height; AnchorX = anchorX; AnchorY = anchorY; Shape = shape;
        this.mask = mask.ToArray();
    }

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int AnchorX { get; }
    public int AnchorY { get; }
    public BrushShape Shape { get; }
    public ReadOnlyMemory<bool> Mask => mask;

    public bool IsSet(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && mask[y * Width + x];

    public IEnumerable<(int X, int Y)> EnumerateOffsets()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (mask[y * Width + x]) yield return (x - AnchorX, y - AnchorY);
    }

    public BrushDefinition Resize(int size)
    {
        size = Math.Clamp(size, 1, 256);
        if (Shape == BrushShape.Custom) return ScaleNearest(size, size);
        return CreateBuiltIn(Shape, size);
    }

    public BrushDefinition RotateClockwise()
    {
        bool[] result = new bool[mask.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                result[x * Height + (Height - 1 - y)] = IsSet(x, y);
        return new BrushDefinition(Name + " Rotated", Height, Width, result, Height - 1 - AnchorY, AnchorX, BrushShape.Custom);
    }

    public BrushDefinition FlipHorizontal()
    {
        bool[] result = new bool[mask.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++) result[y * Width + (Width - 1 - x)] = IsSet(x, y);
        return new BrushDefinition(Name + " Flipped", Width, Height, result, Width - 1 - AnchorX, AnchorY, BrushShape.Custom);
    }

    public BrushDefinition FlipVertical()
    {
        bool[] result = new bool[mask.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++) result[(Height - 1 - y) * Width + x] = IsSet(x, y);
        return new BrushDefinition(Name + " Flipped", Width, Height, result, AnchorX, Height - 1 - AnchorY, BrushShape.Custom);
    }

    public static BrushDefinition FromAlpha(PixelBuffer source, byte alphaThreshold = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool[] result = new bool[checked(source.Width * source.Height)];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) result[y * source.Width + x] = source.GetPixel(x, y).A >= alphaThreshold;
        return new BrushDefinition("Captured Brush", source.Width, source.Height, result, source.Width / 2, source.Height / 2);
    }

    public static BrushDefinition CreateBuiltIn(BrushShape shape, int size)
    {
        size = Math.Clamp(size, 1, 256);
        int width = shape == BrushShape.VerticalLine ? 1 : size;
        int height = shape == BrushShape.HorizontalLine ? 1 : size;
        bool[] result = new bool[checked(width * height)];
        double cx = (width - 1) / 2d;
        double cy = (height - 1) / 2d;
        double radiusX = Math.Max(.5, width / 2d);
        double radiusY = Math.Max(.5, height / 2d);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool set = shape switch
                {
                    BrushShape.Circle => Math.Pow((x - cx) / radiusX, 2) + Math.Pow((y - cy) / radiusY, 2) <= 1.000001,
                    BrushShape.Diamond => Math.Abs(x - cx) / radiusX + Math.Abs(y - cy) / radiusY <= 1.000001,
                    _ => true
                };
                result[y * width + x] = set;
            }
        }
        return new BrushDefinition($"{shape} {size}", width, height, result, width / 2, height / 2, shape);
    }

    private BrushDefinition ScaleNearest(int targetWidth, int targetHeight)
    {
        bool[] result = new bool[checked(targetWidth * targetHeight)];
        for (int y = 0; y < targetHeight; y++)
        {
            int sy = Math.Min(Height - 1, y * Height / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                int sx = Math.Min(Width - 1, x * Width / targetWidth);
                result[y * targetWidth + x] = IsSet(sx, sy);
            }
        }
        return new BrushDefinition(Name + $" {targetWidth}×{targetHeight}", targetWidth, targetHeight, result, targetWidth / 2, targetHeight / 2);
    }
}

public sealed class BrushPattern
{
    public BrushPattern(string name, PixelBuffer pixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim(); Pixels = pixels?.Clone() ?? throw new ArgumentNullException(nameof(pixels));
    }

    public string Name { get; }
    public PixelBuffer Pixels { get; }

    public Rgba32 Sample(int x, int y)
    {
        int wrappedX = Mod(x, Pixels.Width);
        int wrappedY = Mod(y, Pixels.Height);
        return Pixels.GetPixel(wrappedX, wrappedY);
    }

    public BrushPattern RotateClockwise()
    {
        PixelBuffer result = new(Pixels.Height, Pixels.Width);
        for (int y = 0; y < Pixels.Height; y++)
            for (int x = 0; x < Pixels.Width; x++) result.SetPixel(Pixels.Height - 1 - y, x, Pixels.GetPixel(x, y));
        return new BrushPattern(Name + " Rotated", result);
    }

    public BrushPattern FlipHorizontal()
    {
        PixelBuffer result = new(Pixels.Width, Pixels.Height);
        for (int y = 0; y < Pixels.Height; y++)
            for (int x = 0; x < Pixels.Width; x++) result.SetPixel(Pixels.Width - 1 - x, y, Pixels.GetPixel(x, y));
        return new BrushPattern(Name + " Flipped", result);
    }

    public BrushPattern FlipVertical()
    {
        PixelBuffer result = new(Pixels.Width, Pixels.Height);
        for (int y = 0; y < Pixels.Height; y++)
            for (int x = 0; x < Pixels.Width; x++) result.SetPixel(x, Pixels.Height - 1 - y, Pixels.GetPixel(x, y));
        return new BrushPattern(Name + " Flipped", result);
    }

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}

public sealed record BrushPreset(string Name, BrushDefinition Brush, BrushPattern? Pattern = null, InkMode Mode = InkMode.AlphaComposite, byte Opacity = 255);

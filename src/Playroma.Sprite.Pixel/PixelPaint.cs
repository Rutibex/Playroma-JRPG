namespace Playroma.Sprite.Pixel;

public enum BrushShape
{
    Square,
    Circle,
    Diamond,
    HorizontalLine,
    VerticalLine,
    Custom
}

public enum InkMode
{
    AlphaComposite,
    CopyColorAndAlpha,
    LockAlpha,
    Erase,
    Behind,
    ReplaceColor,
    Shade
}

[Flags]
public enum SymmetryMode
{
    None = 0,
    Horizontal = 1,
    Vertical = 2
}

[Flags]
public enum TiledMode
{
    None = 0,
    X = 1,
    Y = 2
}

public enum PatternAlignment
{
    Canvas,
    StrokeOrigin,
    BrushCenter
}

public enum DitherMatrixKind
{
    Bayer2x2,
    Bayer4x4,
    Checker,
    Diagonal
}

public sealed record StrokeDynamicsSettings
{
    public bool PressureAffectsSize { get; init; }
    public bool PressureAffectsOpacity { get; init; }
    public bool VelocityAffectsSize { get; init; }
    public bool VelocityAffectsOpacity { get; init; }
    public double MinimumSizeFactor { get; init; } = 0.2;
    public double MinimumOpacityFactor { get; init; } = 0.15;
    public double VelocityForMinimum { get; init; } = 800;
    public double PressureCurveExponent { get; init; } = 1d;
    public double VelocityCurveExponent { get; init; } = 1d;

    public StrokeDynamicsResult Evaluate(double pressure, double velocityPixelsPerSecond)
    {
        pressure = Math.Pow(Math.Clamp(pressure, 0d, 1d), Math.Clamp(PressureCurveExponent, .1d, 8d));
        double velocityFactor = Math.Pow(1d - Math.Clamp(velocityPixelsPerSecond / Math.Max(1d, VelocityForMinimum), 0d, 1d), Math.Clamp(VelocityCurveExponent, .1d, 8d));
        double size = 1d;
        double opacity = 1d;
        if (PressureAffectsSize) size *= MinimumSizeFactor + (1d - MinimumSizeFactor) * pressure;
        if (PressureAffectsOpacity) opacity *= MinimumOpacityFactor + (1d - MinimumOpacityFactor) * pressure;
        if (VelocityAffectsSize) size *= MinimumSizeFactor + (1d - MinimumSizeFactor) * velocityFactor;
        if (VelocityAffectsOpacity) opacity *= MinimumOpacityFactor + (1d - MinimumOpacityFactor) * velocityFactor;
        return new StrokeDynamicsResult(Math.Clamp(size, 0.01, 1d), Math.Clamp(opacity, 0.01, 1d));
    }
}

public readonly record struct StrokeDynamicsResult(double SizeFactor, double OpacityFactor);

public sealed record PixelPaintSettings
{
    public BrushDefinition Brush { get; init; } = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1);
    public Rgba32 PrimaryColor { get; init; } = Rgba32.Black;
    public Rgba32 SecondaryColor { get; init; } = Rgba32.White;
    public byte Opacity { get; init; } = 255;
    public InkMode InkMode { get; init; } = InkMode.AlphaComposite;
    public Rgba32 ReplaceColor { get; init; } = Rgba32.Transparent;
    public int ReplaceTolerance { get; init; }
    public IReadOnlyList<Rgba32> ShadeRamp { get; init; } = Array.Empty<Rgba32>();
    public int ShadeDirection { get; init; } = 1;
    public BrushPattern? Pattern { get; init; }
    public PatternAlignment PatternAlignment { get; init; } = PatternAlignment.Canvas;
    public SymmetryMode Symmetry { get; init; }
    public double SymmetryAxisX { get; init; }
    public double SymmetryAxisY { get; init; }
    public TiledMode TiledMode { get; init; }
    public bool PixelPerfect { get; init; }
    public StrokeDynamicsSettings Dynamics { get; init; } = new();
    public int StrokeOriginX { get; init; }
    public int StrokeOriginY { get; init; }
    public SelectionMask? Selection { get; init; }
}

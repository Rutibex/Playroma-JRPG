namespace Playroma.Sprite.Pixel;

public static class PixelCompositor
{
    public static Rgba32 Apply(Rgba32 destination, Rgba32 source, PixelPaintSettings settings, double opacityFactor = 1d)
    {
        byte effectiveAlpha = (byte)Math.Clamp((int)Math.Round(source.A * (settings.Opacity / 255d) * Math.Clamp(opacityFactor, 0d, 1d)), 0, 255);
        source = source.WithAlpha(effectiveAlpha);
        return settings.InkMode switch
        {
            InkMode.CopyColorAndAlpha => source,
            InkMode.Erase => Erase(destination, effectiveAlpha),
            InkMode.LockAlpha => destination.A == 0 ? destination : AlphaComposite(destination, source).WithAlpha(destination.A),
            InkMode.Behind => destination.A == 255 ? destination : AlphaComposite(source, destination),
            InkMode.ReplaceColor => destination.ColorDistanceSquared(settings.ReplaceColor) <= settings.ReplaceTolerance * settings.ReplaceTolerance * 4 ? source : destination,
            InkMode.Shade => Shade(destination, settings),
            _ => AlphaComposite(destination, source)
        };
    }

    public static Rgba32 AlphaComposite(Rgba32 destination, Rgba32 source)
    {
        if (source.A == 0) return destination;
        if (source.A == 255) return source;
        double sa = source.A / 255d;
        double da = destination.A / 255d;
        double outputAlpha = sa + da * (1d - sa);
        if (outputAlpha <= 0d) return Rgba32.Transparent;
        byte r = ToByte((source.R * sa + destination.R * da * (1d - sa)) / outputAlpha);
        byte g = ToByte((source.G * sa + destination.G * da * (1d - sa)) / outputAlpha);
        byte b = ToByte((source.B * sa + destination.B * da * (1d - sa)) / outputAlpha);
        return new Rgba32(r, g, b, ToByte(outputAlpha * 255d));
    }

    private static Rgba32 Erase(Rgba32 destination, byte amount)
    {
        int alpha = destination.A * (255 - amount) / 255;
        return alpha == 0 ? Rgba32.Transparent : destination.WithAlpha((byte)alpha);
    }

    private static Rgba32 Shade(Rgba32 destination, PixelPaintSettings settings)
    {
        if (settings.ShadeRamp.Count == 0) return destination;
        int closest = 0;
        int best = int.MaxValue;
        for (int index = 0; index < settings.ShadeRamp.Count; index++)
        {
            int distance = destination.ColorDistanceSquared(settings.ShadeRamp[index]);
            if (distance < best) { best = distance; closest = index; }
        }
        int target = Math.Clamp(closest + Math.Sign(settings.ShadeDirection), 0, settings.ShadeRamp.Count - 1);
        Rgba32 shaded = settings.ShadeRamp[target];
        return shaded.WithAlpha(destination.A == 0 ? shaded.A : destination.A);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}

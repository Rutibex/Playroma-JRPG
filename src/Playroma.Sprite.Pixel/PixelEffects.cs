namespace Playroma.Sprite.Pixel;

public static class PixelEffects
{
    public static int Outline(PixelBuffer buffer, Rgba32 color, int thickness = 1, bool includeDiagonals = true, bool inside = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        thickness = Math.Clamp(thickness, 1, 64);
        PixelBuffer source = buffer.Clone();
        bool[] selected = new bool[checked(buffer.Width * buffer.Height)];
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                bool opaque = source.GetPixel(x, y).A > 0;
                if (inside != opaque) continue;
                bool neighbourOpposite = false;
                for (int dy = -thickness; dy <= thickness && !neighbourOpposite; dy++)
                {
                    for (int dx = -thickness; dx <= thickness; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!includeDiagonals && dx != 0 && dy != 0) continue;
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > thickness) continue;
                        bool neighbourOpaque = source.TryGetPixel(x + dx, y + dy, out Rgba32 neighbour) && neighbour.A > 0;
                        if (neighbourOpaque != opaque) { neighbourOpposite = true; break; }
                    }
                }
                if (neighbourOpposite) selected[y * buffer.Width + x] = true;
            }
        }
        int changed = 0;
        for (int y = 0; y < buffer.Height; y++)
            for (int x = 0; x < buffer.Width; x++)
                if (selected[y * buffer.Width + x]) { buffer.SetPixel(x, y, color); changed++; }
        return changed;
    }

    public static int ReplaceColor(PixelBuffer buffer, Rgba32 from, Rgba32 to, int tolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int threshold = checked(tolerance * tolerance * 4);
        int changed = 0;
        for (int y = 0; y < buffer.Height; y++)
            for (int x = 0; x < buffer.Width; x++)
                if (buffer.GetPixel(x, y).ColorDistanceSquared(from) <= threshold) { buffer.SetPixel(x, y, to); changed++; }
        return changed;
    }

    public static void ApplyOrderedDither(PixelBuffer buffer, Rgba32 dark, Rgba32 light, DitherMatrixKind matrix = DitherMatrixKind.Bayer4x4)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                Rgba32 source = buffer.GetPixel(x, y);
                double luminance = (source.R * .2126 + source.G * .7152 + source.B * .0722) / 255d;
                Rgba32 chosen = PixelDrawing.DitherThreshold(x, y, matrix) < luminance ? light : dark;
                buffer.SetPixel(x, y, chosen.WithAlpha(source.A));
            }
        }
    }
}

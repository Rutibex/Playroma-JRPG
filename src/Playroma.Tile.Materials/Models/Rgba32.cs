using System.Drawing;

namespace Playroma.Tile.Materials.Models;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba32 Transparent => new(0, 0, 0, 0);
    public static Rgba32 FromColor(Color color) => new(color.R, color.G, color.B, color.A);
    public Color ToColor() => Color.FromArgb(A, R, G, B);

    public static Rgba32 Lerp(Rgba32 a, Rgba32 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgba32(
            (byte)Math.Clamp((int)MathF.Round(a.R + (b.R - a.R) * t), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(a.G + (b.G - a.G) * t), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(a.B + (b.B - a.B) * t), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(a.A + (b.A - a.A) * t), 0, 255));
    }
}

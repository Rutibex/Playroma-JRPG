namespace Playroma.Sprite.Pixel;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A = byte.MaxValue)
{
    public static readonly Rgba32 Transparent = new(0, 0, 0, 0);
    public static readonly Rgba32 Black = new(0, 0, 0, 255);
    public static readonly Rgba32 White = new(255, 255, 255, 255);

    public Rgba32 WithAlpha(byte alpha) => new(R, G, B, alpha);

    public int ColorDistanceSquared(Rgba32 other)
    {
        int dr = R - other.R;
        int dg = G - other.G;
        int db = B - other.B;
        int da = A - other.A;
        return dr * dr + dg * dg + db * db + da * da;
    }

    public static Rgba32 Lerp(Rgba32 from, Rgba32 to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return new Rgba32(
            LerpByte(from.R, to.R, amount),
            LerpByte(from.G, to.G, amount),
            LerpByte(from.B, to.B, amount),
            LerpByte(from.A, to.A, amount));
    }

    private static byte LerpByte(byte from, byte to, double amount) =>
        (byte)Math.Clamp((int)Math.Round(from + (to - from) * amount), 0, 255);
}

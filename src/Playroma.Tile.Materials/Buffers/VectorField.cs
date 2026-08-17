using System.Numerics;

namespace Playroma.Tile.Materials.Buffers;

public sealed class VectorField
{
    private readonly Vector2[] _values;
    public int Width { get; }
    public int Height { get; }
    public Span<Vector2> Values => _values;

    public VectorField(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; _values = new Vector2[checked(width * height)];
    }

    public Vector2 this[int x, int y] { get => _values[y * Width + x]; set => _values[y * Width + x] = value; }

    public Vector2 Sample(int x, int y, bool wrapX = true, bool wrapY = true)
    {
        x = wrapX ? Mod(x, Width) : Math.Clamp(x, 0, Width - 1);
        y = wrapY ? Mod(y, Height) : Math.Clamp(y, 0, Height - 1);
        return this[x, y];
    }

    public VectorField Clone() { var result = new VectorField(Width, Height); _values.CopyTo(result._values, 0); return result; }
    private static int Mod(int value, int modulus) { var r = value % modulus; return r < 0 ? r + modulus : r; }
}

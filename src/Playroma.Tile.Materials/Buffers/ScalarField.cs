namespace Playroma.Tile.Materials.Buffers;

public sealed class ScalarField
{
    private readonly float[] _values;
    public int Width { get; }
    public int Height { get; }
    public Span<float> Values => _values;
    public ReadOnlySpan<float> ReadOnlyValues => _values;

    public ScalarField(int width, int height, float initial = 0f)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _values = new float[checked(width * height)];
        if (initial != 0f) Array.Fill(_values, initial);
    }

    public float this[int x, int y]
    {
        get => _values[y * Width + x];
        set => _values[y * Width + x] = value;
    }

    public float Sample(int x, int y, bool wrapX = true, bool wrapY = true)
    {
        if (wrapX) x = Mod(x, Width); else x = Math.Clamp(x, 0, Width - 1);
        if (wrapY) y = Mod(y, Height); else y = Math.Clamp(y, 0, Height - 1);
        return this[x, y];
    }

    public float SampleBilinear(float x, float y, bool wrapX = true, bool wrapY = true)
    {
        var x0 = (int)MathF.Floor(x); var y0 = (int)MathF.Floor(y);
        var tx = x - x0; var ty = y - y0;
        var a = Lerp(Sample(x0, y0, wrapX, wrapY), Sample(x0 + 1, y0, wrapX, wrapY), tx);
        var b = Lerp(Sample(x0, y0 + 1, wrapX, wrapY), Sample(x0 + 1, y0 + 1, wrapX, wrapY), tx);
        return Lerp(a, b, ty);
    }

    public ScalarField Clone()
    {
        var result = new ScalarField(Width, Height);
        _values.CopyTo(result._values, 0);
        return result;
    }

    public (float Min, float Max) GetRange()
    {
        if (_values.Length == 0) return (0, 0);
        var min = _values[0]; var max = _values[0];
        foreach (var value in _values) { min = Math.Min(min, value); max = Math.Max(max, value); }
        return (min, max);
    }

    public void Normalize(float targetMin = 0f, float targetMax = 1f)
    {
        var (min, max) = GetRange();
        var span = max - min;
        if (span <= 1e-8f) { Array.Fill(_values, targetMin); return; }
        for (var i = 0; i < _values.Length; i++) _values[i] = targetMin + ((_values[i] - min) / span) * (targetMax - targetMin);
    }

    public void Clamp01()
    {
        for (var i = 0; i < _values.Length; i++) _values[i] = Math.Clamp(_values[i], 0f, 1f);
    }

    private static int Mod(int value, int modulus) { var r = value % modulus; return r < 0 ? r + modulus : r; }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

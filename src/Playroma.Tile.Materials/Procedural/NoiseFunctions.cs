using System.Numerics;

namespace Playroma.Tile.Materials.Procedural;

public static class NoiseFunctions
{
    public static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B9u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0x85EBCA6Bu;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }

    public static float Value(float x, float y, int seed, int periodX = 0, int periodY = 0)
    {
        var x0 = (int)MathF.Floor(x); var y0 = (int)MathF.Floor(y);
        var tx = Smooth(x - x0); var ty = Smooth(y - y0);
        float H(int ix, int iy)
        {
            if (periodX > 0) ix = Mod(ix, periodX);
            if (periodY > 0) iy = Mod(iy, periodY);
            return Hash01(ix, iy, seed);
        }
        var a = Lerp(H(x0, y0), H(x0 + 1, y0), tx);
        var b = Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx);
        return Lerp(a, b, ty);
    }

    public static float Perlin(float x, float y, int seed, int periodX = 0, int periodY = 0)
    {
        var x0 = (int)MathF.Floor(x); var y0 = (int)MathF.Floor(y);
        var tx = x - x0; var ty = y - y0;
        Vector2 G(int ix, int iy)
        {
            if (periodX > 0) ix = Mod(ix, periodX);
            if (periodY > 0) iy = Mod(iy, periodY);
            var angle = Hash01(ix, iy, seed) * MathF.Tau;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
        var g00 = G(x0, y0); var g10 = G(x0 + 1, y0); var g01 = G(x0, y0 + 1); var g11 = G(x0 + 1, y0 + 1);
        var d00 = Vector2.Dot(g00, new Vector2(tx, ty));
        var d10 = Vector2.Dot(g10, new Vector2(tx - 1, ty));
        var d01 = Vector2.Dot(g01, new Vector2(tx, ty - 1));
        var d11 = Vector2.Dot(g11, new Vector2(tx - 1, ty - 1));
        var u = Fade(tx); var v = Fade(ty);
        return Math.Clamp(Lerp(Lerp(d00, d10, u), Lerp(d01, d11, u), v) * 0.7071f + 0.5f, 0f, 1f);
    }

    public static (float F1, float F2, int CellId) Cellular(float x, float y, int seed, int periodX = 0, int periodY = 0)
    {
        var cx = (int)MathF.Floor(x); var cy = (int)MathF.Floor(y);
        var f1 = float.MaxValue; var f2 = float.MaxValue; var id = 0;
        for (var oy = -1; oy <= 1; oy++)
        for (var ox = -1; ox <= 1; ox++)
        {
            var gx = cx + ox; var gy = cy + oy;
            var hx = periodX > 0 ? Mod(gx, periodX) : gx;
            var hy = periodY > 0 ? Mod(gy, periodY) : gy;
            var px = gx + Hash01(hx, hy, seed);
            var py = gy + Hash01(hx, hy, seed ^ 0x51ED270B);
            var dx = x - px; var dy = y - py; var d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < f1) { f2 = f1; f1 = d; id = unchecked(hx * 73856093 ^ hy * 19349663 ^ seed); }
            else if (d < f2) f2 = d;
        }
        return (f1, f2, id);
    }

    public static float Fractal(float x, float y, int seed, int octaves, float lacunarity, float persistence, Func<float,float,int,float> basis)
    {
        var value = 0f; var amplitude = 1f; var frequency = 1f; var total = 0f;
        for (var octave = 0; octave < Math.Max(1, octaves); octave++)
        {
            value += basis(x * frequency, y * frequency, seed + octave * 1013) * amplitude;
            total += amplitude; amplitude *= persistence; frequency *= lacunarity;
        }
        return total <= 0f ? 0f : value / total;
    }

    public static float Ridged(float x, float y, int seed, int octaves, float lacunarity, float persistence)
    {
        return Fractal(x, y, seed, octaves, lacunarity, persistence, (a,b,s) => { var n = Perlin(a,b,s); return 1f - MathF.Abs(n * 2f - 1f); });
    }

    public static float Billow(float x, float y, int seed, int octaves, float lacunarity, float persistence)
    {
        return Fractal(x, y, seed, octaves, lacunarity, persistence, (a,b,s) => MathF.Abs(Perlin(a,b,s) * 2f - 1f));
    }

    public static float Turbulence(float x, float y, int seed, int octaves, float lacunarity, float persistence) => Billow(x, y, seed, octaves, lacunarity, persistence);
    public static int Mod(int value, int modulus) { var r = value % modulus; return r < 0 ? r + modulus : r; }
    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

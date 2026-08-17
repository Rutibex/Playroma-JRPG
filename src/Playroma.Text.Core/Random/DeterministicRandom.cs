using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Playroma.Text.Core.Random;

public interface IDeterministicRandom
{
    ulong Seed { get; }
    ulong NextUInt64();
    int NextInt(int exclusiveMax);
    int NextInt(int inclusiveMin, int exclusiveMax);
    double NextDouble();
    bool NextBool(double probability = 0.5);
    IDeterministicRandom Fork(string scope);
}

public static class SeedFactory
{
    public static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public static ulong Derive(ulong seed, string scope)
    {
        var scopeBytes = Encoding.UTF8.GetBytes(scope ?? string.Empty);
        var input = new byte[8 + scopeBytes.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(0, 8), seed);
        scopeBytes.CopyTo(input, 8);
        var hash = SHA256.HashData(input);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8));
    }
}

public sealed class Xoshiro256StarStar : IDeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;
    public ulong Seed { get; }

    public Xoshiro256StarStar(ulong seed)
    {
        Seed = seed;
        var sm = new SplitMix64(seed);
        _s0 = sm.Next(); _s1 = sm.Next(); _s2 = sm.Next(); _s3 = sm.Next();
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    public ulong NextUInt64()
    {
        // xoshiro256** is defined in terms of modulo-2^64 arithmetic.
        // Directory.Build.props enables checked arithmetic globally, so the
        // intentional wraparound in the generator must be explicitly unchecked.
        unchecked
        {
            var result = RotateLeft(_s1 * 5, 7) * 9;
            var t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }
    }

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        return (int)(NextUInt64Bounded((ulong)exclusiveMax));
    }

    public int NextInt(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        // Compute the width in 64-bit space.  With checked arithmetic enabled,
        // subtracting int.MinValue from int.MaxValue would otherwise overflow.
        var width = (ulong)((long)exclusiveMax - inclusiveMin);
        var offset = (long)NextUInt64Bounded(width);
        return checked((int)((long)inclusiveMin + offset));
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    public bool NextBool(double probability = 0.5)
    {
        if (probability is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(probability));
        return NextDouble() < probability;
    }

    public IDeterministicRandom Fork(string scope) => new Xoshiro256StarStar(SeedFactory.Derive(Seed, scope));

    private ulong NextUInt64Bounded(ulong bound)
    {
        var threshold = unchecked(0UL - bound) % bound;
        while (true) { var r = NextUInt64(); if (r >= threshold) return r % bound; }
    }
    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    private sealed class SplitMix64
    {
        private ulong _state;
        public SplitMix64(ulong seed) => _state = seed;

        public ulong Next()
        {
            // SplitMix64 also relies on modulo-2^64 wraparound.  An overflow
            // here is normal PRNG behavior, not an error condition.
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}

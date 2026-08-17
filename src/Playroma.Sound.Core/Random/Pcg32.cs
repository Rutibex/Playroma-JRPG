namespace Playroma.Sound.Core.Random;

public readonly record struct Pcg32Snapshot(ulong State, ulong Increment);

/// <summary>
/// Small, fast PCG32 pseudo-random generator with stable cross-runtime output.
/// The internal state may be captured for deterministic streaming-scene snapshots.
/// </summary>
public struct Pcg32
{
    private const ulong Multiplier = 6_364_136_223_846_793_005UL;
    private ulong _state;
    private readonly ulong _increment;

    public Pcg32(ulong seed, ulong stream = 54UL)
    {
        // PCG initialization deliberately uses modulo-2^64 arithmetic. The
        // solution enables checked arithmetic globally, so the wraparound must
        // be stated explicitly rather than relying on the project default.
        unchecked
        {
            _state = 0UL;
            _increment = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }
    }

    public Pcg32(Pcg32Snapshot snapshot)
    {
        if ((snapshot.Increment & 1UL) == 0)
        {
            throw new ArgumentException("A PCG increment must be odd.", nameof(snapshot));
        }

        _state = snapshot.State;
        _increment = snapshot.Increment;
    }

    public readonly Pcg32Snapshot CaptureState() => new(_state, _increment);

    public uint NextUInt()
    {
        // Every operation in the PCG output permutation is defined in terms of
        // fixed-width wraparound arithmetic. This includes the narrowing cast
        // from the 64-bit xorshift result to 32 bits; in a checked project that
        // cast otherwise throws for approximately half of all generator states.
        unchecked
        {
            ulong oldState = _state;
            _state = (oldState * Multiplier) + _increment;
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }
    }

    public float NextFloat()
    {
        return (NextUInt() >> 8) * (1f / 16_777_216f);
    }

    public float NextSignedFloat() => (NextFloat() * 2f) - 1f;

    /// <summary>Returns a deterministic double in the half-open interval [0, 1).</summary>
    public double NextDouble()
    {
        ulong high = NextUInt() >> 5;
        ulong low = NextUInt() >> 6;
        return ((high << 26) + low) * (1.0 / 9_007_199_254_740_992.0);
    }

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        uint bound = (uint)exclusiveMax;
        uint threshold = unchecked((uint)(-bound)) % bound;

        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }
}

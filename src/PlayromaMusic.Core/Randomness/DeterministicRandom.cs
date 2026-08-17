namespace PlayromaMusic.Core.Randomness;

public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        // SplitMix64 is specified using modulo-2^64 wraparound arithmetic.
        // The repository enables checked arithmetic globally, so keep the
        // intentional overflow local and explicit.
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        ulong bound = (ulong)exclusiveMaximum;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    public int NextInt(int inclusiveMinimum, int exclusiveMaximum)
    {
        if (exclusiveMaximum <= inclusiveMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        return inclusiveMinimum + NextInt(exclusiveMaximum - inclusiveMinimum);
    }

    public double NextDouble() =>
        (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public bool Chance(double probability)
    {
        if (probability is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        return NextDouble() < probability;
    }

    public T Pick<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("The collection cannot be empty.", nameof(values));
        }

        return values[NextInt(values.Count)];
    }
}

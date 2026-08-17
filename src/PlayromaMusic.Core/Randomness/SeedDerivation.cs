using System.Text;

namespace PlayromaMusic.Core.Randomness;

public static class SeedDerivation
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Derive(ulong masterSeed, params string[] path)
    {
        // FNV-1a and the final avalanche mixer are deliberately defined with
        // modulo-2^64 arithmetic. The solution enables checked arithmetic
        // globally, so this method must explicitly opt back into wraparound.
        unchecked
        {
            ulong hash = FnvOffsetBasis;

            for (int index = 0; index < 8; index++)
            {
                hash ^= (byte)(masterSeed >> (index * 8));
                hash *= FnvPrime;
            }

            foreach (string component in path)
            {
                hash ^= 0xFF;
                hash *= FnvPrime;

                foreach (byte value in Encoding.UTF8.GetBytes(component ?? string.Empty))
                {
                    hash ^= value;
                    hash *= FnvPrime;
                }
            }

            return Avalanche(hash);
        }
    }

    public static string StableId(string prefix, ulong masterSeed, params string[] path)
    {
        ulong value = Derive(masterSeed, path);
        return $"{prefix}-{value:x16}";
    }

    private static ulong Avalanche(ulong value)
    {
        // SplitMix64-style avalanche; overflow is intentional and required.
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}

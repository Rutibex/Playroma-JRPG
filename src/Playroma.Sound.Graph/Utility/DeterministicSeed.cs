using System.Text;

namespace Playroma.Sound.Graph.Utility;

public static class DeterministicSeed
{
    public static ulong Derive(ulong rootSeed, string nodeId, int variationIndex = 0, string streamName = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);

        ulong hash = 14695981039346656037UL ^ rootSeed;
        HashBytes(ref hash, Encoding.UTF8.GetBytes(nodeId));
        HashBytes(ref hash, BitConverter.GetBytes(variationIndex));
        HashBytes(ref hash, Encoding.UTF8.GetBytes(streamName));
        return Mix(hash);
    }

    private static void HashBytes(ref ulong hash, ReadOnlySpan<byte> bytes)
    {
        unchecked
        {
            foreach (byte value in bytes)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebUL;
            value ^= value >> 31;
            return value == 0 ? 1UL : value;
        }
    }
}

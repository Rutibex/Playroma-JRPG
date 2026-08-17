using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PlayromaMusic.Core.Randomness;

public readonly record struct EntropySeed(ulong Seed, string Hex)
{
    public override string ToString() => Hex;
}

public static class CryptographicSeedGenerator
{
    public static EntropySeed Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return new EntropySeed(seed, Convert.ToHexString(bytes));
    }

    public static ulong CreateUInt64()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }
}

namespace Playroma.JRPG.Documents.Json;

public static class Utf8JsonInput
{
    public static bool HasBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    public static byte[] ToBomlessArray(ReadOnlySpan<byte> bytes) =>
        (HasBom(bytes) ? bytes[3..] : bytes).ToArray();
}

using System.Text;

namespace Playroma.JRPG.Documents;

public static class Utf8TextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes) => StrictUtf8.GetString(bytes);

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = Decode(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}

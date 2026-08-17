using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Playroma.JRPG.Documents.Json;

public static class JsonValueEncoder
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static byte[] EncodeString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, CompactOptions);
    }

    public static byte[] EncodeBoolean(bool value) => value ? "true"u8.ToArray() : "false"u8.ToArray();

    public static byte[] EncodeNull() => "null"u8.ToArray();

    public static byte[] EncodeInt32(int value) => Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));

    public static byte[] EncodeInt64(long value) => Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));

    public static byte[] EncodeDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
        return Encoding.UTF8.GetBytes(value.ToString("R", CultureInfo.InvariantCulture));
    }

    public static byte[] ValidateRawJson(ReadOnlySpan<byte> value)
    {
        using JsonDocument document = JsonDocument.Parse(value.ToArray());
        return value.ToArray();
    }
}

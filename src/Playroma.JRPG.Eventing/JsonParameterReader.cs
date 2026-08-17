using System.Text.Json;

namespace Playroma.JRPG.Eventing;

public static class JsonParameterReader
{
    public static JsonElement ParseArray(string rawJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using JsonDocument fallback = JsonDocument.Parse("[]");
            return fallback.RootElement.Clone();
        }
    }

    public static bool TryGet(JsonElement array, int index, out JsonElement value)
    {
        value = default;
        if (array.ValueKind != JsonValueKind.Array || index < 0 || index >= array.GetArrayLength()) return false;
        value = array[index];
        return true;
    }

    public static int GetInt(JsonElement array, int index, int fallback = 0)
    {
        if (!TryGet(array, index, out JsonElement value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)) return result;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result)) return result;
        return fallback;
    }

    public static bool GetBool(JsonElement array, int index, bool fallback = false)
    {
        if (!TryGet(array, index, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out int number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => fallback
        };
    }

    public static string GetString(JsonElement array, int index, string fallback = "")
    {
        if (!TryGet(array, index, out JsonElement value)) return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.GetRawText();
    }

    public static string GetObjectString(JsonElement array, int index, string property, string fallback = "")
    {
        if (!TryGet(array, index, out JsonElement value) || value.ValueKind != JsonValueKind.Object) return fallback;
        return value.TryGetProperty(property, out JsonElement member) && member.ValueKind == JsonValueKind.String
            ? member.GetString() ?? fallback
            : fallback;
    }

    public static string Compact(JsonElement value, int maximumLength = 100)
    {
        string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        text = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return text.Length <= maximumLength ? text : text[..Math.Max(0, maximumLength - 1)] + "…";
    }
}

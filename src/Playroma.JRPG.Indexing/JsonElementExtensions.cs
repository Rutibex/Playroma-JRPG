using System.Globalization;
using System.Text.Json;

namespace Playroma.JRPG.Indexing;

public static class JsonElementExtensions
{
    public static bool TryGetPropertyValue(this JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static int? GetOptionalInt32(this JsonElement element, string propertyName)
    {
        if (!element.TryGetPropertyValue(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    public static string? GetOptionalString(this JsonElement element, string propertyName)
    {
        if (!element.TryGetPropertyValue(propertyName, out JsonElement value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static bool GetOptionalBoolean(this JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (!element.TryGetPropertyValue(propertyName, out JsonElement value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    public static IEnumerable<(int Index, JsonElement Value)> EnumerateIndexedArray(this JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) yield break;
        int index = 0;
        foreach (JsonElement value in element.EnumerateArray())
        {
            yield return (index, value);
            index++;
        }
    }
}

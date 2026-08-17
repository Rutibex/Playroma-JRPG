using System.Globalization;
using System.Text.Json;

namespace Playroma.JRPG.Documents.Json;

public sealed record JsonSemanticDifference(JsonPath Path, string Kind, string? Before, string? After);

public static class JsonSemanticDiffer
{
    public static IReadOnlyList<JsonSemanticDifference> Compare(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        using JsonDocument left = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(before));
        using JsonDocument right = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(after));
        List<JsonSemanticDifference> differences = [];
        CompareElement(left.RootElement, right.RootElement, JsonPath.Root, differences);
        return differences;
    }

    private static void CompareElement(
        JsonElement left,
        JsonElement right,
        JsonPath path,
        ICollection<JsonSemanticDifference> differences)
    {
        if (left.ValueKind != right.ValueKind)
        {
            differences.Add(new JsonSemanticDifference(path, "kind", left.GetRawText(), right.GetRawText()));
            return;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObjects(left, right, path, differences);
                break;
            case JsonValueKind.Array:
                CompareArrays(left, right, path, differences);
                break;
            case JsonValueKind.String:
                if (!string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal))
                {
                    differences.Add(new JsonSemanticDifference(path, "value", left.GetRawText(), right.GetRawText()));
                }
                break;
            case JsonValueKind.Number:
                if (!NumericEqual(left, right))
                {
                    differences.Add(new JsonSemanticDifference(path, "value", left.GetRawText(), right.GetRawText()));
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                if (!string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal))
                {
                    differences.Add(new JsonSemanticDifference(path, "value", left.GetRawText(), right.GetRawText()));
                }
                break;
            default:
                break;
        }
    }

    private static void CompareObjects(
        JsonElement left,
        JsonElement right,
        JsonPath path,
        ICollection<JsonSemanticDifference> differences)
    {
        // System.Text.Json permits duplicate object-property names. Treat every occurrence
        // as significant instead of silently comparing only the last value. This matters
        // for compatibility projects that contain plugin-created or hand-edited JSON.
        Dictionary<string, JsonElement[]> leftProperties = left.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(property => property.Value).ToArray(),
                StringComparer.Ordinal);
        Dictionary<string, JsonElement[]> rightProperties = right.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(property => property.Value).ToArray(),
                StringComparer.Ordinal);

        foreach (string name in leftProperties.Keys
                     .Union(rightProperties.Keys, StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            JsonElement[] leftValues = leftProperties.TryGetValue(name, out JsonElement[]? foundLeft)
                ? foundLeft ?? []
                : [];
            JsonElement[] rightValues = rightProperties.TryGetValue(name, out JsonElement[]? foundRight)
                ? foundRight ?? []
                : [];
            int maximum = Math.Max(leftValues.Length, rightValues.Length);
            bool hasDuplicates = maximum > 1;
            for (int occurrence = 0; occurrence < maximum; occurrence++)
            {
                JsonPath propertyPath = path.Append(hasDuplicates
                    ? $"{name}[occurrence={occurrence}]"
                    : name);
                if (occurrence >= leftValues.Length)
                {
                    differences.Add(new JsonSemanticDifference(
                        propertyPath,
                        "added",
                        null,
                        rightValues[occurrence].GetRawText()));
                }
                else if (occurrence >= rightValues.Length)
                {
                    differences.Add(new JsonSemanticDifference(
                        propertyPath,
                        "removed",
                        leftValues[occurrence].GetRawText(),
                        null));
                }
                else
                {
                    CompareElement(leftValues[occurrence], rightValues[occurrence], propertyPath, differences);
                }
            }
        }
    }

    private static void CompareArrays(
        JsonElement left,
        JsonElement right,
        JsonPath path,
        ICollection<JsonSemanticDifference> differences)
    {
        JsonElement.ArrayEnumerator leftEnumerator = left.EnumerateArray();
        JsonElement.ArrayEnumerator rightEnumerator = right.EnumerateArray();
        JsonElement[] leftValues = leftEnumerator.ToArray();
        JsonElement[] rightValues = rightEnumerator.ToArray();
        int maximum = Math.Max(leftValues.Length, rightValues.Length);
        for (int index = 0; index < maximum; index++)
        {
            JsonPath elementPath = path.Append(index);
            if (index >= leftValues.Length)
            {
                differences.Add(new JsonSemanticDifference(elementPath, "added", null, rightValues[index].GetRawText()));
            }
            else if (index >= rightValues.Length)
            {
                differences.Add(new JsonSemanticDifference(elementPath, "removed", leftValues[index].GetRawText(), null));
            }
            else
            {
                CompareElement(leftValues[index], rightValues[index], elementPath, differences);
            }
        }
    }

    private static bool NumericEqual(JsonElement left, JsonElement right)
    {
        if (left.TryGetInt64(out long leftInteger) && right.TryGetInt64(out long rightInteger))
        {
            return leftInteger == rightInteger;
        }

        if (left.TryGetDecimal(out decimal leftDecimal) && right.TryGetDecimal(out decimal rightDecimal))
        {
            return leftDecimal == rightDecimal;
        }

        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Semantics;

public enum SemanticFeatureType
{
    String,
    Integer,
    Number,
    Boolean,
    StringList
}

/// <summary>
/// Serializable, deterministic semantic value used by generated bundles and grammar rules.
/// Scalar values are stored canonically so they remain stable across JSON round trips.
/// </summary>
public sealed record SemanticFeatureValue(
    SemanticFeatureType Type,
    string? Value = null,
    IReadOnlyList<string>? Values = null)
{
    [JsonIgnore] public IReadOnlyList<string> EffectiveValues => Values ?? Array.Empty<string>();

    public static SemanticFeatureValue FromString(string? value) => new(SemanticFeatureType.String, value ?? string.Empty);
    public static SemanticFeatureValue FromInteger(long value) => new(SemanticFeatureType.Integer, value.ToString(CultureInfo.InvariantCulture));
    public static SemanticFeatureValue FromNumber(double value) => new(SemanticFeatureType.Number, value.ToString("R", CultureInfo.InvariantCulture));
    public static SemanticFeatureValue FromBoolean(bool value) => new(SemanticFeatureType.Boolean, value ? "true" : "false");
    public static SemanticFeatureValue FromStrings(IEnumerable<string> values) => new(SemanticFeatureType.StringList, Values: values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray());

    public static SemanticFeatureValue FromObject(object? value) => value switch
    {
        null => FromString(string.Empty),
        SemanticFeatureValue semantic => semantic,
        bool b => FromBoolean(b),
        byte b => FromInteger(b),
        sbyte b => FromInteger(b),
        short i => FromInteger(i),
        ushort i => FromInteger(i),
        int i => FromInteger(i),
        uint i => FromInteger(i),
        long i => FromInteger(i),
        ulong i when i <= (ulong)long.MaxValue => FromInteger((long)i),
        float d => FromNumber(d),
        double d => FromNumber(d),
        decimal d => FromNumber((double)d),
        IEnumerable<string> values => FromStrings(values),
        _ => FromString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
    };

    public static SemanticFeatureValue FromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("type", out var typeElement))
        {
            var typeText = typeElement.GetString() ?? "string";
            if (!Enum.TryParse<SemanticFeatureType>(typeText, true, out var declared)) declared = SemanticFeatureType.String;
            if (element.TryGetProperty("value", out var declaredValue)) return FromDeclared(declared, declaredValue);
            if (element.TryGetProperty("values", out var declaredValues)) return FromDeclared(declared, declaredValues);
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => FromString(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => FromInteger(integer),
            JsonValueKind.Number => FromNumber(element.GetDouble()),
            JsonValueKind.True => FromBoolean(true),
            JsonValueKind.False => FromBoolean(false),
            JsonValueKind.Array => FromStrings(element.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? string.Empty : x.ToString())),
            JsonValueKind.Null => FromString(string.Empty),
            _ => FromString(element.ToString())
        };
    }

    private static SemanticFeatureValue FromDeclared(SemanticFeatureType type, JsonElement element)
    {
        try
        {
            return type switch
            {
                SemanticFeatureType.Integer => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var i)
                    ? FromInteger(i) : FromInteger(long.Parse(element.ToString(), CultureInfo.InvariantCulture)),
                SemanticFeatureType.Number => element.ValueKind == JsonValueKind.Number
                    ? FromNumber(element.GetDouble()) : FromNumber(double.Parse(element.ToString(), CultureInfo.InvariantCulture)),
                SemanticFeatureType.Boolean => element.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? FromBoolean(element.GetBoolean()) : FromBoolean(bool.Parse(element.ToString())),
                SemanticFeatureType.StringList => element.ValueKind == JsonValueKind.Array
                    ? FromStrings(element.EnumerateArray().Select(x => x.ToString()))
                    : FromStrings(element.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                _ => FromString(element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString())
            };
        }
        catch
        {
            return FromString(element.ToString());
        }
    }

    public object? ToObject() => Type switch
    {
        SemanticFeatureType.Integer when long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        SemanticFeatureType.Number when double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        SemanticFeatureType.Boolean when bool.TryParse(Value, out var b) => b,
        SemanticFeatureType.StringList => EffectiveValues,
        _ => Value ?? string.Empty
    };

    [JsonIgnore] public string DisplayText => Type == SemanticFeatureType.StringList ? string.Join(", ", EffectiveValues) : Value ?? string.Empty;
    [JsonIgnore] public bool IsWildcard => Type == SemanticFeatureType.String && (Value == "*" || string.Equals(Value, "any", StringComparison.OrdinalIgnoreCase));

    public bool TryGetNumber(out double value)
    {
        if (Type is (SemanticFeatureType.Integer or SemanticFeatureType.Number) && double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        value = 0;
        return false;
    }

    public bool EquivalentTo(SemanticFeatureValue other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (IsWildcard || other.IsWildcard) return true;
        if (TryGetNumber(out var left) && other.TryGetNumber(out var right)) return Math.Abs(left - right) <= 1e-12;
        if (Type == SemanticFeatureType.Boolean && other.Type == SemanticFeatureType.Boolean)
            return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
        if (Type == SemanticFeatureType.StringList || other.Type == SemanticFeatureType.StringList)
        {
            var a = Type == SemanticFeatureType.StringList ? EffectiveValues : [Value ?? string.Empty];
            var b = other.Type == SemanticFeatureType.StringList ? other.EffectiveValues : [other.Value ?? string.Empty];
            return a.Count == b.Count && a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }
        return string.Equals(DisplayText, other.DisplayText, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SemanticUnificationConflict(string Feature, SemanticFeatureValue Existing, SemanticFeatureValue Required);

public static class SemanticFeatureUnifier
{
    public static bool TryUnify(SemanticFeatureValue existing, SemanticFeatureValue required, out SemanticFeatureValue unified)
    {
        if (existing.IsWildcard) { unified = required; return true; }
        if (required.IsWildcard) { unified = existing; return true; }
        if (existing.EquivalentTo(required)) { unified = existing; return true; }

        if (existing.Type == SemanticFeatureType.StringList || required.Type == SemanticFeatureType.StringList)
        {
            var left = existing.Type == SemanticFeatureType.StringList ? existing.EffectiveValues : [existing.DisplayText];
            var right = required.Type == SemanticFeatureType.StringList ? required.EffectiveValues : [required.DisplayText];
            var intersection = left.Intersect(right, StringComparer.OrdinalIgnoreCase).ToArray();
            if (intersection.Length > 0)
            {
                unified = intersection.Length == 1 ? SemanticFeatureValue.FromString(intersection[0]) : SemanticFeatureValue.FromStrings(intersection);
                return true;
            }
        }

        unified = existing;
        return false;
    }

    public static IReadOnlyDictionary<string, SemanticFeatureValue> Empty { get; } =
        new ReadOnlyDictionary<string, SemanticFeatureValue>(new Dictionary<string, SemanticFeatureValue>(StringComparer.OrdinalIgnoreCase));
}

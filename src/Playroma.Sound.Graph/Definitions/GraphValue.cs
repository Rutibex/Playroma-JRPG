using System.Globalization;

namespace Playroma.Sound.Graph.Definitions;

public enum GraphValueKind
{
    Null,
    Number,
    Boolean,
    String
}

/// <summary>
/// Small JSON-friendly value used by graph node parameters. Complex runtime state is never serialized.
/// </summary>
public readonly record struct GraphValue
{
    private GraphValue(GraphValueKind kind, double number, bool boolean, string? text)
    {
        Kind = kind;
        Number = number;
        Boolean = boolean;
        Text = text;
    }

    public GraphValueKind Kind { get; }
    public double Number { get; }
    public bool Boolean { get; }
    public string? Text { get; }

    public static GraphValue Null => new(GraphValueKind.Null, 0, false, null);
    public static GraphValue FromNumber(double value) => new(GraphValueKind.Number, value, false, null);
    public static GraphValue FromBoolean(bool value) => new(GraphValueKind.Boolean, 0, value, null);
    public static GraphValue FromString(string value) => new(GraphValueKind.String, 0, false, value ?? throw new ArgumentNullException(nameof(value)));

    public double AsDouble(double fallback = 0) => Kind switch
    {
        GraphValueKind.Number when double.IsFinite(Number) => Number,
        GraphValueKind.Boolean => Boolean ? 1 : 0,
        GraphValueKind.String when double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) => value,
        _ => fallback
    };

    public float AsFloat(float fallback = 0) => (float)AsDouble(fallback);

    public int AsInt32(int fallback = 0)
    {
        double value = AsDouble(fallback);
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            return fallback;
        }

        return (int)Math.Round(value);
    }

    public ulong AsUInt64(ulong fallback = 0)
    {
        double value = AsDouble(fallback);
        if (!double.IsFinite(value) || value < 0 || value > ulong.MaxValue)
        {
            return fallback;
        }

        return (ulong)Math.Round(value);
    }

    public bool AsBoolean(bool fallback = false) => Kind switch
    {
        GraphValueKind.Boolean => Boolean,
        GraphValueKind.Number => Number != 0,
        GraphValueKind.String when bool.TryParse(Text, out bool value) => value,
        _ => fallback
    };

    public string AsString(string fallback = "") => Kind switch
    {
        GraphValueKind.String => Text ?? fallback,
        GraphValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
        GraphValueKind.Boolean => Boolean.ToString(CultureInfo.InvariantCulture),
        _ => fallback
    };

    public static implicit operator GraphValue(double value) => FromNumber(value);
    public static implicit operator GraphValue(float value) => FromNumber(value);
    public static implicit operator GraphValue(int value) => FromNumber(value);
    public static implicit operator GraphValue(long value) => FromNumber(value);
    public static implicit operator GraphValue(bool value) => FromBoolean(value);
    public static implicit operator GraphValue(string value) => FromString(value);
}

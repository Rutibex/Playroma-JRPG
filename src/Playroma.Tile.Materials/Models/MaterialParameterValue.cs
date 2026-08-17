using System.Drawing;
using System.Globalization;

namespace Playroma.Tile.Materials.Models;

public sealed class MaterialParameterValue
{
    public MaterialParameterKind Kind { get; set; } = MaterialParameterKind.String;
    public string Value { get; set; } = string.Empty;

    public static MaterialParameterValue From(string value) => new() { Kind = MaterialParameterKind.String, Value = value ?? string.Empty };
    public static MaterialParameterValue From(int value) => new() { Kind = MaterialParameterKind.Integer, Value = value.ToString(CultureInfo.InvariantCulture) };
    public static MaterialParameterValue From(float value) => new() { Kind = MaterialParameterKind.Float, Value = value.ToString("R", CultureInfo.InvariantCulture) };
    public static MaterialParameterValue From(double value) => From((float)value);
    public static MaterialParameterValue From(bool value) => new() { Kind = MaterialParameterKind.Boolean, Value = value ? "true" : "false" };
    public static MaterialParameterValue From(Color value) => new() { Kind = MaterialParameterKind.Color, Value = $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}" };
    public static MaterialParameterValue FromEnum<T>(T value) where T : struct, Enum => new() { Kind = MaterialParameterKind.Enum, Value = value.ToString() };

    public string AsString(string fallback = "") => string.IsNullOrWhiteSpace(Value) ? fallback : Value;
    public int AsInt(int fallback = 0) => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    public float AsFloat(float fallback = 0f) => float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    public bool AsBool(bool fallback = false) => bool.TryParse(Value, out var result) ? result : fallback;
    public T AsEnum<T>(T fallback) where T : struct, Enum => Enum.TryParse<T>(Value, true, out var result) ? result : fallback;

    public Color AsColor(Color fallback)
    {
        var text = Value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (text.StartsWith('#')) text = text[1..];
        try
        {
            return text.Length switch
            {
                8 => Color.FromArgb(Convert.ToByte(text[..2], 16), Convert.ToByte(text.Substring(2, 2), 16), Convert.ToByte(text.Substring(4, 2), 16), Convert.ToByte(text.Substring(6, 2), 16)),
                6 => Color.FromArgb(255, Convert.ToByte(text[..2], 16), Convert.ToByte(text.Substring(2, 2), 16), Convert.ToByte(text.Substring(4, 2), 16)),
                _ => Color.FromName(text)
            };
        }
        catch { return fallback; }
    }

    public MaterialParameterValue Clone() => new() { Kind = Kind, Value = Value };
    public override string ToString() => Value;
}

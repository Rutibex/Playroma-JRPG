using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public sealed record PluginValueValidationResult(bool IsValid, PluginValue Value, string? Error)
{
    public static PluginValueValidationResult Success(PluginValue value) => new(true, value, null);
    public static PluginValueValidationResult Failure(string error, string raw) => new(false, PluginValue.FromText(raw), error);
}

/// <summary>Shared parsing, canonicalization, and validation for WinForms and future UIs.</summary>
public static class PluginTypedValueCodec
{
    private static readonly Regex IntegerPattern = new("[-+]?[0-9]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex ColorPattern = new("^(?:#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}|rgba?\\([^)]*\\)|[A-Za-z]+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex CoordinatePattern = new("^\\s*[-+]?[0-9]+(?:\\.[0-9]+)?\\s*[, ]\\s*[-+]?[0-9]+(?:\\.[0-9]+)?\\s*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static PluginValueValidationResult Parse(PluginArgumentDefinition definition, string? raw)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string text = raw?.Trim() ?? string.Empty;
        if (definition.IsRequired && text.Length == 0)
            return PluginValueValidationResult.Failure($"{definition.DisplayName} is required.", text);
        if (text.Length == 0) return PluginValueValidationResult.Success(PluginValue.FromText(string.Empty));
        if (definition.Choices is { Count: > 0 } && !definition.Choices.Contains(text, StringComparer.OrdinalIgnoreCase))
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must be one of the listed values.", text);

        PluginValueValidationResult result = definition.Kind switch
        {
            PluginArgumentKind.Boolean => ParseBoolean(definition, text),
            PluginArgumentKind.Integer => ParseInteger(definition, text),
            PluginArgumentKind.Decimal => ParseDecimal(definition, text, false),
            PluginArgumentKind.Percentage => ParseDecimal(definition, text.TrimEnd('%'), true),
            PluginArgumentKind.DatabaseIdList => ParseIdList(definition, text),
            PluginArgumentKind.DatabaseIdRange => ParseRange(definition, text),
            PluginArgumentKind.DatabaseId or PluginArgumentKind.SwitchId or PluginArgumentKind.VariableId or
            PluginArgumentKind.CommonEventId or PluginArgumentKind.AnimationId or PluginArgumentKind.StateId or
            PluginArgumentKind.SkillId or PluginArgumentKind.ItemId or PluginArgumentKind.WeaponId or
            PluginArgumentKind.ArmorId or PluginArgumentKind.ClassId or PluginArgumentKind.ActorId or
            PluginArgumentKind.EnemyId or PluginArgumentKind.RegionId or PluginArgumentKind.IconId => ParseIdentifier(definition, text),
            PluginArgumentKind.Color => ColorPattern.IsMatch(text)
                ? PluginValueValidationResult.Success(PluginValue.FromText(text))
                : PluginValueValidationResult.Failure($"{definition.DisplayName} must be a named, hex, rgb, or rgba color.", text),
            PluginArgumentKind.Coordinate => CoordinatePattern.IsMatch(text)
                ? PluginValueValidationResult.Success(PluginValue.FromText(NormalizeCoordinate(text)))
                : PluginValueValidationResult.Failure($"{definition.DisplayName} must contain X and Y values.", text),
            PluginArgumentKind.JavaScriptExpression or PluginArgumentKind.Formula =>
                PluginValueValidationResult.Success(PluginValue.FromExpression(text)),
            PluginArgumentKind.JavaScriptBlock => PluginValueValidationResult.Success(PluginValue.FromBlock(raw ?? string.Empty)),
            _ => PluginValueValidationResult.Success(PluginValue.FromText(raw ?? string.Empty))
        };
        return result.IsValid ? ValidateBounds(definition, result) : result;
    }

    public static string Canonicalize(PluginArgumentDefinition definition, string? raw)
    {
        PluginValueValidationResult result = Parse(definition, raw);
        return result.IsValid ? result.Value.Text : raw?.Trim() ?? string.Empty;
    }

    public static IReadOnlyList<int> ParseIdentifiers(string? raw) => IntegerPattern.Matches(raw ?? string.Empty)
        .Cast<Match>()
        .Select(match => int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0)
        .Where(id => id >= 0)
        .Distinct()
        .ToArray();

    public static string MergeIdentifierLists(string? left, string? right) => string.Join(", ",
        ParseIdentifiers(left).Concat(ParseIdentifiers(right)).Distinct().OrderBy(id => id));

    private static PluginValueValidationResult ParseBoolean(PluginArgumentDefinition definition, string text)
    {
        if (bool.TryParse(text, out bool value)) return PluginValueValidationResult.Success(PluginValue.FromBoolean(value));
        if (text == "1") return PluginValueValidationResult.Success(PluginValue.FromBoolean(true));
        if (text == "0") return PluginValueValidationResult.Success(PluginValue.FromBoolean(false));
        return PluginValueValidationResult.Failure($"{definition.DisplayName} must be true or false.", text);
    }

    private static PluginValueValidationResult ParseInteger(PluginArgumentDefinition definition, string text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must be an integer.", text);
        return PluginValueValidationResult.Success(PluginValue.FromInteger(value));
    }

    private static PluginValueValidationResult ParseIdentifier(PluginArgumentDefinition definition, string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must be a non-negative database ID.", text);
        return PluginValueValidationResult.Success(PluginValue.FromIdentifier(value));
    }

    private static PluginValueValidationResult ParseDecimal(PluginArgumentDefinition definition, string text, bool percentage)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must be a number.", text);
        return PluginValueValidationResult.Success(percentage ? PluginValue.FromPercentage(value) : PluginValue.FromDecimal(value));
    }

    private static PluginValueValidationResult ParseIdList(PluginArgumentDefinition definition, string text)
    {
        IReadOnlyList<int> values = ParseIdentifiers(text);
        if (values.Count == 0)
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must contain at least one ID.", text);
        return PluginValueValidationResult.Success(PluginValue.FromIdentifierList(values));
    }

    private static PluginValueValidationResult ParseRange(PluginArgumentDefinition definition, string text)
    {
        IReadOnlyList<int> values = ParseIdentifiers(text);
        if (values.Count != 2)
            return PluginValueValidationResult.Failure($"{definition.DisplayName} must contain exactly two IDs.", text);
        int first = Math.Min(values[0], values[1]);
        int last = Math.Max(values[0], values[1]);
        return PluginValueValidationResult.Success(PluginValue.FromText($"{first} to {last}"));
    }

    private static PluginValueValidationResult ValidateBounds(
        PluginArgumentDefinition definition,
        PluginValueValidationResult result)
    {
        if (definition.Minimum is null && definition.Maximum is null) return result;
        if (definition.Kind is PluginArgumentKind.DatabaseIdList or PluginArgumentKind.DatabaseIdRange)
        {
            foreach (int id in ParseIdentifiers(result.Value.Text))
            {
                if (definition.Minimum is not null && id < definition.Minimum.Value)
                    return PluginValueValidationResult.Failure($"{definition.DisplayName} must be at least {definition.Minimum.Value}.", result.Value.Text);
                if (definition.Maximum is not null && id > definition.Maximum.Value)
                    return PluginValueValidationResult.Failure($"{definition.DisplayName} must be at most {definition.Maximum.Value}.", result.Value.Text);
            }
            return result;
        }
        if (decimal.TryParse(result.Value.Text.TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
        {
            if (definition.Minimum is not null && number < definition.Minimum.Value)
                return PluginValueValidationResult.Failure($"{definition.DisplayName} must be at least {definition.Minimum.Value}.", result.Value.Text);
            if (definition.Maximum is not null && number > definition.Maximum.Value)
                return PluginValueValidationResult.Failure($"{definition.DisplayName} must be at most {definition.Maximum.Value}.", result.Value.Text);
        }
        return result;
    }

    private static string NormalizeCoordinate(string value)
    {
        string[] parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[0] + ", " + parts[1] : value.Trim();
    }
}

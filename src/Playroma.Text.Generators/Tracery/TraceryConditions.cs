using System.Globalization;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Tracery;

internal static class TraceryConditionEvaluator
{
    public static bool Evaluate(string? expression, GenerationContext context, IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, SemanticFeatureValue>? features = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        foreach (var orTerm in expression.Split("||", StringSplitOptions.TrimEntries))
        {
            var all = true;
            foreach (var andTerm in orTerm.Split("&&", StringSplitOptions.TrimEntries))
            {
                if (!EvaluateTerm(andTerm, context, parameters, features)) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    private static bool EvaluateTerm(string term, GenerationContext context, IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, SemanticFeatureValue>? features)
    {
        term = term.Trim();
        if (term.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            return context.EffectiveTags.Contains(term[4..].Trim());
        if (term.StartsWith("!tag:", StringComparison.OrdinalIgnoreCase))
            return !context.EffectiveTags.Contains(term[5..].Trim());

        foreach (var op in new[] { "!=", ">=", "<=", "==", "=", ">", "<" })
        {
            var idx = term.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0)
            {
                var key = term[..idx].Trim();
                var rhs = Unquote(term[(idx + op.Length)..].Trim());
                var value = Lookup(key, context, parameters, features);
                return Compare(value, rhs, op);
            }
        }

        if (term.StartsWith('!')) return !Truthy(Lookup(term[1..].Trim(), context, parameters, features));
        return Truthy(Lookup(term, context, parameters, features));
    }

    private static object? Lookup(string key, GenerationContext context, IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, SemanticFeatureValue>? features)
    {
        if (parameters.TryGetValue(key, out var parameter)) return parameter;
        if (key.StartsWith("feature.", StringComparison.OrdinalIgnoreCase))
        {
            var name = key[8..];
            if (features is not null && features.TryGetValue(name, out var feature)) return feature.ToObject();
        }
        if (features is not null && features.TryGetValue(key, out var bareFeature)) return bareFeature.ToObject();
        if (context.Data.TryGetValue(key, out var value)) return value;
        return null;
    }

    private static bool Compare(object? value, string rhs, string op)
    {
        if (value is null) return op == "!=" && !string.IsNullOrEmpty(rhs);
        if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
            double.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return op switch { ">" => leftNumber > rightNumber, "<" => leftNumber < rightNumber, ">=" => leftNumber >= rightNumber, "<=" => leftNumber <= rightNumber, "!=" => leftNumber != rightNumber, _ => leftNumber == rightNumber };
        }
        var left = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var cmp = string.Compare(left, rhs, StringComparison.OrdinalIgnoreCase);
        return op switch { "!=" => cmp != 0, ">" => cmp > 0, "<" => cmp < 0, ">=" => cmp >= 0, "<=" => cmp <= 0, _ => cmp == 0 };
    }

    private static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0",
        int i => i != 0,
        long l => l != 0,
        double d => Math.Abs(d) > double.Epsilon,
        _ => true
    };

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'))) return value[1..^1];
        return value;
    }
}

internal static class TraceryParameterBinder
{
    public static (IReadOnlyDictionary<string, object?> Values, IReadOnlyList<GenerationDiagnostic> Diagnostics) Bind(
        TraceryGrammarDocument grammar,
        GenerationContext context,
        JsonElement? supplied,
        string? stageId)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<GenerationDiagnostic>();
        foreach (var pair in grammar.EffectiveParameters)
        {
            object? raw = null;
            var has = false;
            if (supplied.HasValue && supplied.Value.ValueKind == JsonValueKind.Object && supplied.Value.TryGetProperty(pair.Key, out var fromStage)) { raw = JsonToObject(fromStage); has = true; }
            else if (context.Data.TryGetValue(pair.Key, out var fromContext)) { raw = fromContext; has = true; }
            else if (pair.Value.DefaultValue is { } def) { raw = JsonToObject(def); has = true; }

            if (!has)
            {
                if (pair.Value.Required) diagnostics.Add(Error($"Required Tracery parameter '{pair.Key}' was not supplied.", stageId));
                continue;
            }
            if (!TryCoerce(raw, pair.Value, out var coerced))
                diagnostics.Add(Error($"Tracery parameter '{pair.Key}' is not valid for type {pair.Value.Type}.", stageId));
            else values[pair.Key] = coerced;
        }
        // Extra supplied parameters are allowed and become string/number/bool context values.
        if (supplied.HasValue && supplied.Value.ValueKind == JsonValueKind.Object)
            foreach (var prop in supplied.Value.EnumerateObject()) if (!values.ContainsKey(prop.Name)) values[prop.Name] = JsonToObject(prop.Value);
        return (values, diagnostics);
    }

    private static bool TryCoerce(object? raw, TraceryParameterDefinition definition, out object? value)
    {
        value = raw;
        try
        {
            switch (definition.Type)
            {
                case RecipeParameterType.String: value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty; return true;
                case RecipeParameterType.Integer: value = Convert.ToInt32(raw, CultureInfo.InvariantCulture); return true;
                case RecipeParameterType.Number: value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return true;
                case RecipeParameterType.Boolean:
                    if (raw is bool b) { value = b; return true; }
                    if (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed)) { value = parsed; return true; }
                    return false;
                case RecipeParameterType.Choice:
                    var choice = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (definition.Choices?.Contains(choice, StringComparer.OrdinalIgnoreCase) != true) return false;
                    value = choice; return true;
                case RecipeParameterType.StringList:
                    if (raw is IReadOnlyList<string> list) { value = list; return true; }
                    value = (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); return true;
                default: return false;
            }
        }
        catch { return false; }
    }

    private static object? JsonToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number when e.TryGetInt64(out var l) => l,
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(x => x.ToString()).ToArray(),
        JsonValueKind.Null => null,
        _ => e.ToString()
    };

    private static GenerationDiagnostic Error(string message, string? stageId) => new(
        GenerationDiagnosticCodes.TraceryParameterError,
        GenerationDiagnosticSeverity.Error,
        message,
        stageId);
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Core.Tracery;

public static class TraceryGrammarJson
{
    public static TraceryGrammarDocument Parse(string json, string? fallbackId = null, string? fallbackName = null)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return Parse(doc.RootElement, fallbackId, fallbackName);
    }

    public static TraceryGrammarDocument Parse(JsonElement root, string? fallbackId = null, string? fallbackName = null)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Tracery grammar JSON must be an object.");
        var extended = root.TryGetProperty("format", out var fmt) && string.Equals(fmt.GetString(), "playroma-tracery", StringComparison.OrdinalIgnoreCase);
        var hasWrapper = root.TryGetProperty("symbols", out var wrappedSymbols) && wrappedSymbols.ValueKind == JsonValueKind.Object;
        var symbolsElement = hasWrapper ? wrappedSymbols : root;
        var symbols = new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in symbolsElement.EnumerateObject())
        {
            if (!hasWrapper && extended && IsMetadataProperty(prop.Name)) continue;
            symbols[prop.Name] = ParseRules(prop.Value);
        }

        var id = GetString(root, "id") ?? fallbackId ?? "tracery.imported";
        var name = GetString(root, "name") ?? fallbackName ?? id;
        var version = GetInt(root, "version", 1);
        var origin = GetString(root, "origin") ?? "origin";
        var description = GetString(root, "description");
        var tags = root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
            ? tagsEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameters = ParseParameters(root);
        return new TraceryGrammarDocument(id, version, name, symbols, origin, parameters, tags, description,
            extended ? TraceryCompatibilityMode.PlayromaExtended : TraceryCompatibilityMode.Classic);
    }

    public static string SerializeClassic(TraceryGrammarDocument grammar, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            foreach (var symbol in grammar.Symbols.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(symbol.Key);
                writer.WriteStartArray();
                foreach (var rule in symbol.Value) writer.WriteStringValue(rule.Text);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string SerializePlayroma(TraceryGrammarDocument grammar, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "playroma-tracery");
            writer.WriteString("id", grammar.Id);
            writer.WriteNumber("version", grammar.Version);
            writer.WriteString("name", grammar.Name);
            writer.WriteString("origin", grammar.Origin);
            if (!string.IsNullOrWhiteSpace(grammar.Description)) writer.WriteString("description", grammar.Description);
            if (grammar.EffectiveTags.Count > 0)
            {
                writer.WritePropertyName("tags"); writer.WriteStartArray(); foreach (var tag in grammar.EffectiveTags.Order()) writer.WriteStringValue(tag); writer.WriteEndArray();
            }
            if (grammar.EffectiveParameters.Count > 0)
            {
                writer.WritePropertyName("parameters"); writer.WriteStartObject();
                foreach (var p in grammar.EffectiveParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WritePropertyName(p.Key); writer.WriteStartObject();
                    writer.WriteString("type", JsonNamingPolicy.CamelCase.ConvertName(p.Value.Type.ToString()));
                    if (p.Value.Required) writer.WriteBoolean("required", true);
                    if (!string.IsNullOrWhiteSpace(p.Value.Description)) writer.WriteString("description", p.Value.Description);
                    if (p.Value.DefaultValue is { } d) { writer.WritePropertyName("defaultValue"); d.WriteTo(writer); }
                    if (p.Value.Choices is { Count: > 0 }) { writer.WritePropertyName("choices"); writer.WriteStartArray(); foreach (var c in p.Value.Choices) writer.WriteStringValue(c); writer.WriteEndArray(); }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WritePropertyName("symbols"); writer.WriteStartObject();
            foreach (var symbol in grammar.Symbols.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(symbol.Key); writer.WriteStartArray();
                foreach (var rule in symbol.Value)
                {
                    if (rule.Weight == 1 && string.IsNullOrWhiteSpace(rule.Condition) && rule.EffectiveMetadata.Count == 0 && rule.EffectiveFeatures.Count == 0) writer.WriteStringValue(rule.Text);
                    else
                    {
                        writer.WriteStartObject(); writer.WriteString("text", rule.Text);
                        if (rule.Weight != 1) writer.WriteNumber("weight", rule.Weight);
                        if (!string.IsNullOrWhiteSpace(rule.Condition)) writer.WriteString("condition", rule.Condition);
                        if (rule.EffectiveMetadata.Count > 0) { writer.WritePropertyName("metadata"); writer.WriteStartObject(); foreach (var m in rule.EffectiveMetadata) writer.WriteString(m.Key, m.Value); writer.WriteEndObject(); }
                        if (rule.EffectiveFeatures.Count > 0)
                        {
                            writer.WritePropertyName("features"); writer.WriteStartObject();
                            foreach (var f in rule.EffectiveFeatures.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                            {
                                writer.WritePropertyName(f.Key); WriteFeatureValue(writer, f.Value);
                            }
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IReadOnlyList<TraceryRuleDefinition> ParseRules(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return [new TraceryRuleDefinition(e.GetString() ?? string.Empty)];
        if (e.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Tracery symbol values must be strings or arrays.");
        var rules = new List<TraceryRuleDefinition>();
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) rules.Add(new TraceryRuleDefinition(item.GetString() ?? string.Empty));
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var text = GetString(item, "text") ?? throw new InvalidDataException("Extended Tracery rules require a 'text' property.");
                var weight = item.TryGetProperty("weight", out var w) && w.TryGetDouble(out var d) ? d : 1;
                var condition = GetString(item, "condition");
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                    foreach (var m in meta.EnumerateObject()) metadata[m.Name] = m.Value.ValueKind == JsonValueKind.String ? m.Value.GetString() ?? string.Empty : m.Value.ToString();
                var features = new Dictionary<string, SemanticFeatureValue>(StringComparer.OrdinalIgnoreCase);
                if (item.TryGetProperty("features", out var featureElement) && featureElement.ValueKind == JsonValueKind.Object)
                    foreach (var f in featureElement.EnumerateObject()) features[f.Name] = SemanticFeatureValue.FromJsonElement(f.Value);
                rules.Add(new TraceryRuleDefinition(text, weight, metadata.Count == 0 ? null : metadata, condition, features.Count == 0 ? null : features));
            }
            else throw new InvalidDataException("Tracery rule arrays may contain strings or Playroma rule objects.");
        }
        return rules;
    }

    private static IReadOnlyDictionary<string, TraceryParameterDefinition> ParseParameters(JsonElement root)
    {
        var result = new Dictionary<string, TraceryParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in parameters.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var typeText = GetString(prop.Value, "type") ?? "string";
            if (!Enum.TryParse<RecipeParameterType>(typeText, true, out var type)) type = RecipeParameterType.String;
            var required = prop.Value.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True;
            JsonElement? def = prop.Value.TryGetProperty("defaultValue", out var defaultEl) ? defaultEl.Clone() : null;
            var description = GetString(prop.Value, "description");
            var choices = prop.Value.TryGetProperty("choices", out var choicesEl) && choicesEl.ValueKind == JsonValueKind.Array
                ? choicesEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray() : null;
            result[prop.Name] = new TraceryParameterDefinition(prop.Name, type, required, def, description, choices);
        }
        return result;
    }


    private static void WriteFeatureValue(Utf8JsonWriter writer, SemanticFeatureValue value)
    {
        switch (value.Type)
        {
            case SemanticFeatureType.Integer:
                if (long.TryParse(value.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer)) writer.WriteNumberValue(integer);
                else writer.WriteStringValue(value.DisplayText);
                break;
            case SemanticFeatureType.Number:
                if (double.TryParse(value.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) writer.WriteNumberValue(number);
                else writer.WriteStringValue(value.DisplayText);
                break;
            case SemanticFeatureType.Boolean:
                if (bool.TryParse(value.Value, out var boolean)) writer.WriteBooleanValue(boolean);
                else writer.WriteStringValue(value.DisplayText);
                break;
            case SemanticFeatureType.StringList:
                writer.WriteStartArray(); foreach (var item in value.EffectiveValues) writer.WriteStringValue(item); writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.Value ?? string.Empty);
                break;
        }
    }

    private static bool IsMetadataProperty(string name) => name is "format" or "id" or "version" or "name" or "origin" or "description" or "tags" or "parameters";
    private static string? GetString(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static int GetInt(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : fallback;
}

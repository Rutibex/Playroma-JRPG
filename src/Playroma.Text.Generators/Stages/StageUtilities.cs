using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators.Stages;

internal static class StageUtilities
{
    public static GeneratedTextBundle Write(GenerationStageContext context, string text, string defaultField, string semanticRole)
    {
        var field = StageSettingsReader.FieldName(context.Stage, defaultField);
        var primary = StageSettingsReader.IsPrimary(context.Stage, true);
        return context.WorkingBundle.WithField(field, text, semanticRole, primary);
    }

    public static Dictionary<string, object?> BuildTemplateValues(GenerationStageContext context)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in context.Context.Data) values[kv.Key] = kv.Value;
        foreach (var field in context.WorkingBundle.Fields) values[field.Name] = field.Text;
        foreach (var tag in context.WorkingBundle.Tags) values[tag.Key] = tag.Value;
        values["primary"] = context.WorkingBundle.PrimaryText;
        if (StageSettingsReader.TryGet(context.Stage, "variables", out var variables) && variables.ValueKind == JsonValueKind.Object)
            foreach (var property in variables.EnumerateObject()) values[property.Name] = JsonToValue(property.Value);
        return values;
    }

    public static object? JsonToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var i) => i,
        JsonValueKind.Number when value.TryGetDouble(out var d) => d,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.ToString()
    };
}

using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators;

public static class StageSettingsReader
{
    public static bool TryGet(TextRecipeStage stage, string name, out JsonElement value)
    {
        value = default;
        return stage.Settings is not null && stage.Settings.TryGetValue(name, out value);
    }

    public static string GetString(TextRecipeStage stage, string name, string defaultValue = "")
        => TryGet(stage, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? defaultValue : defaultValue;

    public static bool GetBool(TextRecipeStage stage, string name, bool defaultValue = false)
        => TryGet(stage, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : defaultValue;

    public static int GetInt(TextRecipeStage stage, string name, int defaultValue = 0)
        => TryGet(stage, name, out var value) && value.TryGetInt32(out var result) ? result : defaultValue;

    public static double GetDouble(TextRecipeStage stage, string name, double defaultValue = 0)
        => TryGet(stage, name, out var value) && value.TryGetDouble(out var result) ? result : defaultValue;

    public static IReadOnlyList<string> GetStringArray(TextRecipeStage stage, string name)
    {
        if (!TryGet(stage, name, out var value) || value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray();
    }

    public static string FieldName(TextRecipeStage stage, string fallback = "text") => GetString(stage, "field", fallback);
    public static bool IsPrimary(TextRecipeStage stage, bool fallback = true) => GetBool(stage, "isPrimary", fallback);

    public static GenerationStageResult Invalid(GenerationStageContext context, string message)
        => GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(
            GenerationDiagnosticCodes.InvalidStageSettings,
            GenerationDiagnosticSeverity.Error,
            message,
            context.Stage.Id));
}

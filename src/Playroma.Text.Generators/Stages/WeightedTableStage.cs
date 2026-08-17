using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Selection;

namespace Playroma.Text.Generators.Stages;

public sealed class WeightedTableStage : ITextGenerationStage
{
    public const string StageKind = "phase1.weighted-table";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = ReadEntries(context.Stage);
        if (entries.Count == 0) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "weighted-table requires 'entries' containing text and weight values."));
        if (entries.All(x => x.Weight <= 0)) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "weighted-table requires at least one positive weight."));

        var count = Math.Max(1, StageSettingsReader.GetInt(context.Stage, "count", 1));
        var unique = StageSettingsReader.GetBool(context.Stage, "unique", false);
        var separator = StageSettingsReader.GetString(context.Stage, "separator", " ");
        IReadOnlyList<WeightedText> selected;
        try
        {
            if (unique) selected = WeightedSelector.SelectWithoutReplacement(entries, x => x.Weight, count, context.Random);
            else
            {
                var list = new List<WeightedText>(count);
                for (var i = 0; i < count; i++) list.Add(WeightedSelector.Select(entries, x => x.Weight, context.Random));
                selected = list;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, ex.Message));
        }

        foreach (var item in selected)
        {
            context.Budget.ConsumeCandidateAttempt();
            context.Trace.Add(context.Stage.Id, "weighted-table.pick", output: item.Text, metadata: new Dictionary<string, string> { ["weight"] = item.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture), ["id"] = item.Id ?? string.Empty });
        }
        var text = string.Join(separator, selected.Select(x => x.Text));
        return ValueTask.FromResult(GenerationStageResult.Ok(StageUtilities.Write(context, text, "selection", "weighted-selection")));
    }

    internal static IReadOnlyList<WeightedText> ReadEntries(TextRecipeStage stage)
    {
        if (!StageSettingsReader.TryGet(stage, "entries", out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<WeightedText>();
        var result = new List<WeightedText>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) { result.Add(new WeightedText(item.GetString() ?? string.Empty)); continue; }
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String) continue;
            var weight = item.TryGetProperty("weight", out var weightElement) && weightElement.TryGetDouble(out var parsed) ? parsed : 1.0;
            var id = item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
            result.Add(new WeightedText(textElement.GetString() ?? string.Empty, weight, id));
        }
        return result;
    }
}

using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Selection;
using Playroma.Text.Generators.Transforms;

namespace Playroma.Text.Generators.Stages;

public sealed class AffixComposeStage : ITextGenerationStage
{
    public const string StageKind = "phase1.affix-compose";
    private readonly TextTransformRegistry _transforms;
    public string Kind => StageKind;
    public AffixComposeStage(TextTransformRegistry? transforms = null) => _transforms = transforms ?? TextTransformRegistry.CreateDefault();

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = ReadPool(context.Stage, "roots");
        if (roots.Count == 0) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "affix-compose requires a non-empty 'roots' pool."));
        var prefixes = ReadPool(context.Stage, "prefixes");
        var suffixes = ReadPool(context.Stage, "suffixes");
        var prefixChance = Math.Clamp(StageSettingsReader.GetDouble(context.Stage, "prefixProbability", prefixes.Count > 0 ? 0.5 : 0), 0, 1);
        var suffixChance = Math.Clamp(StageSettingsReader.GetDouble(context.Stage, "suffixProbability", suffixes.Count > 0 ? 0.5 : 0), 0, 1);
        var separator = StageSettingsReader.GetString(context.Stage, "separator", string.Empty);
        var parts = new List<string>(3);
        if (prefixes.Count > 0 && context.Random.NextBool(prefixChance)) parts.Add(Pick(prefixes, context));
        parts.Add(Pick(roots, context));
        if (suffixes.Count > 0 && context.Random.NextBool(suffixChance)) parts.Add(Pick(suffixes, context));
        var text = string.Join(separator, parts);

        foreach (var transformName in StageSettingsReader.GetStringArray(context.Stage, "transforms"))
        {
            var invocation = TextTransformInvocation.Parse(transformName);
            if (!_transforms.TryApply(invocation.Name, text, invocation.Arguments, out var transformed))
                return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownTransform, GenerationDiagnosticSeverity.Error, $"Unknown transform '{invocation.Name}'.", context.Stage.Id)));
            text = transformed;
        }
        context.Trace.Add(context.Stage.Id, "affix-compose", output: text);
        return ValueTask.FromResult(GenerationStageResult.Ok(StageUtilities.Write(context, text, "name", "constructed-name")));
    }

    private static string Pick(IReadOnlyList<WeightedText> entries, GenerationStageContext context)
    {
        context.Budget.ConsumeCandidateAttempt();
        return WeightedSelector.Select(entries, x => x.Weight, context.Random).Text;
    }

    private static IReadOnlyList<WeightedText> ReadPool(TextRecipeStage stage, string name)
    {
        if (!StageSettingsReader.TryGet(stage, name, out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<WeightedText>();
        var result = new List<WeightedText>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) result.Add(new WeightedText(item.GetString() ?? string.Empty));
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                var weight = item.TryGetProperty("weight", out var weightElement) && weightElement.TryGetDouble(out var w) ? w : 1;
                result.Add(new WeightedText(text.GetString() ?? string.Empty, weight));
            }
        }
        return result;
    }
}

using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Stages;

public sealed class FeatureUnifyStage : ITextGenerationStage
{
    public const string StageKind = "phase6.feature-unify";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StageSettingsReader.TryGet(context.Stage, "requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Object)
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidStageSettings, GenerationDiagnosticSeverity.Error, "Feature unification requires a 'requirements' object.", context.Stage.Id)));

        var features = context.WorkingBundle.Features.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<SemanticUnificationConflict>();
        var bindMissing = StageSettingsReader.GetBool(context.Stage, "bindMissing", true);

        foreach (var property in requirements.EnumerateObject())
        {
            SemanticFeatureValue required;
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } text && text.StartsWith('$')) required = SemanticValueResolver.ResolveOrLiteral(text, context);
            else required = SemanticFeatureValue.FromJsonElement(property.Value);

            if (!features.TryGetValue(property.Name, out var existing))
            {
                if (bindMissing) features[property.Name] = required;
                continue;
            }
            if (SemanticFeatureUnifier.TryUnify(existing, required, out var unified)) features[property.Name] = unified;
            else conflicts.Add(new SemanticUnificationConflict(property.Name, existing, required));
        }

        var bundle = context.WorkingBundle with { Features = new System.Collections.ObjectModel.ReadOnlyDictionary<string, SemanticFeatureValue>(features) };
        context.Trace.Add(context.Stage.Id, conflicts.Count == 0 ? "semantic.unify.pass" : "semantic.unify.conflict", output: bundle.PrimaryText,
            metadata: new Dictionary<string, string> { ["conflicts"] = conflicts.Count.ToString(), ["features"] = features.Count.ToString() });
        if (conflicts.Count == 0) return ValueTask.FromResult(GenerationStageResult.Ok(bundle));

        var severity = StageSettingsReader.GetString(context.Stage, "mode", "error").Equals("warn", StringComparison.OrdinalIgnoreCase)
            ? GenerationDiagnosticSeverity.Warning : GenerationDiagnosticSeverity.Error;
        var detail = string.Join("; ", conflicts.Select(x => $"{x.Feature}: '{x.Existing.DisplayText}' vs '{x.Required.DisplayText}'"));
        var diagnostic = new GenerationDiagnostic(GenerationDiagnosticCodes.SemanticUnificationConflict, severity, $"{conflicts.Count} semantic feature conflict(s) could not be unified.", context.Stage.Id, detail);
        return ValueTask.FromResult(new GenerationStageResult(severity != GenerationDiagnosticSeverity.Error, bundle, [diagnostic]));
    }
}

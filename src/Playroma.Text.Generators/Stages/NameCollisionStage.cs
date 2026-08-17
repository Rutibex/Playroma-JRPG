using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Generators.Stages;

public sealed class NameCollisionStage : ITextGenerationStage
{
    public const string StageKind = "phase2.name-collision";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fieldName = StageSettingsReader.GetString(context.Stage, "sourceField", string.Empty);
        var text = string.IsNullOrWhiteSpace(fieldName)
            ? context.WorkingBundle.PrimaryText
            : context.WorkingBundle.Fields.LastOrDefault(x => string.Equals(x.Name, fieldName, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "name-collision requires a generated source value."));
        var existing = NameGeneratorStage.ReadExisting(context);
        var threshold = StageSettingsReader.GetDouble(context.Stage, "maxSimilarity", .88);
        var mode = StageSettingsReader.GetString(context.Stage, "mode", "error");
        foreach (var used in existing)
        {
            var similarity = NameSimilarity.Composite(text, used);
            if (similarity <= threshold) continue;
            var severity = mode.Equals("warn", StringComparison.OrdinalIgnoreCase) ? GenerationDiagnosticSeverity.Warning : GenerationDiagnosticSeverity.Error;
            var diagnostic = new GenerationDiagnostic(GenerationDiagnosticCodes.NameCollision, severity,
                $"Generated name '{text}' is too similar to existing name '{used}' ({similarity:0.000}).", context.Stage.Id);
            return ValueTask.FromResult(severity == GenerationDiagnosticSeverity.Error
                ? GenerationStageResult.Fail(context.WorkingBundle, diagnostic)
                : new GenerationStageResult(true, context.WorkingBundle, new[] { diagnostic }));
        }
        return ValueTask.FromResult(GenerationStageResult.Ok(context.WorkingBundle));
    }
}

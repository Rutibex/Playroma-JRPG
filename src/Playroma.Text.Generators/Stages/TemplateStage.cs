using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Templates;
using Playroma.Text.Generators.Transforms;

namespace Playroma.Text.Generators.Stages;

public sealed class TemplateStage : ITextGenerationStage
{
    public const string StageKind = "phase1.template";
    private readonly TemplateRenderer _renderer;
    public string Kind => StageKind;

    public TemplateStage(TextTransformRegistry? transforms = null) => _renderer = new TemplateRenderer(transforms);

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var template = StageSettingsReader.GetString(context.Stage, "template");
        if (template.Length == 0) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "template stage requires a non-empty 'template'."));
        var behaviorText = StageSettingsReader.GetString(context.Stage, "missing", "error");
        var behavior = behaviorText.ToLowerInvariant() switch { "empty" => MissingTemplateValueBehavior.Empty, "keep" or "keeptoken" => MissingTemplateValueBehavior.KeepToken, _ => MissingTemplateValueBehavior.Error };
        var optionalProbability = Math.Clamp(StageSettingsReader.GetDouble(context.Stage, "optionalProbability", 0.5), 0, 1);
        var result = _renderer.Render(template, StageUtilities.BuildTemplateValues(context), context.Random, new TemplateRenderOptions(optionalProbability, behavior));
        var diagnostics = new List<GenerationDiagnostic>();
        if (result.MissingKeys.Count > 0)
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.MissingTemplateValue, behavior == MissingTemplateValueBehavior.Error ? GenerationDiagnosticSeverity.Error : GenerationDiagnosticSeverity.Warning, "Template references missing values: " + string.Join(", ", result.MissingKeys), context.Stage.Id));
        if (result.UnknownTransforms.Count > 0)
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownTransform, GenerationDiagnosticSeverity.Error, "Unknown template transforms: " + string.Join(", ", result.UnknownTransforms), context.Stage.Id));
        context.Trace.Add(context.Stage.Id, "template.render", input: template, output: result.Text);
        var bundle = StageUtilities.Write(context, result.Text, "text", "template");
        var success = diagnostics.All(x => x.Severity != GenerationDiagnosticSeverity.Error);
        return ValueTask.FromResult(new GenerationStageResult(success, bundle, diagnostics));
    }
}

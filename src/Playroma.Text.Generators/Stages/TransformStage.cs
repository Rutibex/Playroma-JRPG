using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Transforms;

namespace Playroma.Text.Generators.Stages;

public sealed class TransformStage : ITextGenerationStage
{
    public const string StageKind = "phase1.transform";
    private readonly TextTransformRegistry _transforms;
    public string Kind => StageKind;
    public TransformStage(TextTransformRegistry? transforms = null) => _transforms = transforms ?? TextTransformRegistry.CreateDefault();

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var field = StageSettingsReader.GetString(context.Stage, "target", "primary");
        var text = field.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? context.WorkingBundle.PrimaryText
            : context.WorkingBundle.Fields.FirstOrDefault(x => string.Equals(x.Name, field, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
        foreach (var expression in StageSettingsReader.GetStringArray(context.Stage, "transforms"))
        {
            var invocation = TextTransformInvocation.Parse(expression);
            if (!_transforms.TryApply(invocation.Name, text, invocation.Arguments, out var transformed))
                return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownTransform, GenerationDiagnosticSeverity.Error, $"Unknown transform '{invocation.Name}'.", context.Stage.Id)));
            text = transformed;
        }
        context.Trace.Add(context.Stage.Id, "transform", output: text, metadata: new Dictionary<string, string> { ["target"] = field });
        GeneratedTextBundle bundle;
        if (field.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            var primaryField = context.WorkingBundle.Fields.FirstOrDefault(x => x.IsPrimary);
            bundle = primaryField is null ? context.WorkingBundle with { PrimaryText = text } : context.WorkingBundle.WithField(primaryField.Name, text, primaryField.SemanticRole, true);
        }
        else
        {
            var old = context.WorkingBundle.Fields.FirstOrDefault(x => string.Equals(x.Name, field, StringComparison.OrdinalIgnoreCase));
            bundle = context.WorkingBundle.WithField(field, text, old?.SemanticRole, old?.IsPrimary ?? false);
        }
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
    }
}

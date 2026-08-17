using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Stages;

public sealed class SemanticConstraintStage : ITextGenerationStage
{
    public const string StageKind = "phase6.semantic-constraint";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expressions = StageSettingsReader.GetStringArray(context.Stage, "predicates").ToList();
        var single = StageSettingsReader.GetString(context.Stage, "predicate");
        if (!string.IsNullOrWhiteSpace(single)) expressions.Insert(0, single);
        if (expressions.Count == 0)
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidStageSettings, GenerationDiagnosticSeverity.Error, "Semantic constraint requires 'predicate' or 'predicates'.", context.Stage.Id)));

        var failures = new List<string>();
        foreach (var expression in expressions)
        {
            var result = SemanticPredicateEvaluator.Evaluate(expression, context.Context, context.WorkingBundle);
            if (!result.Success)
                return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidSemanticPredicate, GenerationDiagnosticSeverity.Error, $"Invalid semantic predicate '{expression}'.", context.Stage.Id, result.Error)));
            if (!result.Value) failures.Add(expression);
        }
        if (failures.Count == 0)
        {
            context.Trace.Add(context.Stage.Id, "semantic.constraint.pass", output: context.WorkingBundle.PrimaryText);
            return ValueTask.FromResult(GenerationStageResult.Ok(context.WorkingBundle));
        }

        var severity = StageSettingsReader.GetString(context.Stage, "mode", "error").Equals("warn", StringComparison.OrdinalIgnoreCase)
            ? GenerationDiagnosticSeverity.Warning : GenerationDiagnosticSeverity.Error;
        var diagnostic = new GenerationDiagnostic(GenerationDiagnosticCodes.SemanticConstraintFailed, severity,
            $"Semantic constraint failed: {string.Join("; ", failures)}", context.Stage.Id);
        context.Trace.Add(context.Stage.Id, "semantic.constraint.fail", output: context.WorkingBundle.PrimaryText, metadata: new Dictionary<string, string> { ["failures"] = failures.Count.ToString() });
        return ValueTask.FromResult(new GenerationStageResult(severity != GenerationDiagnosticSeverity.Error, context.WorkingBundle, [diagnostic]));
    }
}

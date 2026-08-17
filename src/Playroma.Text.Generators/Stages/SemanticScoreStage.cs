using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Candidates;

namespace Playroma.Text.Generators.Stages;

public sealed class SemanticScoreStage : ITextGenerationStage
{
    public const string StageKind = "phase6.score";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StageSettingsReader.TryGet(context.Stage, "scorers", out var element) || element.ValueKind != JsonValueKind.Array)
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidStageSettings,
                GenerationDiagnosticSeverity.Error, "Semantic score stage requires a 'scorers' array.", context.Stage.Id)));
        try
        {
            var scorers = element.Deserialize<CandidateScorerDefinition[]>(RecipeJson.Options) ?? Array.Empty<CandidateScorerDefinition>();
            var components = CandidateScoring.ScoreBundle(context.WorkingBundle, context.Context, scorers);
            var scores = context.WorkingBundle.Scores.Concat(components.Select(x => new GenerationScore(x.Id, x.RawValue, x.Explanation))).ToArray();
            var bundle = context.WorkingBundle with { Scores = scores };
            context.Trace.Add(context.Stage.Id, "semantic.score", output: bundle.PrimaryText, metadata: components.ToDictionary(x => x.Id, x => x.RawValue.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase));
            return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.CandidateScoringError,
                GenerationDiagnosticSeverity.Error, "Could not read semantic scorers.", context.Stage.Id, ex.Message)));
        }
    }
}

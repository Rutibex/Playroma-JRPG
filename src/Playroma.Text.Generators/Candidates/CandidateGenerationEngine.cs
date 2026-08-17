using System.Diagnostics;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Candidates;

/// <summary>
/// Executes a recipe repeatedly as hidden candidates, applies semantic filters/scorers,
/// then selects a deterministic high-quality/diverse result. The inner TextGenerationEngine
/// remains intentionally unaware of candidate pipelines, preventing recursive orchestration.
/// </summary>
public sealed class CandidateGenerationEngine
{
    private readonly TextGenerationEngine _engine;

    public CandidateGenerationEngine(TextGenerationEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public async ValueTask<GeneratedTextBundle> GenerateBestAsync(
        TextRecipe recipe,
        GenerationContext? context = null,
        ulong? seed = null,
        GenerationBudgetOptions? budgetOptions = null,
        IEnumerable<string>? referenceTexts = null,
        CancellationToken cancellationToken = default)
    {
        var ranked = await GenerateRankedAsync(recipe, 1, context, seed, budgetOptions, referenceTexts, cancellationToken).ConfigureAwait(false);
        return ranked.Count > 0 ? ranked[0] : new GeneratedTextBundle
        {
            Kind = recipe.Kind,
            Diagnostics = [new GenerationDiagnostic(GenerationDiagnosticCodes.CandidatePipelineExhausted, GenerationDiagnosticSeverity.Error, "Candidate pipeline did not return a result.")]
        };
    }

    public async ValueTask<IReadOnlyList<GeneratedTextBundle>> GenerateRankedAsync(
        TextRecipe recipe,
        int take,
        GenerationContext? context = null,
        ulong? seed = null,
        GenerationBudgetOptions? budgetOptions = null,
        IEnumerable<string>? referenceTexts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        context ??= new GenerationContext();
        var pipeline = recipe.CandidatePipeline;
        var actualSeed = seed ?? SeedFactory.CreateSeed();
        if (pipeline?.Search is { Mode: not CandidateSearchMode.RandomPool })
        {
            budgetOptions ??= new GenerationBudgetOptions();
            return await new AdvancedCandidateSearchEngine(_engine).GenerateRankedAsync(recipe, take, context, actualSeed, budgetOptions, referenceTexts, cancellationToken).ConfigureAwait(false);
        }
        if (pipeline is null || (pipeline.EffectiveCandidateCount == 1 && pipeline.EffectiveFilters.Count == 0 && pipeline.EffectiveScorers.Count == 0 && pipeline.EffectiveDiversityWeight <= 0 && pipeline.EffectiveMinimumDiversity <= 0))
            return [await _engine.GenerateAsync(recipe, context, actualSeed, budgetOptions, cancellationToken).ConfigureAwait(false)];

        take = Math.Clamp(take, 1, 100);
        budgetOptions ??= new GenerationBudgetOptions();
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<RankedCandidate>();
        GeneratedTextBundle? lastGenerated = null;
        var rejected = 0;
        var invalidPredicateErrors = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < pipeline.EffectiveCandidateCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budgetOptions.EffectiveMaxElapsed - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var childBudget = budgetOptions with { MaxElapsed = remaining };
            var candidateSeed = SeedFactory.Derive(actualSeed, "candidate:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var bundle = await _engine.GenerateAsync(recipe, context, candidateSeed, childBudget, cancellationToken).ConfigureAwait(false);
            lastGenerated = bundle;
            if (pipeline.RejectGenerationErrors && bundle.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error)) { rejected++; continue; }

            var filterPassed = true;
            foreach (var filter in pipeline.EffectiveFilters)
            {
                var evaluation = SemanticPredicateEvaluator.Evaluate(filter.Predicate, context, bundle);
                if (!evaluation.Success)
                {
                    invalidPredicateErrors.Add($"{filter.Id}: {evaluation.Error}");
                    if (filter.Required) filterPassed = false;
                    continue;
                }
                if (!evaluation.Value && filter.Required) { filterPassed = false; break; }
            }
            if (!filterPassed) { rejected++; continue; }

            var components = CandidateScoring.ScoreBundle(bundle, context, pipeline.EffectiveScorers);
            var baseScore = components.Sum(x => x.WeightedValue);
            var diversity = CandidateScoring.TextDiversity(bundle.PrimaryText, referenceTexts);
            if (diversity + 1e-12 < pipeline.EffectiveMinimumDiversity) { rejected++; continue; }
            candidates.Add(new RankedCandidate(bundle, candidateSeed, components, baseScore, diversity));
        }

        if (candidates.Count == 0)
        {
            var diagnostics = (lastGenerated?.Diagnostics ?? Array.Empty<GenerationDiagnostic>()).ToList();
            if (invalidPredicateErrors.Count > 0)
                diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidSemanticPredicate, GenerationDiagnosticSeverity.Error,
                    "Candidate pipeline contains invalid predicate expressions.", Detail: string.Join(Environment.NewLine, invalidPredicateErrors)));
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.CandidatePipelineExhausted, GenerationDiagnosticSeverity.Error,
                $"All hidden candidates were rejected or the time budget expired. Rejected: {rejected}; requested: {pipeline.EffectiveCandidateCount}."));
            return [(lastGenerated ?? new GeneratedTextBundle { Kind = recipe.Kind }) with { Diagnostics = diagnostics }];
        }

        var selected = new List<GeneratedTextBundle>();
        var remainingCandidates = candidates.ToList();
        var references = (referenceTexts ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        while (selected.Count < take && remainingCandidates.Count > 0)
        {
            RankedCandidate? best = null;
            double bestTotal = double.NegativeInfinity;
            double bestDiversity = 0;
            foreach (var candidate in remainingCandidates)
            {
                var diversity = CandidateScoring.TextDiversity(candidate.Bundle.PrimaryText, references);
                if (diversity + 1e-12 < pipeline.EffectiveMinimumDiversity) continue;
                var total = candidate.BaseScore + pipeline.EffectiveDiversityWeight * diversity;
                if (best is null || total > bestTotal + 1e-12 || (Math.Abs(total - bestTotal) <= 1e-12 && candidate.Seed < best.Seed))
                {
                    best = candidate; bestTotal = total; bestDiversity = diversity;
                }
            }
            if (best is null) break;
            remainingCandidates.Remove(best);
            var scores = best.Bundle.Scores.Concat(best.Components.Select(x => new GenerationScore("candidate." + x.Id, x.RawValue, x.Explanation)))
                .Concat([
                    new GenerationScore("candidate.base", best.BaseScore, "Weighted sum of candidate scorers."),
                    new GenerationScore("candidate.diversity", bestDiversity, "Lexical diversity from locked/selected reference results."),
                    new GenerationScore("candidate.total", bestTotal, "Base score plus diversity contribution.")
                ]).ToArray();
            var tags = best.Bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            tags["candidatePipeline"] = "phase6";
            tags["candidatePool"] = pipeline.EffectiveCandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["candidateAccepted"] = candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["candidateSelectedSeed"] = best.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var diagnostics = best.Bundle.Diagnostics.ToList();
            if (invalidPredicateErrors.Count > 0)
                diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidSemanticPredicate, GenerationDiagnosticSeverity.Warning,
                    "One or more non-required candidate predicates were invalid.", Detail: string.Join(Environment.NewLine, invalidPredicateErrors)));
            var final = best.Bundle with { Scores = scores, Tags = tags, Diagnostics = diagnostics };
            selected.Add(final);
            references.Add(final.PrimaryText);
        }

        return selected;
    }

    private sealed record RankedCandidate(
        GeneratedTextBundle Bundle,
        ulong Seed,
        IReadOnlyList<CandidateScoreComponent> Components,
        double BaseScore,
        double InitialDiversity);
}

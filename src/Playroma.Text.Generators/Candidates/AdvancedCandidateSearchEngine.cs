using System.Diagnostics;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Candidates;

/// <summary>
/// Phase 14 seed-space optimizer. It never mutates recipe data or generated text directly;
/// instead it searches deterministic seed neighborhoods and evaluates results with the same
/// filters/scorers used by the Phase 6 candidate pipeline.
/// </summary>
public sealed class AdvancedCandidateSearchEngine
{
    private readonly TextGenerationEngine _engine;
    public AdvancedCandidateSearchEngine(TextGenerationEngine engine) => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public async ValueTask<IReadOnlyList<GeneratedTextBundle>> GenerateRankedAsync(
        TextRecipe recipe,
        int take,
        GenerationContext context,
        ulong rootSeed,
        GenerationBudgetOptions budgetOptions,
        IEnumerable<string>? referenceTexts,
        CancellationToken cancellationToken)
    {
        var pipeline = recipe.CandidatePipeline ?? new CandidatePipelineDefinition();
        var search = pipeline.EffectiveSearch;
        take = Math.Clamp(take, 1, 100);
        var stopwatch = Stopwatch.StartNew();
        var externalReferences = (referenceTexts ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var all = new Dictionary<ulong, SearchCandidate>();
        var invalidPredicates = new HashSet<string>(StringComparer.Ordinal);
        var maxEvaluations = Math.Min(512, Math.Max(pipeline.EffectiveCandidateCount, search.EffectivePopulationSize * search.EffectiveIterations));
        var evaluationCount = 0;

        async ValueTask<SearchCandidate?> EvaluateAsync(ulong seed, int generation)
        {
            if (all.TryGetValue(seed, out var cached)) return cached;
            if (evaluationCount >= maxEvaluations) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budgetOptions.EffectiveMaxElapsed - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) return null;
            evaluationCount++;
            var bundle = await _engine.GenerateAsync(recipe, context, seed, budgetOptions with { MaxElapsed = remaining }, cancellationToken).ConfigureAwait(false);
            if (pipeline.RejectGenerationErrors && bundle.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error)) return null;
            foreach (var filter in pipeline.EffectiveFilters)
            {
                var eval = SemanticPredicateEvaluator.Evaluate(filter.Predicate, context, bundle);
                if (!eval.Success)
                {
                    invalidPredicates.Add($"{filter.Id}: {eval.Error}");
                    if (filter.Required) return null;
                }
                else if (!eval.Value && filter.Required) return null;
            }
            var components = CandidateScoring.ScoreBundle(bundle, context, pipeline.EffectiveScorers);
            var baseScore = components.Sum(x => x.WeightedValue);
            var novelty = CandidateScoring.TextDiversity(bundle.PrimaryText, externalReferences.Concat(all.Values.Select(x => x.Bundle.PrimaryText)));
            var objective = baseScore + search.EffectiveNoveltyWeight * novelty;
            var candidate = new SearchCandidate(bundle, seed, generation, components, baseScore, novelty, objective);
            all[seed] = candidate;
            return candidate;
        }

        if (search.Mode == CandidateSearchMode.QualityDiversity)
        {
            for (var i = 0; i < maxEvaluations; i++)
                await EvaluateAsync(SeedFactory.Derive(rootSeed, $"qd:{i}"), 0).ConfigureAwait(false);
        }
        else
        {
            var population = Enumerable.Range(0, search.EffectivePopulationSize)
                .Select(i => SeedFactory.Derive(rootSeed, $"search:0:{i}"))
                .ToList();
            for (var generation = 0; generation < search.EffectiveIterations && evaluationCount < maxEvaluations; generation++)
            {
                var evaluated = new List<SearchCandidate>();
                foreach (var seed in population)
                {
                    var candidate = await EvaluateAsync(seed, generation).ConfigureAwait(false);
                    if (candidate is not null) evaluated.Add(candidate);
                }
                if (evaluated.Count == 0) break;
                var ranked = evaluated.OrderByDescending(x => x.Objective).ThenBy(x => x.Seed).ToArray();
                var parentCount = search.Mode == CandidateSearchMode.BeamSeedSearch ? search.EffectiveBeamWidth : search.EffectiveEliteCount;
                var parents = ranked.Take(Math.Min(parentCount, ranked.Length)).ToArray();
                var next = new List<ulong>(search.EffectivePopulationSize);
                for (var slot = 0; slot < search.EffectivePopulationSize; slot++)
                {
                    var immigrantCutoff = (int)Math.Round(search.EffectivePopulationSize * search.EffectiveRandomImmigrantRate);
                    if (slot < immigrantCutoff)
                        next.Add(SeedFactory.Derive(rootSeed, $"immigrant:{generation + 1}:{slot}"));
                    else
                    {
                        var parent = parents[(slot - immigrantCutoff) % parents.Length];
                        next.Add(SeedFactory.Derive(parent.Seed, $"mutation:{generation + 1}:{slot}"));
                    }
                }
                population = next;
            }
        }

        if (all.Count == 0)
        {
            var diagnostics = new List<GenerationDiagnostic>
            {
                new(GenerationDiagnosticCodes.AdvancedSearchFailed, GenerationDiagnosticSeverity.Error, "Advanced candidate search produced no valid candidates.")
            };
            if (invalidPredicates.Count > 0)
                diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidSemanticPredicate, GenerationDiagnosticSeverity.Error, "Advanced search encountered invalid candidate predicates.", Detail: string.Join(Environment.NewLine, invalidPredicates)));
            return [new GeneratedTextBundle { Kind = recipe.Kind, Diagnostics = diagnostics }];
        }

        var pool = search.Mode == CandidateSearchMode.QualityDiversity
            ? BuildQualityDiversityArchive(all.Values, search.EffectiveArchiveBins)
            : all.Values.OrderByDescending(x => x.Objective).ThenBy(x => x.Seed).ToList();

        var selected = new List<GeneratedTextBundle>();
        var references = externalReferences.ToList();
        foreach (var candidate in pool)
        {
            if (selected.Count >= take) break;
            var diversity = CandidateScoring.TextDiversity(candidate.Bundle.PrimaryText, references);
            if (diversity + 1e-12 < pipeline.EffectiveMinimumDiversity) continue;
            var total = candidate.BaseScore + pipeline.EffectiveDiversityWeight * diversity + search.EffectiveNoveltyWeight * candidate.Novelty;
            var scores = candidate.Bundle.Scores
                .Concat(candidate.Components.Select(x => new GenerationScore("candidate." + x.Id, x.RawValue, x.Explanation)))
                .Concat([
                    new GenerationScore("candidate.base", candidate.BaseScore, "Weighted sum of candidate scorers."),
                    new GenerationScore("candidate.diversity", diversity, "Diversity from locked/selected reference results."),
                    new GenerationScore("search.novelty", candidate.Novelty, "Novelty used by the deterministic seed-space search."),
                    new GenerationScore("search.objective", candidate.Objective, "Search objective before final selection diversity."),
                    new GenerationScore("candidate.total", total, "Final score including search novelty and selection diversity.")
                ]).ToArray();
            var tags = candidate.Bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            tags["candidatePipeline"] = "phase14";
            tags["search.mode"] = search.Mode.ToString();
            tags["search.evaluated"] = evaluationCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["search.generation"] = candidate.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["candidateSelectedSeed"] = candidate.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var final = candidate.Bundle with { Scores = scores, Tags = tags };
            selected.Add(final);
            references.Add(final.PrimaryText);
        }
        return selected.Count > 0 ? selected : [all.Values.OrderByDescending(x => x.Objective).First().Bundle];
    }

    private static IReadOnlyList<SearchCandidate> BuildQualityDiversityArchive(IEnumerable<SearchCandidate> candidates, int bins)
    {
        var archive = new Dictionary<(int LengthBin, int VocabularyBin), SearchCandidate>();
        foreach (var candidate in candidates)
        {
            var length = candidate.Bundle.PrimaryText?.Length ?? 0;
            var lengthBin = Math.Min(bins - 1, length / Math.Max(1, 240 / bins));
            var words = (candidate.Bundle.PrimaryText ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var ratio = words.Length == 0 ? 0 : words.Distinct(StringComparer.OrdinalIgnoreCase).Count() / (double)words.Length;
            var vocabularyBin = Math.Min(bins - 1, (int)Math.Floor(ratio * bins));
            var key = (lengthBin, vocabularyBin);
            if (!archive.TryGetValue(key, out var current) || candidate.Objective > current.Objective + 1e-12 || (Math.Abs(candidate.Objective - current.Objective) <= 1e-12 && candidate.Seed < current.Seed))
                archive[key] = candidate;
        }
        return archive.Values.OrderByDescending(x => x.Objective).ThenBy(x => x.Seed).ToArray();
    }

    private sealed record SearchCandidate(
        GeneratedTextBundle Bundle,
        ulong Seed,
        int Generation,
        IReadOnlyList<CandidateScoreComponent> Components,
        double BaseScore,
        double Novelty,
        double Objective);
}

using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Semantics;

namespace Playroma.Text.Generators.Candidates;

public sealed record CandidateScoreComponent(string Id, double RawValue, double WeightedValue, string Explanation);

public static class CandidateScoring
{
    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}']+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<CandidateScoreComponent> ScoreBundle(
        GeneratedTextBundle bundle,
        GenerationContext context,
        IEnumerable<CandidateScorerDefinition> scorers)
    {
        var result = new List<CandidateScoreComponent>();
        foreach (var scorer in scorers)
        {
            var raw = ScoreOne(bundle, context, scorer);
            raw = Math.Clamp(raw, 0, 1);
            if (scorer.Invert) raw = 1 - raw;
            result.Add(new CandidateScoreComponent(scorer.Id, raw, raw * scorer.Weight,
                scorer.Description ?? Describe(scorer, raw)));
        }
        return result;
    }

    public static double TextDiversity(string text, IEnumerable<string>? references)
    {
        if (references is null) return 1;
        var referenceArray = references.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (referenceArray.Length == 0) return 1;
        var maxSimilarity = referenceArray.Max(x => TokenJaccard(text, x));
        return Math.Clamp(1 - maxSimilarity, 0, 1);
    }

    public static double TokenJaccard(string left, string right)
    {
        var a = WordRegex.Matches(left ?? string.Empty).Select(x => x.Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var b = WordRegex.Matches(right ?? string.Empty).Select(x => x.Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 && b.Count == 0) return 1;
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b, StringComparer.Ordinal).Count();
        var union = a.Union(b, StringComparer.Ordinal).Count();
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static double ScoreOne(GeneratedTextBundle bundle, GenerationContext context, CandidateScorerDefinition scorer)
    {
        return scorer.Kind switch
        {
            CandidateScorerKind.Predicate => ScorePredicate(bundle, context, scorer),
            CandidateScorerKind.ExistingScore => ScoreExisting(bundle, scorer),
            CandidateScorerKind.CharacterLength => ScoreTarget(bundle.PrimaryText?.Length ?? 0, scorer),
            CandidateScorerKind.WordCount => ScoreTarget(WordRegex.Matches(bundle.PrimaryText ?? string.Empty).Count, scorer),
            CandidateScorerKind.UniqueWordRatio => UniqueWordRatio(bundle.PrimaryText ?? string.Empty),
            CandidateScorerKind.FeatureCoverage => FeatureCoverage(bundle, scorer.Target),
            _ => 0
        };
    }

    private static double ScorePredicate(GeneratedTextBundle bundle, GenerationContext context, CandidateScorerDefinition scorer)
    {
        var evaluation = SemanticPredicateEvaluator.Evaluate(scorer.Predicate, context, bundle);
        return evaluation.Success && evaluation.Value ? 1 : 0;
    }

    private static double ScoreExisting(GeneratedTextBundle bundle, CandidateScorerDefinition scorer)
    {
        if (string.IsNullOrWhiteSpace(scorer.Target)) return 0;
        var score = bundle.Scores.LastOrDefault(x => x.Name.Equals(scorer.Target, StringComparison.OrdinalIgnoreCase));
        if (score is null) return 0;
        if (scorer.Minimum is { } min && scorer.Maximum is { } max && max > min)
            return Math.Clamp((score.Value - min) / (max - min), 0, 1);
        return Math.Clamp(score.Value, 0, 1);
    }

    private static double ScoreTarget(double actual, CandidateScorerDefinition scorer)
    {
        if (scorer.Minimum is { } min && actual < min) return 0;
        if (scorer.Maximum is { } max && actual > max) return 0;
        if (scorer.Ideal is not { } ideal)
        {
            if (scorer.Minimum is not null || scorer.Maximum is not null) return 1;
            return Math.Clamp(actual / 100d, 0, 1);
        }
        var span = Math.Max(1d, Math.Max(Math.Abs(ideal - (scorer.Minimum ?? 0)), Math.Abs((scorer.Maximum ?? ideal * 2) - ideal)));
        return Math.Clamp(1 - Math.Abs(actual - ideal) / span, 0, 1);
    }

    private static double UniqueWordRatio(string text)
    {
        var words = WordRegex.Matches(text).Select(x => x.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return 0;
        return words.Distinct(StringComparer.Ordinal).Count() / (double)words.Length;
    }

    private static double FeatureCoverage(GeneratedTextBundle bundle, string? target)
    {
        var names = (target ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) return bundle.Features.Count > 0 ? 1 : 0;
        return names.Count(name => bundle.Features.ContainsKey(name)) / (double)names.Length;
    }

    private static string Describe(CandidateScorerDefinition scorer, double raw) => $"{scorer.Kind} score {raw:0.000}";
}

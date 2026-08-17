using Playroma.Text.Core.Corpora;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Generators.Markov;

public sealed class CorpusSimilarityGuard
{
    private sealed record Passage(string Original, string Normalized, HashSet<string> Trigrams);
    private readonly string _normalizedCorpus;
    private readonly Passage[] _passages;

    public CorpusSimilarityGuard(TextCorpus corpus)
    {
        var source = CorpusNormalizer.Normalize(corpus.Text, corpus.EffectiveNormalization);
        _normalizedCorpus = NormalizeProse(source);
        var passages = CorpusTokenizer.SplitSentences(source).Concat(source.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5000);
        _passages = passages.Select(x =>
        {
            var normalized = NormalizeProse(x);
            return new Passage(x, normalized, Grams(normalized, 3));
        }).ToArray();
    }

    public CorpusSimilarityReport Analyze(string candidate)
    {
        candidate = (candidate ?? string.Empty).Trim();
        if (candidate.Length == 0) return new(false, 0, 0, 0, 0);
        var normalizedCandidate = NormalizeProse(candidate);
        var candidateGrams = Grams(normalizedCandidate, 3);
        var exact = _passages.Any(x => string.Equals(x.Normalized, normalizedCandidate, StringComparison.Ordinal))
            || (normalizedCandidate.Length >= 24 && _normalizedCorpus.Contains(normalizedCandidate, StringComparison.Ordinal));

        var shortlist = _passages
            .Select(p => (Passage: p, Gram: Jaccard(candidateGrams, p.Trigrams), LengthRatio: (double)Math.Min(p.Normalized.Length, normalizedCandidate.Length) / Math.Max(1, Math.Max(p.Normalized.Length, normalizedCandidate.Length))))
            .Where(x => x.LengthRatio >= .35)
            .OrderByDescending(x => x.Gram)
            .ThenByDescending(x => x.LengthRatio)
            .Take(16)
            .ToArray();

        double bestEdit = 0, bestGram = 0, bestLcs = 0, best = 0;
        string? closest = null;
        foreach (var item in shortlist)
        {
            var edit = NameSimilarity.LevenshteinSimilarity(normalizedCandidate, item.Passage.Normalized);
            var lcs = NameSimilarity.LongestCommonSubstringRatio(normalizedCandidate, item.Passage.Normalized);
            var combined = Math.Max(edit, Math.Max(item.Gram, lcs));
            if (combined > best)
            {
                best = combined; bestEdit = edit; bestGram = item.Gram; bestLcs = lcs; closest = item.Passage.Original;
            }
        }
        return new(exact, bestEdit, bestGram, bestLcs, exact ? 1 : best, closest);
    }

    public bool IsAcceptable(string candidate, MarkovTrainingOptions options, out CorpusSimilarityReport report)
    {
        report = Analyze(candidate);
        if (options.PreventExactCorpusMatches && report.IsExactMatch) return false;
        return report.CombinedSimilarity <= options.MaximumCorpusSimilarity;
    }

    private static string NormalizeProse(string value)
        => string.Join(' ', (value ?? string.Empty).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static HashSet<string> Grams(string value, int n)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (value.Length == 0) return result;
        if (value.Length < n) { result.Add(value); return result; }
        for (var i = 0; i <= value.Length - n; i++) result.Add(value.Substring(i, n));
        return result;
    }

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0) return 1;
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Count <= right.Count ? left.Count(right.Contains) : right.Count(left.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}

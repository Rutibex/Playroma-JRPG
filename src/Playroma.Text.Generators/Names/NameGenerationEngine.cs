using Playroma.Text.Core.Names;
using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Names;

public sealed record NameGenerationRequest(
    string ProfileId,
    NameDomain Domain,
    NameGenerationStrategy Strategy = NameGenerationStrategy.Hybrid,
    int CandidateCount = 32,
    int? MinLength = null,
    int? MaxLength = null,
    int? MinSyllables = null,
    int? MaxSyllables = null,
    int MarkovOrder = 3,
    double? MarkovBlend = null,
    double MaxExistingSimilarity = 0.88,
    double MaxCorpusSimilarity = 0.965,
    IReadOnlyCollection<string>? ExistingNames = null,
    string? RequiredFragment = null);

public sealed record NameCandidate(string Text, double Score, NameGenerationStrategy Strategy, string? RejectionReason = null);

public sealed record NameGenerationResult(
    bool Success,
    string Text,
    string ProfileId,
    NameDomain Domain,
    NameGenerationStrategy Strategy,
    double Score,
    IReadOnlyList<NameCandidate> Candidates,
    IReadOnlyList<NameCandidate> Rejected);

public sealed class NameGenerationEngine
{
    private readonly LanguageProfileRegistry _profiles;
    public NameGenerationEngine(LanguageProfileRegistry profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public NameGenerationResult Generate(NameGenerationRequest request, IDeterministicRandom random)
    {
        var profile = _profiles.Resolve(request.ProfileId) ?? throw new KeyNotFoundException($"Unknown language profile '{request.ProfileId}'.");
        var domain = profile.EffectiveDomains.TryGetValue(request.Domain, out var found) ? found : new NameDomainProfile();
        var minLength = request.MinLength ?? domain.MinLength;
        var maxLength = request.MaxLength ?? domain.MaxLength;
        var minSyl = request.MinSyllables ?? domain.MinSyllables;
        var maxSyl = request.MaxSyllables ?? domain.MaxSyllables;
        if (minLength < 1 || maxLength < minLength || minSyl < 1 || maxSyl < minSyl) throw new ArgumentException("Invalid name length or syllable range.");

        var corpus = FindCorpus(profile, request.Domain);
        CharacterMarkovModel? markov = null;
        if (corpus.Count > 0)
            markov = CharacterMarkovModel.Train(corpus, Math.Clamp(request.MarkovOrder, 1, 8));

        var existing = (request.ExistingNames ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var accepted = new List<NameCandidate>();
        var rejected = new List<NameCandidate>();
        var attempts = Math.Clamp(request.CandidateCount, 1, 512);

        for (var i = 0; i < attempts; i++)
        {
            var candidateRandom = random.Fork($"candidate:{i}");
            var actualStrategy = ResolveStrategy(request.Strategy, markov is not null, request.MarkovBlend ?? domain.MarkovBlend, candidateRandom);
            var raw = GenerateRaw(profile, domain, actualStrategy, markov, minSyl, maxSyl, minLength, maxLength, candidateRandom);
            var text = NameOrthography.Apply(raw, profile, candidateRandom.Fork("orthography"));
            if (!string.IsNullOrWhiteSpace(request.RequiredFragment) && !text.Contains(request.RequiredFragment, StringComparison.OrdinalIgnoreCase))
                text = request.RequiredFragment + text;
            if (domain.TitleCase) text = NameOrthography.TitleCaseName(text);

            var reason = Validate(text, profile, minLength, maxLength, existing, corpus, request.MaxExistingSimilarity, request.MaxCorpusSimilarity);
            var score = Score(text, minLength, maxLength, existing, corpus);
            var item = new NameCandidate(text, score, actualStrategy, reason);
            if (reason is null) accepted.Add(item); else rejected.Add(item);
        }

        if (accepted.Count == 0)
        {
            var fallback = rejected.OrderByDescending(x => x.Score).ThenBy(x => x.Text, StringComparer.Ordinal).FirstOrDefault();
            return new NameGenerationResult(false, fallback?.Text ?? string.Empty, profile.Id, request.Domain, fallback?.Strategy ?? request.Strategy, fallback?.Score ?? 0, accepted, rejected);
        }

        var bestScore = accepted.Max(x => x.Score);
        var finalists = accepted.Where(x => Math.Abs(x.Score - bestScore) < 0.000001).OrderBy(x => x.Text, StringComparer.Ordinal).ToArray();
        var selected = finalists[random.Fork("select-best").NextInt(finalists.Length)];
        return new NameGenerationResult(true, selected.Text, profile.Id, request.Domain, selected.Strategy, selected.Score, accepted, rejected);
    }

    private static NameGenerationStrategy ResolveStrategy(NameGenerationStrategy requested, bool hasMarkov, double markovBlend, IDeterministicRandom random)
    {
        if (requested != NameGenerationStrategy.Hybrid) return requested == NameGenerationStrategy.Markov && !hasMarkov ? NameGenerationStrategy.Phonotactic : requested;
        if (hasMarkov && random.NextBool(Math.Clamp(markovBlend, 0, 1))) return NameGenerationStrategy.Markov;
        return random.NextBool(.16) ? NameGenerationStrategy.Morphological : NameGenerationStrategy.Phonotactic;
    }

    private static string GenerateRaw(LanguageProfile profile, NameDomainProfile domain, NameGenerationStrategy strategy, CharacterMarkovModel? markov,
        int minSyl, int maxSyl, int minLength, int maxLength, IDeterministicRandom random)
    {
        string core;
        if (strategy == NameGenerationStrategy.Markov && markov is not null)
            core = markov.Generate(random.Fork("markov"), Math.Max(2, minLength - 2), maxLength);
        else if (strategy == NameGenerationStrategy.Compound)
        {
            var left = GeneratePhonotactic(profile, domain, minSyl, maxSyl, random.Fork("left"));
            var right = GeneratePhonotactic(profile, domain, Math.Max(1, minSyl - 1), Math.Max(1, maxSyl - 1), random.Fork("right"));
            core = left + Pick(domain.EffectiveJoiners.Count > 0 ? domain.EffectiveJoiners : new[] { "" }, random.Fork("join")) + right;
        }
        else core = GeneratePhonotactic(profile, domain, minSyl, maxSyl, random.Fork("phonotactic"));

        var shouldMorph = strategy == NameGenerationStrategy.Morphological || strategy == NameGenerationStrategy.Hybrid || strategy == NameGenerationStrategy.Phonotactic || strategy == NameGenerationStrategy.Markov;
        if (shouldMorph) core = ApplyMorphology(core, domain, random.Fork("morphology"));
        if (strategy != NameGenerationStrategy.Compound && domain.CompoundProbability > 0 && random.NextBool(domain.CompoundProbability))
        {
            var second = GeneratePhonotactic(profile, domain, 1, Math.Max(1, maxSyl - 1), random.Fork("compound-second"));
            core += Pick(domain.EffectiveJoiners.Count > 0 ? domain.EffectiveJoiners : new[] { "" }, random.Fork("compound-join")) + second;
        }
        return core;
    }

    private static string GeneratePhonotactic(LanguageProfile profile, NameDomainProfile domain, int minSyl, int maxSyl, IDeterministicRandom random)
    {
        var count = random.NextInt(minSyl, maxSyl + 1);
        var patterns = domain.EffectivePatterns.Count > 0 ? domain.EffectivePatterns : profile.SyllablePatterns;
        if (patterns.Count == 0) patterns = new[] { "ONC" };
        var parts = new List<string>();
        for (var syllable = 0; syllable < count; syllable++)
        {
            var pattern = Pick(patterns, random.Fork($"pattern:{syllable}"));
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var symbolRandom = random.Fork($"syllable:{syllable}:symbol:{i}");
                builder.Append(pattern[i] switch
                {
                    'O' => PickWeighted(profile.Onsets, symbolRandom),
                    'N' or 'V' => PickWeighted(profile.Nuclei, symbolRandom),
                    'C' => PickWeighted(profile.Codas, symbolRandom),
                    _ => pattern[i].ToString()
                });
            }
            parts.Add(builder.ToString());
        }
        return string.Concat(parts);
    }

    private static string ApplyMorphology(string core, NameDomainProfile domain, IDeterministicRandom random)
    {
        if (domain.EffectivePrefixes.Count > 0 && random.NextBool(Math.Clamp(domain.PrefixProbability, 0, 1)))
            core = PickWeighted(domain.EffectivePrefixes, random.Fork("prefix")) + core;
        if (domain.EffectiveSuffixes.Count > 0 && random.NextBool(Math.Clamp(domain.SuffixProbability, 0, 1)))
            core += PickWeighted(domain.EffectiveSuffixes, random.Fork("suffix"));
        return core;
    }

    private static string? Validate(string text, LanguageProfile profile, int minLength, int maxLength, IReadOnlyList<string> existing,
        IReadOnlyList<string> corpus, double maxExistingSimilarity, double maxCorpusSimilarity)
    {
        if (string.IsNullOrWhiteSpace(text)) return "empty";
        if (text.Length < minLength) return "too-short";
        if (text.Length > maxLength) return "too-long";
        foreach (var forbidden in profile.EffectiveForbiddenSequences)
            if (!string.IsNullOrEmpty(forbidden) && text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)) return "forbidden-sequence:" + forbidden;
        foreach (var used in existing)
        {
            if (string.Equals(text, used, StringComparison.OrdinalIgnoreCase)) return "duplicate-existing";
            if (NameSimilarity.Composite(text, used) > maxExistingSimilarity) return "too-similar-existing";
        }
        foreach (var source in corpus)
        {
            if (string.Equals(text, source, StringComparison.OrdinalIgnoreCase)) return "exact-corpus-match";
            if (NameSimilarity.Composite(text, source) > maxCorpusSimilarity) return "too-similar-corpus";
        }
        return null;
    }

    private static double Score(string text, int minLength, int maxLength, IReadOnlyList<string> existing, IReadOnlyList<string> corpus)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NegativeInfinity;
        var normalized = NameSimilarity.Normalize(text);
        var target = (minLength + maxLength) / 2.0;
        var lengthScore = 1.0 - Math.Min(1.0, Math.Abs(text.Length - target) / Math.Max(1.0, maxLength - minLength + 1));
        var vowels = normalized.Count(c => "aeiouyæë".Contains(c));
        var vowelRatio = normalized.Length == 0 ? 0 : (double)vowels / normalized.Length;
        var pronounceability = 1.0 - Math.Min(1.0, Math.Abs(vowelRatio - .42) / .42);
        var repeatPenalty = 0.0;
        for (var i = 2; i < normalized.Length; i++) if (normalized[i] == normalized[i - 1] && normalized[i] == normalized[i - 2]) repeatPenalty += .18;
        var nearestExisting = existing.Count == 0 ? 0 : existing.Max(x => NameSimilarity.Composite(text, x));
        var nearestCorpus = corpus.Count == 0 ? 0 : corpus.Max(x => NameSimilarity.Composite(text, x));
        var novelty = 1.0 - Math.Max(nearestExisting, nearestCorpus * .72);
        return lengthScore * .28 + pronounceability * .32 + novelty * .40 - repeatPenalty;
    }

    private static IReadOnlyList<string> FindCorpus(LanguageProfile profile, NameDomain domain)
    {
        if (profile.EffectiveCorpora.TryGetValue(domain, out var direct) && direct.Count > 0) return direct;
        if (domain == NameDomain.PersonFull && profile.EffectiveCorpora.TryGetValue(NameDomain.PersonGiven, out var person)) return person;
        if (profile.EffectiveCorpora.TryGetValue(NameDomain.Custom, out var custom)) return custom;
        return Array.Empty<string>();
    }

    private static string PickWeighted(IReadOnlyList<WeightedNameToken> items, IDeterministicRandom random)
    {
        if (items.Count == 0) return string.Empty;
        var positive = items.Where(x => x.Weight > 0).ToArray();
        if (positive.Length == 0) return items[0].Text;
        var total = positive.Sum(x => x.Weight);
        var roll = random.NextDouble() * total;
        foreach (var item in positive)
        {
            if (roll < item.Weight) return item.Text;
            roll -= item.Weight;
        }
        return positive[^1].Text;
    }

    private static string Pick(IReadOnlyList<string> items, IDeterministicRandom random) => items.Count == 0 ? string.Empty : items[random.NextInt(items.Count)];
}

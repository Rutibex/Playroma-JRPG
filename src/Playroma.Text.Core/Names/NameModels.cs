using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Names;

public enum NameDomain
{
    PersonGiven,
    PersonFamily,
    PersonFull,
    Settlement,
    City,
    Village,
    Country,
    Region,
    River,
    Mountain,
    Forest,
    Dungeon,
    Deity,
    Faction,
    NobleHouse,
    Monster,
    Spell,
    Weapon,
    Armor,
    Artifact,
    Language,
    Title,
    Custom
}

public enum NameGenerationStrategy
{
    Hybrid,
    Phonotactic,
    Markov,
    Morphological,
    Compound
}

public sealed record WeightedNameToken(string Text, double Weight = 1.0);

public sealed record OrthographicRewriteRule(
    string Pattern,
    string Replacement,
    double Probability = 1.0,
    bool UseRegex = false,
    bool IgnoreCase = true);

public sealed record NameDomainProfile(
    int MinSyllables = 2,
    int MaxSyllables = 3,
    int MinLength = 3,
    int MaxLength = 18,
    IReadOnlyList<string>? SyllablePatterns = null,
    IReadOnlyList<WeightedNameToken>? Prefixes = null,
    IReadOnlyList<WeightedNameToken>? Suffixes = null,
    double PrefixProbability = 0.0,
    double SuffixProbability = 0.0,
    double CompoundProbability = 0.0,
    IReadOnlyList<string>? CompoundJoiners = null,
    bool TitleCase = true,
    double MarkovBlend = 0.35)
{
[JsonIgnore] public IReadOnlyList<string> EffectivePatterns => SyllablePatterns ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyList<WeightedNameToken> EffectivePrefixes => Prefixes ?? Array.Empty<WeightedNameToken>();
    [JsonIgnore] public IReadOnlyList<WeightedNameToken> EffectiveSuffixes => Suffixes ?? Array.Empty<WeightedNameToken>();
    [JsonIgnore] public IReadOnlyList<string> EffectiveJoiners => CompoundJoiners ?? new[] { "" };
}

public sealed record LanguageProfile(
    string Id,
    string Name,
    string? ParentId,
    IReadOnlyList<WeightedNameToken> Onsets,
    IReadOnlyList<WeightedNameToken> Nuclei,
    IReadOnlyList<WeightedNameToken> Codas,
    IReadOnlyList<string> SyllablePatterns,
    IReadOnlyList<string>? ForbiddenSequences = null,
    IReadOnlyList<OrthographicRewriteRule>? OrthographicRules = null,
    IReadOnlyDictionary<NameDomain, NameDomainProfile>? Domains = null,
    IReadOnlyDictionary<NameDomain, IReadOnlyList<string>>? Corpora = null,
    string? Description = null,
    IReadOnlySet<string>? Tags = null)
{
[JsonIgnore] public IReadOnlyList<string> EffectiveForbiddenSequences => ForbiddenSequences ?? Array.Empty<string>();
    [JsonIgnore] public IReadOnlyList<OrthographicRewriteRule> EffectiveOrthographicRules => OrthographicRules ?? Array.Empty<OrthographicRewriteRule>();
    [JsonIgnore] public IReadOnlyDictionary<NameDomain, NameDomainProfile> EffectiveDomains => Domains ?? EmptyDomains;
    [JsonIgnore] public IReadOnlyDictionary<NameDomain, IReadOnlyList<string>> EffectiveCorpora => Corpora ?? EmptyCorpora;
    [JsonIgnore] public IReadOnlySet<string> EffectiveTags => Tags ?? EmptyTags;

    private static IReadOnlyDictionary<NameDomain, NameDomainProfile> EmptyDomains { get; } = new ReadOnlyDictionary<NameDomain, NameDomainProfile>(new Dictionary<NameDomain, NameDomainProfile>());
    private static IReadOnlyDictionary<NameDomain, IReadOnlyList<string>> EmptyCorpora { get; } = new ReadOnlyDictionary<NameDomain, IReadOnlyList<string>>(new Dictionary<NameDomain, IReadOnlyList<string>>());
    private static IReadOnlySet<string> EmptyTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public static class LanguageProfileValidator
{
    public static IReadOnlyList<Playroma.Text.Core.Generation.GenerationDiagnostic> Validate(LanguageProfile profile)
    {
        var diagnostics = new List<Playroma.Text.Core.Generation.GenerationDiagnostic>();
        void Error(string message) => diagnostics.Add(new Playroma.Text.Core.Generation.GenerationDiagnostic(
            Playroma.Text.Core.Generation.GenerationDiagnosticCodes.InvalidNameProfile,
            Playroma.Text.Core.Generation.GenerationDiagnosticSeverity.Error,
            message));
        void Warning(string message) => diagnostics.Add(new Playroma.Text.Core.Generation.GenerationDiagnostic(
            Playroma.Text.Core.Generation.GenerationDiagnosticCodes.InvalidNameProfile,
            Playroma.Text.Core.Generation.GenerationDiagnosticSeverity.Warning,
            message));

        if (string.IsNullOrWhiteSpace(profile.Id)) Error("Language profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.Name)) Error("Language profile name is required.");
        if (profile.Onsets.Count == 0) Error("Language profile requires at least one onset token.");
        if (profile.Nuclei.Count == 0) Error("Language profile requires at least one nucleus token.");
        if (profile.Codas.Count == 0) Error("Language profile requires at least one coda token.");
        if (profile.SyllablePatterns.Count == 0) Error("Language profile requires at least one syllable pattern.");
        foreach (var token in profile.Onsets.Concat(profile.Nuclei).Concat(profile.Codas))
            if (token.Weight <= 0) Error($"Token '{token.Text}' has a non-positive weight.");
        foreach (var pattern in profile.SyllablePatterns)
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Any(ch => !"ONVC".Contains(ch) && !char.IsLetter(ch)))
                Warning($"Syllable pattern '{pattern}' contains unusual symbols; non O/N/V/C symbols are emitted literally.");
        foreach (var pair in profile.EffectiveDomains)
        {
            var domain = pair.Value;
            if (domain.MinSyllables < 1 || domain.MaxSyllables < domain.MinSyllables) Error($"Domain '{pair.Key}' has an invalid syllable range.");
            if (domain.MinLength < 1 || domain.MaxLength < domain.MinLength) Error($"Domain '{pair.Key}' has an invalid length range.");
            if (domain.PrefixProbability is < 0 or > 1 || domain.SuffixProbability is < 0 or > 1 || domain.CompoundProbability is < 0 or > 1 || domain.MarkovBlend is < 0 or > 1)
                Error($"Domain '{pair.Key}' has a probability outside 0..1.");
        }
        foreach (var rule in profile.EffectiveOrthographicRules)
            if (rule.Probability is < 0 or > 1) Error($"Orthographic rewrite '{rule.Pattern}' has a probability outside 0..1.");
        return diagnostics;
    }
}

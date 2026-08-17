using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Generation;

public enum CandidateScorerKind
{
    Predicate,
    ExistingScore,
    CharacterLength,
    WordCount,
    UniqueWordRatio,
    FeatureCoverage
}

public sealed record CandidateFilterDefinition(
    string Id,
    string Predicate,
    bool Required = true,
    string? Description = null);

public sealed record CandidateScorerDefinition(
    string Id,
    CandidateScorerKind Kind,
    double Weight = 1.0,
    string? Predicate = null,
    string? Target = null,
    double? Ideal = null,
    double? Minimum = null,
    double? Maximum = null,
    bool Invert = false,
    string? Description = null);

/// <summary>
/// Hidden-candidate quality pipeline. CandidateCount=1 preserves pre-Phase-6 behavior.
/// </summary>
public sealed record CandidatePipelineDefinition(
    int CandidateCount = 1,
    IReadOnlyList<CandidateFilterDefinition>? Filters = null,
    IReadOnlyList<CandidateScorerDefinition>? Scorers = null,
    double DiversityWeight = 0,
    double MinimumDiversity = 0,
    bool RejectGenerationErrors = true,
    CandidateSearchDefinition? Search = null)
{
    [JsonIgnore] public IReadOnlyList<CandidateFilterDefinition> EffectiveFilters => Filters ?? Array.Empty<CandidateFilterDefinition>();
    [JsonIgnore] public IReadOnlyList<CandidateScorerDefinition> EffectiveScorers => Scorers ?? Array.Empty<CandidateScorerDefinition>();
    [JsonIgnore] public int EffectiveCandidateCount => Math.Clamp(CandidateCount, 1, 512);
    [JsonIgnore] public double EffectiveDiversityWeight => Math.Clamp(DiversityWeight, 0, 10);
    [JsonIgnore] public double EffectiveMinimumDiversity => Math.Clamp(MinimumDiversity, 0, 1);
    [JsonIgnore] public CandidateSearchDefinition EffectiveSearch => Search ?? new CandidateSearchDefinition();
}

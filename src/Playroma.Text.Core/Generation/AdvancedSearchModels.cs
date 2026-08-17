using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Generation;

public enum CandidateSearchMode
{
    RandomPool,
    BeamSeedSearch,
    EvolutionarySeedSearch,
    QualityDiversity
}

/// <summary>
/// Optional search policy layered over the Phase 6 candidate pipeline. Search operates over
/// deterministic seed space; generators remain pure and do not need to understand optimization.
/// </summary>
public sealed record CandidateSearchDefinition(
    CandidateSearchMode Mode = CandidateSearchMode.RandomPool,
    int PopulationSize = 24,
    int Iterations = 4,
    int EliteCount = 4,
    int BeamWidth = 6,
    int ArchiveBins = 8,
    double NoveltyWeight = 0.25,
    double RandomImmigrantRate = 0.15)
{
    [JsonIgnore] public int EffectivePopulationSize => Math.Clamp(PopulationSize, 2, 128);
    [JsonIgnore] public int EffectiveIterations => Math.Clamp(Iterations, 1, 24);
    [JsonIgnore] public int EffectiveEliteCount => Math.Clamp(EliteCount, 1, EffectivePopulationSize);
    [JsonIgnore] public int EffectiveBeamWidth => Math.Clamp(BeamWidth, 1, EffectivePopulationSize);
    [JsonIgnore] public int EffectiveArchiveBins => Math.Clamp(ArchiveBins, 2, 32);
    [JsonIgnore] public double EffectiveNoveltyWeight => Math.Clamp(NoveltyWeight, 0, 5);
    [JsonIgnore] public double EffectiveRandomImmigrantRate => Math.Clamp(RandomImmigrantRate, 0, 0.75);
}

using System.Text.Json.Serialization;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.StylePacks;

namespace PlayromaMusic.Core.Orchestration;

public enum InstrumentFamily
{
    Unknown,
    Piano,
    ChromaticPercussion,
    Organ,
    Guitar,
    Bass,
    Strings,
    Ensemble,
    Brass,
    Reed,
    Pipe,
    SynthLead,
    SynthPad,
    SynthEffects,
    Ethnic,
    Percussive,
    SoundEffects,
    Drums
}

public enum OrchestrationRandomizationMode
{
    Coherent,
    Adventurous,
    Experimental,
    Chaos,
    CuratedSurprise
}

public enum PresetExplorationMode
{
    Familiar,
    Balanced,
    Underused,
    NeverUsed,
    SoundFontExpedition
}

public enum RoleDistributionStrategy
{
    SingleInstrument,
    LayeredUnison,
    OctaveDoubling,
    RegisterSplit,
    PhraseAlternation,
    CallAndResponse,
    Hocket,
    SectionSubstitution
}

public enum RandomizationDomain
{
    Everything,
    Style,
    Form,
    Harmony,
    Rhythm,
    Ensemble,
    Instruments,
    Algorithms,
    Arrangement,
    Performance
}

public sealed class TimbreProfile
{
    public double Attack { get; set; } = 0.5;
    public double Sustain { get; set; } = 0.5;
    public double Brightness { get; set; } = 0.5;
    public double Density { get; set; } = 0.5;
    public double Percussiveness { get; set; } = 0.5;
    public double PitchStability { get; set; } = 0.8;
    public double Confidence { get; set; } = 0.35;

    public TimbreProfile Clone() => new()
    {
        Attack = Attack,
        Sustain = Sustain,
        Brightness = Brightness,
        Density = Density,
        Percussiveness = Percussiveness,
        PitchStability = PitchStability,
        Confidence = Confidence
    };
}

public sealed class InstrumentPresetDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string SoundFontId { get; set; } = string.Empty;
    public string SoundFontName { get; set; } = string.Empty;
    public int Bank { get; set; }
    public int Program { get; set; }
    public string Name { get; set; } = string.Empty;
    public InstrumentFamily Family { get; set; }
    public List<string> Tags { get; set; } = new();
    public int SuggestedLowNote { get; set; } = 24;
    public int SuggestedHighNote { get; set; } = 108;
    public TimbreProfile Timbre { get; set; } = new();
    public bool IsDrumKit { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsFavorite { get; set; }
    public bool IsExcluded { get; set; }
    public bool WasAudioProfiled { get; set; }

    [JsonIgnore]
    public int BankMsb => Math.Clamp(Bank / 128, 0, 127);

    [JsonIgnore]
    public int BankLsb => Math.Clamp(Bank % 128, 0, 127);

    public InstrumentPresetDescriptor Clone() => new()
    {
        Key = Key,
        SoundFontId = SoundFontId,
        SoundFontName = SoundFontName,
        Bank = Bank,
        Program = Program,
        Name = Name,
        Family = Family,
        Tags = Tags.ToList(),
        SuggestedLowNote = SuggestedLowNote,
        SuggestedHighNote = SuggestedHighNote,
        Timbre = Timbre.Clone(),
        IsDrumKit = IsDrumKit,
        IsEnabled = IsEnabled,
        IsFavorite = IsFavorite,
        IsExcluded = IsExcluded,
        WasAudioProfiled = WasAudioProfiled
    };
}

public sealed class InstrumentCatalogDocument
{
    public string SoundFontId { get; set; } = "gm-fallback";
    public string SoundFontName { get; set; } = "General MIDI fallback catalog";
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public DateTimeOffset ScannedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsComplete { get; set; } = true;
    public List<InstrumentPresetDescriptor> Presets { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();

    [JsonIgnore]
    public int EnabledPresetCount => Presets.Count(item => item.IsEnabled && !item.IsExcluded);
}

public sealed class InstrumentSelectorSettings
{
    public List<InstrumentFamily> Families { get; set; } = new();
    public List<string> RequiredTags { get; set; } = new();
    public List<string> PreferredTags { get; set; } = new();
    public List<string> AvoidTags { get; set; } = new();
    public int MinimumPitch { get; set; } = 0;
    public int MaximumPitch { get; set; } = 127;
    public double MinimumAttack { get; set; }
    public double MaximumAttack { get; set; } = 1;
    public double MinimumSustain { get; set; }
    public double MaximumSustain { get; set; } = 1;
    public double MinimumBrightness { get; set; }
    public double MaximumBrightness { get; set; } = 1;
    public double MinimumPercussiveness { get; set; }
    public double MaximumPercussiveness { get; set; } = 1;
    public double StyleAffinityWeight { get; set; } = 1.0;
    public double NoveltyWeight { get; set; } = 0.35;
    public double RandomWeight { get; set; } = 0.25;
    public int MaximumLayers { get; set; } = 1;
    public List<RoleDistributionStrategy> AllowedStrategies { get; set; } = new() { RoleDistributionStrategy.SingleInstrument };

    public InstrumentSelectorSettings Clone() => new()
    {
        Families = Families.ToList(),
        RequiredTags = RequiredTags.ToList(),
        PreferredTags = PreferredTags.ToList(),
        AvoidTags = AvoidTags.ToList(),
        MinimumPitch = MinimumPitch,
        MaximumPitch = MaximumPitch,
        MinimumAttack = MinimumAttack,
        MaximumAttack = MaximumAttack,
        MinimumSustain = MinimumSustain,
        MaximumSustain = MaximumSustain,
        MinimumBrightness = MinimumBrightness,
        MaximumBrightness = MaximumBrightness,
        MinimumPercussiveness = MinimumPercussiveness,
        MaximumPercussiveness = MaximumPercussiveness,
        StyleAffinityWeight = StyleAffinityWeight,
        NoveltyWeight = NoveltyWeight,
        RandomWeight = RandomWeight,
        MaximumLayers = MaximumLayers,
        AllowedStrategies = AllowedStrategies.ToList()
    };
}

public sealed class PresetUsageRecord
{
    public string PresetKey { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public int AuditionCount { get; set; }
    public int PrimaryRoleCount { get; set; }
    public long LastUsedSequence { get; set; }
}

public sealed class RandomizationLockSettings
{
    public bool Style { get; set; }
    public bool Form { get; set; }
    public bool Harmony { get; set; }
    public bool Rhythm { get; set; }
    public bool Ensemble { get; set; }
    public bool Instruments { get; set; }
    public bool Algorithms { get; set; }
    public bool Arrangement { get; set; }
    public bool Performance { get; set; }

    public bool IsLocked(RandomizationDomain domain) => domain switch
    {
        RandomizationDomain.Style => Style,
        RandomizationDomain.Form => Form,
        RandomizationDomain.Harmony => Harmony,
        RandomizationDomain.Rhythm => Rhythm,
        RandomizationDomain.Ensemble => Ensemble,
        RandomizationDomain.Instruments => Instruments,
        RandomizationDomain.Algorithms => Algorithms,
        RandomizationDomain.Arrangement => Arrangement,
        RandomizationDomain.Performance => Performance,
        _ => false
    };

    public void SetLocked(RandomizationDomain domain, bool locked)
    {
        switch (domain)
        {
            case RandomizationDomain.Style: Style = locked; break;
            case RandomizationDomain.Form: Form = locked; break;
            case RandomizationDomain.Harmony: Harmony = locked; break;
            case RandomizationDomain.Rhythm: Rhythm = locked; break;
            case RandomizationDomain.Ensemble: Ensemble = locked; break;
            case RandomizationDomain.Instruments: Instruments = locked; break;
            case RandomizationDomain.Algorithms: Algorithms = locked; break;
            case RandomizationDomain.Arrangement: Arrangement = locked; break;
            case RandomizationDomain.Performance: Performance = locked; break;
            case RandomizationDomain.Everything:
                throw new ArgumentOutOfRangeException(nameof(domain), domain, "Everything is a command scope, not an individually lockable domain.");
            default:
                throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown randomization domain.");
        }
    }

    public IReadOnlyList<RandomizationDomain> GetLockedDomains() => Enum.GetValues<RandomizationDomain>()
        .Where(domain => domain is not RandomizationDomain.Everything && IsLocked(domain))
        .ToArray();
}

public sealed class TotalRandomizationSettings
{
    public OrchestrationRandomizationMode Mode { get; set; } = OrchestrationRandomizationMode.Adventurous;
    public PresetExplorationMode Exploration { get; set; } = PresetExplorationMode.Balanced;
    public int Surprise { get; set; } = 45;
    public int SurpriseBudget { get; set; } = 40;
    public bool UseCryptographicSeedForNewUniverses { get; set; } = true;
    public bool AllowStyleHybridization { get; set; } = true;
    public int MaximumStyleSources { get; set; } = 3;
    public int MinimumEnsembleSize { get; set; } = 3;
    public int MaximumEnsembleSize { get; set; } = 8;
    public bool PermitSectionInstrumentChanges { get; set; } = true;
    public bool PermitLayering { get; set; } = true;
    public bool FavorCompleteSoundFontCoverage { get; set; }
    public RandomizationLockSettings Locks { get; set; } = new();
}

public sealed class CompositionGenome
{
    public string Id { get; set; } = string.Empty;
    public string EntropySeedHex { get; set; } = string.Empty;
    public OrchestrationRandomizationMode Mode { get; set; }
    public int SurpriseSpent { get; set; }
    public List<string> SourceStyleIds { get; set; } = new();
    public string StructuralFamily { get; set; } = string.Empty;
    public string HarmonicLanguage { get; set; } = string.Empty;
    public string RhythmicLanguage { get; set; } = string.Empty;
    public string OrchestrationLanguage { get; set; } = string.Empty;
    public string EmotionalModifier { get; set; } = string.Empty;
    public string PerformanceModifier { get; set; } = string.Empty;
    public string EnsembleArchetypeId { get; set; } = string.Empty;
    public List<string> TraitIds { get; set; } = new();
    public Dictionary<string, int> SurpriseCosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InstrumentAssignmentPlan
{
    public string SectionId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public int LayerIndex { get; set; }
    public string PresetKey { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;
    public string SoundFontId { get; set; } = string.Empty;
    public int Bank { get; set; }
    public int Program { get; set; }
    public int MidiChannel { get; set; }
    public bool Active { get; set; } = true;
    public int OctaveOffset { get; set; }
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; }
    public RoleDistributionStrategy Strategy { get; set; }
    public double SuitabilityScore { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public sealed class SectionOrchestrationPlan
{
    public string SectionId { get; set; } = string.Empty;
    public double TargetEnergy { get; set; }
    public double EnsembleScore { get; set; }
    public List<InstrumentAssignmentPlan> Assignments { get; set; } = new();
}

public sealed class InstrumentCoverageStatistics
{
    public int AvailablePresetCount { get; set; }
    public int UsedPresetCount { get; set; }
    public double CoverageRatio { get; set; }
    public double MeanUniquePresetsPerSong { get; set; }
    public double DuplicatePresetRate { get; set; }
    public double MeanRoleSuitability { get; set; }
    public double MeanSectionContrast { get; set; }
    public double MeanTimbralDiversity { get; set; }
    public double RegisterCollisionRate { get; set; }
    public Dictionary<InstrumentFamily, int> FamilyUsage { get; set; } = new();
    public List<string> NeverUsedPresetKeys { get; set; } = new();
}

public sealed class ProceduralOrchestrationDocument
{
    public bool Enabled { get; set; } = true;
    public InstrumentCatalogDocument Catalog { get; set; } = new();
    public TotalRandomizationSettings Randomization { get; set; } = new();
    public CompositionGenome Genome { get; set; } = new();
    public List<SectionOrchestrationPlan> SectionPlans { get; set; } = new();
    public List<PresetUsageRecord> UsageHistory { get; set; } = new();
    public InstrumentCoverageStatistics LastCoverage { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
    public long GenerationSequence { get; set; }
}

public sealed record StyleTraitDefinition(
    string Id,
    string Name,
    string Description,
    string Category,
    int SurpriseCost,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> PreferredTags,
    IReadOnlyList<string> AvoidTags,
    IReadOnlyDictionary<TrackRole, int> DensityAdjustments,
    double EnergyAdjustment,
    double SyncopationAdjustment,
    double HumanizationAdjustment,
    IReadOnlyList<StyleFamily> Families);

public sealed record EnsembleSlotDefinition(
    string Id,
    TrackRole Role,
    bool Required,
    int MinimumLayers,
    int MaximumLayers,
    InstrumentSelectorSettings Selector);

public sealed record EnsembleArchetypeDefinition(
    string Id,
    string Name,
    string Description,
    int MinimumRoles,
    int MaximumRoles,
    IReadOnlyList<EnsembleSlotDefinition> Slots,
    IReadOnlyList<string> PreferredTraits,
    IReadOnlyList<string> AvoidedTraits);

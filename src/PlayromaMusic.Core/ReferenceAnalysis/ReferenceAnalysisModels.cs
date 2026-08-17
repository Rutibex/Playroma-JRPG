using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Core.ReferenceAnalysis;

public enum ReferenceGenerationMode
{
    CloseVariation,
    InspiredBy,
    StructuralRemix,
    GenreTranslation,
    RadicalDescendant,
    InfiniteRadio
}

public enum ReferenceOriginalityPolicy
{
    RecognizableRemix,
    AvoidDirectQuotation,
    NovelMelody,
    NovelHarmonyAndMelody,
    StructureOnly
}

/// <summary>
/// Controls how a descendant uses the programs and banks detected in the source MIDI.
/// ModeDefault chooses a sensible policy for the selected descendant mode.
/// </summary>
public enum ReferenceInstrumentationPolicy
{
    ModeDefault,
    PreserveSourcePrograms,
    PreferSourceFamilies,
    Reorchestrate
}

public enum ReferenceChordQuality
{
    Unknown,
    Major,
    Minor,
    Diminished,
    Augmented,
    SuspendedSecond,
    SuspendedFourth,
    DominantSeventh,
    MajorSeventh,
    MinorSeventh,
    HalfDiminishedSeventh,
    DiminishedSeventh,
    Power,
    Cluster
}

public enum ReferenceBlueprintDimension
{
    Form,
    SectionLengths,
    Harmony,
    HarmonicRhythm,
    Groove,
    MotifContour,
    MotifRhythm,
    Arrangement,
    Instrumentation,
    Performance,
    Tension
}

public sealed class ReferenceAnalysisDocument
{
    public bool Enabled { get; set; }
    public int AnalysisRevision { get; set; }
    public int DescendantRevision { get; set; }
    public string ActiveBlueprintId { get; set; } = string.Empty;
    public ReferenceCompositionBlueprint ActiveBlueprint { get; set; } = new();
    public List<ReferenceCompositionBlueprint> SavedBlueprints { get; set; } = new();
    public ReferenceCorpusProfile Corpus { get; set; } = new();
    public List<ReferenceDescendantEvaluation> RecentDescendants { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class ReferenceCompositionBlueprint
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Untitled Reference Blueprint";
    public int Version { get; set; } = 1;
    public ReferenceSourceInfo Source { get; set; } = new();
    public ReferenceFormProfile Form { get; set; } = new();
    public ReferenceHarmonyProfile Harmony { get; set; } = new();
    public ReferenceMotifProfile Motifs { get; set; } = new();
    public ReferenceRhythmProfile Rhythm { get; set; } = new();
    public ReferenceArrangementProfile Arrangement { get; set; } = new();
    public ReferenceInstrumentationProfile Instrumentation { get; set; } = new();
    public ReferencePerformanceProfile Performance { get; set; } = new();
    public ReferenceTensionProfile Tension { get; set; } = new();
    public ReferenceAnalysisConfidence Confidence { get; set; } = new();
    public ReferenceSourceSignature OriginalitySignature { get; set; } = new();
    public ReferenceGenerationPolicy Generation { get; set; } = new();
    public List<string> StyleHints { get; set; } = new();
    public List<string> Observations { get; set; } = new();
}

public sealed class ReferenceSourceInfo
{
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int TicksPerQuarterNote { get; set; } = 960;
    public int TrackCount { get; set; }
    public int NoteCount { get; set; }
    public int BarCount { get; set; }
    public double PrimaryTempo { get; set; } = 120;
    public int MeterNumerator { get; set; } = 4;
    public int MeterDenominator { get; set; } = 4;
    public DateTimeOffset AnalyzedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ReferenceFormProfile
{
    public int TotalBars { get; set; }
    public List<ReferenceSectionProfile> Sections { get; set; } = new();
    public List<ReferenceSectionFamily> Families { get; set; } = new();
    public double BoundaryConfidence { get; set; }
    public double RepetitionConfidence { get; set; }
}

public sealed class ReferenceSectionProfile
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public FormSectionKind Kind { get; set; } = FormSectionKind.Verse;
    public int StartBar { get; set; }
    public int LengthBars { get; set; } = 4;
    public int VariationIndex { get; set; }
    public double Energy { get; set; } = 0.5;
    public double Density { get; set; }
    public double MeanPitch { get; set; } = 60;
    public double MeanVelocity { get; set; } = 80;
    public double BoundaryConfidence { get; set; }
    public bool UserEdited { get; set; }
}

public sealed class ReferenceSectionFamily
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> SectionIds { get; set; } = new();
    public double InternalSimilarity { get; set; }
}

public sealed class ReferenceHarmonyProfile
{
    public PitchClass GlobalKeyRoot { get; set; } = PitchClass.C;
    public ScaleKind GlobalScale { get; set; } = ScaleKind.Major;
    public List<ReferenceKeyRegion> KeyRegions { get; set; } = new();
    public List<ReferenceHarmonicEvent> Events { get; set; } = new();
    public List<int> CadenceBars { get; set; } = new();
    public double MeanChangesPerBar { get; set; } = 1;
    public double Chromaticism { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceKeyRegion
{
    public int StartBar { get; set; }
    public int LengthBars { get; set; }
    public PitchClass Root { get; set; }
    public ScaleKind Scale { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceHarmonicEvent
{
    public string Id { get; set; } = string.Empty;
    public int StartBar { get; set; }
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; } = 4;
    public int RootPitchClass { get; set; }
    public ReferenceChordQuality Quality { get; set; }
    public int ScaleDegree { get; set; }
    public HarmonicFunction Function { get; set; }
    public CadenceKind Cadence { get; set; }
    public int InversionBassPitchClass { get; set; } = -1;
    public List<int> PitchClasses { get; set; } = new();
    public string RomanNumeral { get; set; } = string.Empty;
    public double Tension { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceMotifProfile
{
    public List<ReferenceMotifPattern> Motifs { get; set; } = new();
    public List<int> PreferredIntervals { get; set; } = new();
    public List<int> PreferredRhythmUnits { get; set; } = new();
    public double MeanPhraseLengthBeats { get; set; } = 4;
    public double Confidence { get; set; }
}

public sealed class ReferenceMotifPattern
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceTrackId { get; set; } = string.Empty;
    public TrackRole SourceRole { get; set; } = TrackRole.Lead;
    public List<int> Intervals { get; set; } = new();
    public List<int> RhythmUnits { get; set; } = new();
    public List<int> Contour { get; set; } = new();
    public List<int> ScaleDegrees { get; set; } = new();
    public List<ReferenceMotifOccurrence> Occurrences { get; set; } = new();
    public double Importance { get; set; }
    public double Confidence { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ReferenceMotifOccurrence
{
    public int StartBar { get; set; }
    public double StartBeat { get; set; }
    public int Transposition { get; set; }
    public double TimeScale { get; set; } = 1;
    public bool Inverted { get; set; }
    public double Similarity { get; set; } = 1;
}

public sealed class ReferenceRhythmProfile
{
    public int StepsPerBar { get; set; } = 16;
    public double SwingRatio { get; set; } = 0.5;
    public double Syncopation { get; set; }
    public double MeanNotesPerBeat { get; set; }
    public List<ReferenceRoleGroove> RoleGrooves { get; set; } = new();
    public List<int> CommonDurationUnits { get; set; } = new();
    public List<int> FillBars { get; set; } = new();
    public double Confidence { get; set; }
}

public sealed class ReferenceRoleGroove
{
    public TrackRole Role { get; set; }
    public string SourceTrackId { get; set; } = string.Empty;
    public List<double> OnsetProbabilities { get; set; } = new();
    public List<double> AccentProfile { get; set; } = new();
    public List<int> RepresentativePattern { get; set; } = new();
    public double Density { get; set; }
    public double Syncopation { get; set; }
}

public sealed class ReferenceArrangementProfile
{
    public List<ReferenceSectionArrangement> Sections { get; set; } = new();
    public double MeanActiveRoles { get; set; }
    public double SectionContrast { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceSectionArrangement
{
    public string SectionId { get; set; } = string.Empty;
    public int StartBar { get; set; }
    public double Density { get; set; }
    public double RegisterCenter { get; set; } = 60;
    public double RegisterSpan { get; set; }
    public List<ReferenceRoleActivity> Roles { get; set; } = new();
}

public sealed class ReferenceRoleActivity
{
    public TrackRole Role { get; set; }
    public string SourceTrackId { get; set; } = string.Empty;
    public bool Active { get; set; }
    public double Intensity { get; set; }
    public int RegisterLow { get; set; }
    public int RegisterHigh { get; set; } = 127;
    public int OctaveDoubling { get; set; }
}

public sealed class ReferenceInstrumentationProfile
{
    public List<ReferenceTrackProfile> Tracks { get; set; } = new();
    public int MeanEnsembleSize { get; set; }
    public bool UsesSectionProgramChanges { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceTrackProfile
{
    public string TrackId { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public int MidiChannel { get; set; }
    public int Bank { get; set; }
    public int Program { get; set; }
    public string PresetName { get; set; } = string.Empty;
    public bool IsPercussion { get; set; }
    public TrackRole InferredRole { get; set; }
    public double RoleConfidence { get; set; }
    public bool UserOverrodeRole { get; set; }
    public bool Ignored { get; set; }
    public int NoteCount { get; set; }
    public double AveragePitch { get; set; } = 60;
    public int MinimumPitch { get; set; }
    public int MaximumPitch { get; set; } = 127;
    public double NotesPerBeat { get; set; }
    public double Polyphony { get; set; }
    public double MonophonicRatio { get; set; }
    public double ChordOnsetRatio { get; set; }
    public double Repetition { get; set; }
}

public sealed class ReferencePerformanceProfile
{
    public double MeanVelocity { get; set; } = 80;
    public double VelocityDeviation { get; set; } = 10;
    public double MeanDurationRatio { get; set; } = 0.82;
    public double TimingDeviationTicks { get; set; }
    public double LegatoRatio { get; set; }
    public double StaccatoRatio { get; set; }
    public double SustainPedalUsage { get; set; }
    public List<ReferenceControllerProfile> Controllers { get; set; } = new();
    public List<ReferenceRolePerformance> Roles { get; set; } = new();
    public double Confidence { get; set; }
}

public sealed class ReferenceControllerProfile
{
    public int ControllerNumber { get; set; }
    public double MeanValue { get; set; }
    public double ChangeRatePerBar { get; set; }
}

public sealed class ReferenceRolePerformance
{
    public TrackRole Role { get; set; }
    public double MeanVelocity { get; set; } = 80;
    public double VelocityDeviation { get; set; }
    public double MeanDurationRatio { get; set; } = 0.82;
    public double TimingDeviationTicks { get; set; }
}

public sealed class ReferenceTensionProfile
{
    public List<ReferenceTensionPoint> Points { get; set; } = new();
    public int ClimaxBar { get; set; }
    public double ClimaxStrength { get; set; }
    public double ResolutionStrength { get; set; }
    public double Confidence { get; set; }
}

public sealed class ReferenceTensionPoint
{
    public int Bar { get; set; }
    public double Tension { get; set; }
    public double DensityContribution { get; set; }
    public double RegisterContribution { get; set; }
    public double VelocityContribution { get; set; }
    public double HarmonicContribution { get; set; }
}

public sealed class ReferenceAnalysisConfidence
{
    public double Overall { get; set; }
    public double Roles { get; set; }
    public double Form { get; set; }
    public double Harmony { get; set; }
    public double Motifs { get; set; }
    public double Rhythm { get; set; }
    public double Arrangement { get; set; }
    public double Performance { get; set; }
}


public sealed class ReferenceSourceSignature
{
    public int QuantizationTicks { get; set; } = 120;
    public List<string> ExactNoteTokens { get; set; } = new();
    public List<string> IntervalNGrams { get; set; } = new();
    public List<string> RhythmNGrams { get; set; } = new();
    public List<string> ChordTokens { get; set; } = new();
    public List<string> MotifContourTokens { get; set; } = new();
}

public sealed class ReferenceGenerationPolicy
{
    public ReferenceGenerationMode Mode { get; set; } = ReferenceGenerationMode.InspiredBy;
    public ReferenceOriginalityPolicy OriginalityPolicy { get; set; } = ReferenceOriginalityPolicy.AvoidDirectQuotation;
    public string TargetStyleId { get; set; } = string.Empty;
    public ReferenceInstrumentationPolicy InstrumentationPolicy { get; set; } = ReferenceInstrumentationPolicy.ModeDefault;
    public bool ReplaceInstrumentation { get; set; } = true;
    public bool AllowTempoMutation { get; set; } = true;
    public bool AllowMeterMutation { get; set; }
    public bool AllowKeyMutation { get; set; } = true;
    public bool AllowSectionExpansion { get; set; } = true;
    public bool AllowSectionContraction { get; set; } = true;
    public bool UseReferenceInstrumentsAsPreferences { get; set; }
    public int CandidateCount { get; set; } = 12;
    public Dictionary<ReferenceBlueprintDimension, double> Preservation { get; set; } = DefaultPreservation();
    public ReferenceOriginalitySettings Originality { get; set; } = new();

    public double GetPreservation(ReferenceBlueprintDimension dimension) =>
        Preservation.TryGetValue(dimension, out double value) ? Math.Clamp(value, 0, 1) : 0;

    public ReferenceInstrumentationPolicy ResolveInstrumentationPolicy(ReferenceGenerationMode mode)
    {
        if (InstrumentationPolicy != ReferenceInstrumentationPolicy.ModeDefault)
        {
            return InstrumentationPolicy;
        }

        // Preserve compatibility with blueprints authored before the explicit policy existed.
        if (!ReplaceInstrumentation) return ReferenceInstrumentationPolicy.PreserveSourcePrograms;
        if (UseReferenceInstrumentsAsPreferences) return ReferenceInstrumentationPolicy.PreferSourceFamilies;

        return mode switch
        {
            ReferenceGenerationMode.CloseVariation => ReferenceInstrumentationPolicy.PreserveSourcePrograms,
            ReferenceGenerationMode.InspiredBy => ReferenceInstrumentationPolicy.PreserveSourcePrograms,
            ReferenceGenerationMode.StructuralRemix => ReferenceInstrumentationPolicy.PreserveSourcePrograms,
            ReferenceGenerationMode.InfiniteRadio => ReferenceInstrumentationPolicy.PreferSourceFamilies,
            _ => ReferenceInstrumentationPolicy.Reorchestrate
        };
    }

    public static Dictionary<ReferenceBlueprintDimension, double> DefaultPreservation() => new()
    {
        [ReferenceBlueprintDimension.Form] = 0.85,
        [ReferenceBlueprintDimension.SectionLengths] = 0.80,
        [ReferenceBlueprintDimension.Harmony] = 0.55,
        [ReferenceBlueprintDimension.HarmonicRhythm] = 0.70,
        [ReferenceBlueprintDimension.Groove] = 0.65,
        [ReferenceBlueprintDimension.MotifContour] = 0.35,
        [ReferenceBlueprintDimension.MotifRhythm] = 0.45,
        [ReferenceBlueprintDimension.Arrangement] = 0.80,
        [ReferenceBlueprintDimension.Instrumentation] = 0.85,
        [ReferenceBlueprintDimension.Performance] = 0.55,
        [ReferenceBlueprintDimension.Tension] = 0.80
    };
}

public sealed class ReferenceOriginalitySettings
{
    public double MaximumExactNoteOverlap { get; set; } = 0.08;
    public double MaximumIntervalNGramOverlap { get; set; } = 0.32;
    public double MaximumRhythmNGramOverlap { get; set; } = 0.45;
    public double MaximumMotifContourSimilarity { get; set; } = 0.74;
    public double MaximumChordProgressionOverlap { get; set; } = 0.68;
    public int IntervalNGramSize { get; set; } = 4;
    public int RhythmNGramSize { get; set; } = 4;
    public int MaximumRepairPasses { get; set; } = 5;
}

public sealed class ReferenceDescendantEvaluation
{
    public string CandidateId { get; set; } = string.Empty;
    public ulong Seed { get; set; }
    public ReferenceGenerationMode Mode { get; set; }
    public string ProjectFingerprint { get; set; } = string.Empty;
    public double StructuralMatch { get; set; }
    public double HarmonicMatch { get; set; }
    public double GrooveMatch { get; set; }
    public double ArrangementMatch { get; set; }
    public double TensionMatch { get; set; }
    public double ExactNoteOverlap { get; set; }
    public double IntervalNGramOverlap { get; set; }
    public double RhythmNGramOverlap { get; set; }
    public double MotifContourSimilarity { get; set; }
    public double ChordProgressionOverlap { get; set; }
    public double NoveltyScore { get; set; }
    public double QualityScore { get; set; }
    public bool PassedOriginality { get; set; }
    public int RepairPasses { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class ReferenceCorpusProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Reference Corpus";
    public List<string> BlueprintIds { get; set; } = new();
    public List<ReferenceCorpusStyleCluster> Clusters { get; set; } = new();
    public Dictionary<string, double> StyleWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> FormWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> HarmonicTransitionWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> RhythmCellWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> RoleWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double MeanTempo { get; set; } = 120;
    public double MeanBars { get; set; }
    public double MeanConfidence { get; set; }
}

public sealed class ReferenceCorpusStyleCluster
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> BlueprintIds { get; set; } = new();
    public string SuggestedStyleId { get; set; } = string.Empty;
    public double Cohesion { get; set; }
}

public sealed class ReferenceCorpusAnalysisRequest
{
    public string Name { get; set; } = "Reference Corpus";
    public List<string> MidiPaths { get; set; } = new();
    public int ClusterCount { get; set; } = 3;
    public bool IgnoreLowConfidenceBlueprints { get; set; } = true;
    public double MinimumConfidence { get; set; } = 0.35;
}

public sealed class ReferenceDescendantRequest
{
    public ulong Seed { get; set; }
    public string BlueprintId { get; set; } = string.Empty;
    public ReferenceGenerationMode? ModeOverride { get; set; }
    public string TargetStyleId { get; set; } = string.Empty;
    public int CandidateCount { get; set; }
    public bool ReturnBestOnly { get; set; } = true;
}

public sealed class ReferenceBatchRequest
{
    public ulong RootSeed { get; set; } = 424242;
    public int Count { get; set; } = 100;
    public int DegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);
    public List<ReferenceGenerationMode> Modes { get; set; } = [ReferenceGenerationMode.InspiredBy];
    public int CandidatesPerSong { get; set; } = 4;
    public ReferenceBatchExpectations Expectations { get; set; } = new();
}

public sealed class ReferenceBatchExpectations
{
    public double MaximumFailureRate { get; set; } = 0.02;
    public double MinimumMeanStructuralMatch { get; set; } = 0.55;
    public double MinimumMeanGrooveMatch { get; set; } = 0.40;
    public double MinimumMeanArrangementMatch { get; set; } = 0.45;
    public double MinimumMeanNovelty { get; set; } = 0.55;
    public double MaximumMeanExactNoteOverlap { get; set; } = 0.10;
    public double MaximumMeanIntervalNGramOverlap { get; set; } = 0.38;
    public double MinimumOriginalityPassRate { get; set; } = 0.90;
}

public sealed class ReferenceBatchSample
{
    public int Index { get; set; }
    public ulong Seed { get; set; }
    public ReferenceGenerationMode Mode { get; set; }
    public bool Succeeded { get; set; }
    public string Error { get; set; } = string.Empty;
    public ReferenceDescendantEvaluation Evaluation { get; set; } = new();
}

public sealed class ReferenceBatchReport
{
    public string BlueprintId { get; set; } = string.Empty;
    public int RequestedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double FailureRate { get; set; }
    public double MeanStructuralMatch { get; set; }
    public double MeanHarmonicMatch { get; set; }
    public double MeanGrooveMatch { get; set; }
    public double MeanArrangementMatch { get; set; }
    public double MeanTensionMatch { get; set; }
    public double MeanNovelty { get; set; }
    public double MeanQuality { get; set; }
    public double MeanExactNoteOverlap { get; set; }
    public double MeanIntervalNGramOverlap { get; set; }
    public double OriginalityPassRate { get; set; }
    public bool PassedExpectations { get; set; }
    public List<string> FailedExpectations { get; set; } = new();
    public List<ReferenceBatchSample> Samples { get; set; } = new();
}

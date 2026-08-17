using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Core.StructuredComposition;

public enum FormSectionKind
{
    Intro,
    Verse,
    Chorus,
    Bridge,
    Breakdown,
    Solo,
    Outro
}

public enum HarmonicFunction
{
    Tonic,
    Predominant,
    Dominant,
    Modal
}

public enum CadenceKind
{
    None,
    Half,
    Authentic,
    Plagal,
    Deceptive,
    Modal
}

public enum MotifTransformationKind
{
    Original,
    Transpose,
    Invert,
    Retrograde,
    Rotate,
    Sequence,
    Augment,
    Diminish,
    Fragment
}

public sealed class StructuredCompositionDocument
{
    public bool Enabled { get; set; }
    public string ActiveStyleId { get; set; } = string.Empty;
    public string FormGrammarId { get; set; } = string.Empty;
    public string HarmonicGrammarId { get; set; } = string.Empty;
    public int GenerationRevision { get; set; }
    public int CandidateCount { get; set; } = 8;
    public int BarsPerPhrase { get; set; } = 2;
    public List<FormDerivationStep> FormDerivation { get; set; } = new();
    public List<FormSectionPlan> FormPlan { get; set; } = new();
    public List<HarmonicEventPlan> HarmonyPlan { get; set; } = new();
    public List<MotifDefinition> Motifs { get; set; } = new();
    public List<CompositionRoleSettings> Roles { get; set; } = new();
    public List<PhraseCandidateScore> CandidateScores { get; set; } = new();
    public StyleBlendSettings HybridStyle { get; set; } = new();
    public List<string> ResolvedStylePackIds { get; set; } = new();
}

public sealed class FormDerivationStep
{
    public int Step { get; set; }
    public string ExpandedSymbol { get; set; } = string.Empty;
    public string Production { get; set; } = string.Empty;
    public List<string> ResultingSymbols { get; set; } = new();
}

public sealed class FormSectionPlan
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FormSectionKind Kind { get; set; }
    public int VariationIndex { get; set; }
    public int StartBar { get; set; }
    public int LengthBars { get; set; }
    public double Energy { get; set; }
    public MotifTransformationKind MotifTreatment { get; set; }
}

public sealed class HarmonicEventPlan
{
    public string Id { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public int StartBar { get; set; }
    public int LengthBars { get; set; } = 1;
    public int ScaleDegree { get; set; }
    public int ChordToneCount { get; set; } = 3;
    public int Inversion { get; set; }
    public HarmonicFunction Function { get; set; }
    public CadenceKind Cadence { get; set; }
    public double Tension { get; set; }
}

public sealed class MotifDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Primary Motif";
    public int Revision { get; set; }
    public List<int> DegreeOffsets { get; set; } = new();
    public List<int> RhythmUnits { get; set; } = new();
    public List<int> Accents { get; set; } = new();
}

public sealed class CompositionRoleSettings
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TrackRole Role { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public int Revision { get; set; }
    public int MidiChannel { get; set; }
    public int GeneralMidiProgram { get; set; }
    public bool IsPercussion { get; set; }
    public string RhythmPatternId { get; set; } = string.Empty;
    public string GeneratorId { get; set; } = string.Empty;
    public int MinimumPitch { get; set; } = 36;
    public int MaximumPitch { get; set; } = 96;
    public int Density { get; set; } = 70;
    public InstrumentSelectorSettings InstrumentSelector { get; set; } = new();
}

public sealed class PhraseCandidateScore
{
    public string Id { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public int PhraseIndex { get; set; }
    public int CandidateIndex { get; set; }
    public bool Selected { get; set; }
    public bool PassedHardConstraints { get; set; }
    public double TotalScore { get; set; }
    public double ChordToneScore { get; set; }
    public double StepwiseMotionScore { get; set; }
    public double MotifFidelityScore { get; set; }
    public double PhraseClosureScore { get; set; }
    public double RhythmBalanceScore { get; set; }
    public double RangeScore { get; set; }
    public double LeapPenalty { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Core.StylePacks;

public enum StyleFamily
{
    Foundation,
    Pop,
    Jazz,
    Dance,
    Ambient,
    Algorithmic,
    Game,
    Jrpg,
    RetroConsole,
    Hybrid,
    Legacy
}

public enum StyleDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed class StyleBlendSettings
{
    public bool Enabled { get; set; }
    public string PrimaryPackId { get; set; } = string.Empty;
    public string SecondaryPackId { get; set; } = string.Empty;
    public int SecondaryWeight { get; set; } = 35;
    public bool BlendForms { get; set; } = true;
    public bool BlendHarmony { get; set; } = true;
    public bool BlendRhythms { get; set; } = true;
    public bool BlendInstruments { get; set; } = true;
    public bool BlendScoring { get; set; } = true;

    public StyleBlendSettings Clone() => new()
    {
        Enabled = Enabled,
        PrimaryPackId = PrimaryPackId,
        SecondaryPackId = SecondaryPackId,
        SecondaryWeight = SecondaryWeight,
        BlendForms = BlendForms,
        BlendHarmony = BlendHarmony,
        BlendRhythms = BlendRhythms,
        BlendInstruments = BlendInstruments,
        BlendScoring = BlendScoring
    };
}

public sealed record StyleConstraintProfile(
    int MinimumNotesPerPhrase = 2,
    int MaximumNotesPerPhrase = 96,
    int MaximumMelodicLeap = 14,
    int MaximumRepeatedPitch = 4,
    double MinimumChordToneRatio = 0.0,
    bool RequireCadentialFinalTone = false,
    int PreferredMinimumRange = 3,
    int PreferredMaximumRange = 20,
    double MaximumDensityRatio = 1.0)
{
    public static StyleConstraintProfile Default { get; } = new();
}

public sealed record StyleScorerWeights(
    double ChordTone = 25.0,
    double StepwiseMotion = 18.0,
    double MotifFidelity = 16.0,
    double PhraseClosure = 16.0,
    double RhythmBalance = 12.0,
    double Range = 10.0,
    double LeapPenalty = 1.0,
    double Syncopation = 0.0,
    double RepetitionControl = 0.0)
{
    public static StyleScorerWeights Default { get; } = new();
}

public sealed record StylePackDiagnostic(
    string PackId,
    StyleDiagnosticSeverity Severity,
    string Message,
    string Source);

public sealed class StylePackValidationSummary
{
    public DateTimeOffset ValidatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int SourceDocumentCount { get; set; }
    public int ResolvedPackCount { get; set; }
    public int ConcretePackCount { get; set; }
    public List<StylePackDiagnostic> Diagnostics { get; set; } = new();
    public bool IsValid => Diagnostics.All(item => item.Severity != StyleDiagnosticSeverity.Error);
    public int ErrorCount => Diagnostics.Count(item => item.Severity == StyleDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(item => item.Severity == StyleDiagnosticSeverity.Warning);
}

public sealed record StyleRoleMapping(
    string Id,
    string Name,
    TrackRole Role,
    int MidiChannel,
    int GeneralMidiProgram,
    bool IsPercussion,
    string RhythmPatternId,
    int MinimumPitch,
    int MaximumPitch,
    int Density);

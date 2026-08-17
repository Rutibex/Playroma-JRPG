namespace PlayromaMusic.Core.Analysis;

public sealed class MidiStyleAnalysisReport
{
    public string SourcePath { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int NoteCount { get; set; }
    public double EstimatedTempo { get; set; } = 120;
    public int MeterNumerator { get; set; } = 4;
    public int MeterDenominator { get; set; } = 4;
    public int MinimumPitch { get; set; }
    public int MaximumPitch { get; set; }
    public double NotesPerBeat { get; set; }
    public double Polyphony { get; set; }
    public double Syncopation { get; set; }
    public double Repetition { get; set; }
    public double PitchClassEntropy { get; set; }
    public string SuggestedStyleId { get; set; } = string.Empty;
    public Dictionary<string, double> StyleScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Observations { get; set; } = new();
}

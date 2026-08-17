using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;

namespace PlayromaMusic.Core.GenerationLab;

public enum AlgorithmKind
{
    ThueMorse,
    Collatz,
    Logistic,
    Henon,
    Lorenz,
    Hopalong,
    PinkNoise,
    RandomWalk,
    CellularAutomata
}

public enum ValueTransformKind
{
    Normalize,
    Invert,
    Power,
    Smooth,
    Quantize,
    Threshold,
    Wrap,
    Fold,
    Derivative,
    SampleHold,
    BiasScale,
    Clamp
}

public sealed class GenerationLabDocument
{
    public bool Enabled { get; set; }
    public string ActivePresetId { get; set; } = string.Empty;
    public int PreviewSampleCount { get; set; } = 256;
    public List<AlgorithmVoiceDefinition> Voices { get; set; } = new();
}

public sealed class AlgorithmVoiceDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Voice";
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public int Revision { get; set; }
    public AlgorithmKind Algorithm { get; set; }
    public string ParameterPresetId { get; set; } = string.Empty;
    public Dictionary<string, double> AlgorithmParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TrackRole Role { get; set; } = TrackRole.Texture;
    public int MidiChannel { get; set; }
    public int GeneralMidiProgram { get; set; }
    public bool IsPercussion { get; set; }
    public ValueGraphDefinition Graph { get; set; } = new();
    public ScaleMapperSettings PitchMapping { get; set; } = new();
    public RhythmMapperSettings RhythmMapping { get; set; } = new();
    public VelocityMapperSettings VelocityMapping { get; set; } = new();
    public ControlChangeMapperSettings ControlChangeMapping { get; set; } = new();
}

public sealed class ValueGraphDefinition
{
    public List<ValueTransformNodeDefinition> Nodes { get; set; } = new();
    public string PitchOutputNodeId { get; set; } = "source.primary";
    public string RhythmOutputNodeId { get; set; } = "source.secondary";
    public string VelocityOutputNodeId { get; set; } = "source.primary";
    public string ControlOutputNodeId { get; set; } = "source.secondary";
}

public sealed class ValueTransformNodeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ValueTransformKind Kind { get; set; }
    public string InputNodeId { get; set; } = "source.primary";
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ScaleMapperSettings
{
    public bool UseProjectScale { get; set; } = true;
    public ScaleKind ScaleOverride { get; set; } = ScaleKind.Chromatic;
    public int BaseOctave { get; set; } = 4;
    public int MinimumDegree { get; set; } = -7;
    public int MaximumDegree { get; set; } = 14;
    public int MinimumMidiPitch { get; set; } = 36;
    public int MaximumMidiPitch { get; set; } = 96;
    public List<int> PercussionPalette { get; set; } = new() { 36, 38, 42, 46, 45, 49 };
}

public sealed class RhythmMapperSettings
{
    public int StepsPerBeat { get; set; } = 2;
    public double GateThreshold { get; set; } = 0.25;
    public int MinimumDurationSteps { get; set; } = 1;
    public int MaximumDurationSteps { get; set; } = 2;
    public double NoteLengthRatio { get; set; } = 0.82;
    public double Swing { get; set; }
}

public sealed class VelocityMapperSettings
{
    public int MinimumVelocity { get; set; } = 45;
    public int MaximumVelocity { get; set; } = 112;
    public int AccentEverySteps { get; set; } = 4;
    public int AccentAmount { get; set; } = 8;
}

public sealed class ControlChangeMapperSettings
{
    public bool Enabled { get; set; }
    public int ControllerNumber { get; set; } = 1;
    public int MinimumValue { get; set; }
    public int MaximumValue { get; set; } = 100;
    public int SampleEverySteps { get; set; } = 2;
}

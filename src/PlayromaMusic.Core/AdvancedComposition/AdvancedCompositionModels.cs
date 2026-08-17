using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Core.AdvancedComposition;

public sealed class AdvancedCompositionDocument
{
    public bool Enabled { get; set; } = true;
    public ConstraintVoicingSettings Voicing { get; set; } = new();
    public EvolutionaryArrangementSettings Arrangement { get; set; } = new();
    public AdaptiveTensionSettings Tension { get; set; } = new();
    public LongRangeMotifSettings MotifConstraints { get; set; } = new();
    public ExtensionSettings Extensions { get; set; } = new();
    public NeuralGenerationSettings Neural { get; set; } = new();
    public List<TensionPoint> TensionCurve { get; set; } = new();
    public List<ArrangementSectionPlan> ArrangementPlan { get; set; } = new();
    public List<MotifOccurrencePlan> MotifOccurrences { get; set; } = new();
    public List<ExpressionScriptDefinition> Scripts { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class ConstraintVoicingSettings
{
    public bool Enabled { get; set; } = true;
    public int VoiceCount { get; set; } = 4;
    public int MaximumVoiceLeap { get; set; } = 9;
    public int MaximumAdjacentSpacing { get; set; } = 16;
    public bool AllowVoiceCrossing { get; set; }
    public bool PreferCommonTones { get; set; } = true;
    public int CandidateBudget { get; set; } = 6000;
}

public sealed class EvolutionaryArrangementSettings
{
    public bool Enabled { get; set; } = true;
    public int PopulationSize { get; set; } = 28;
    public int Generations { get; set; } = 22;
    public double MutationRate { get; set; } = 0.09;
    public int EliteCount { get; set; } = 4;
    public bool PreserveLeadAtCadences { get; set; } = true;
    public bool RequireRhythmicFoundationAtHighEnergy { get; set; } = true;
}

public sealed class AdaptiveTensionSettings
{
    public bool Enabled { get; set; } = true;
    public double Smoothing { get; set; } = 0.58;
    public double ClimaxPosition { get; set; } = 0.72;
    public double ClimaxStrength { get; set; } = 0.92;
    public double ResolutionStrength { get; set; } = 0.22;
    public int SamplesPerBar { get; set; } = 1;
}

public sealed class LongRangeMotifSettings
{
    public bool Enabled { get; set; } = true;
    public int MinimumRecurrences { get; set; } = 3;
    public int MaximumSectionsBetweenRecurrences { get; set; } = 3;
    public double MinimumContourSimilarity { get; set; } = 0.55;
    public bool RequireClimacticReturn { get; set; } = true;
    public bool RequireFinalRecall { get; set; } = true;
}

public sealed class ExtensionSettings
{
    public bool EnableExternalPlugins { get; set; } = true;
    public bool EnableUserScripts { get; set; } = true;
    public List<string> PluginSearchPaths { get; set; } = new();
    public List<string> EnabledPluginIds { get; set; } = new();
}

public sealed class NeuralGenerationSettings
{
    public bool Enabled { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.85;
    public int MaximumTokens { get; set; } = 512;
    public bool FallBackToBuiltInGenerator { get; set; } = true;
}

public sealed class TensionPoint
{
    public int Bar { get; set; }
    public double Value { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class ArrangementSectionPlan
{
    public string SectionId { get; set; } = string.Empty;
    public double TargetEnergy { get; set; }
    public double Fitness { get; set; }
    public List<ArrangementRoleState> Roles { get; set; } = new();
}

public sealed class ArrangementRoleState
{
    public string RoleId { get; set; } = string.Empty;
    public TrackRole Role { get; set; }
    public bool Active { get; set; } = true;
    public int Intensity { get; set; } = 75;
}

public sealed class MotifOccurrencePlan
{
    public string SectionId { get; set; } = string.Empty;
    public string MotifId { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public int OccurrenceIndex { get; set; }
    public MotifTransformationKind Transformation { get; set; }
    public double TargetSimilarity { get; set; } = 1.0;
    public bool Required { get; set; }
}

public sealed class ExpressionScriptDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "User expression";
    public bool Enabled { get; set; } = true;
    public string TargetRoleId { get; set; } = "*";
    public string PitchExpression { get; set; } = "pitch";
    public string VelocityExpression { get; set; } = "velocity";
    public string DurationExpression { get; set; } = "duration";
    public string GateExpression { get; set; } = "1";
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

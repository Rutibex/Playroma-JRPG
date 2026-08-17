using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Core.Extensions;

public sealed record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    int ApiVersion = 1);

public sealed record PluginDiagnostic(
    string PluginId,
    string Severity,
    string Message,
    string Source);

public sealed record StylePackTextSource(string Json, string SourceName);

public sealed class GeneratorRequest
{
    public string GeneratorId { get; init; } = string.Empty;
    public string StyleId { get; init; } = string.Empty;
    public TrackRole Role { get; init; }
    public string RoleId { get; init; } = string.Empty;
    public bool IsPercussion { get; init; }
}

public sealed class GeneratorContext
{
    public required ProjectDocument Project { get; init; }
    public required CompositionRoleSettings Role { get; init; }
    public required IReadOnlyList<FormSectionPlan> Sections { get; init; }
    public required IReadOnlyList<HarmonicEventPlan> Harmony { get; init; }
    public required IReadOnlyList<MotifDefinition> Motifs { get; init; }
    public required ulong Seed { get; init; }
    public required int TicksPerQuarterNote { get; init; }
    public required int BeatsPerBar { get; init; }
    public required int BeatUnit { get; init; }
    public double NeuralTemperature { get; init; } = 0.85;
    public int NeuralMaximumTokens { get; init; } = 512;
    public CancellationToken CancellationToken { get; init; }
}

public sealed class GeneratedNoteData
{
    public string SectionId { get; set; } = string.Empty;
    public long StartTick { get; set; }
    public long DurationTicks { get; set; }
    public int Pitch { get; set; }
    public int Velocity { get; set; } = 80;
}

public sealed class GeneratedControlChangeData
{
    public string SectionId { get; set; } = string.Empty;
    public long StartTick { get; set; }
    public int ControllerNumber { get; set; }
    public int Value { get; set; }
}

public sealed class GeneratorResult
{
    public List<GeneratedNoteData> Notes { get; set; } = new();
    public List<GeneratedControlChangeData> ControlChanges { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public interface IMusicGeneratorPlugin
{
    PluginMetadata Metadata { get; }
    bool CanGenerate(GeneratorRequest request);
    GeneratorResult Generate(GeneratorContext context);
}

public interface IStylePackSourcePlugin
{
    PluginMetadata Metadata { get; }
    IEnumerable<StylePackTextSource> GetStylePacks();
}

public interface INeuralSequenceModel
{
    PluginMetadata Metadata { get; }
    bool Supports(GeneratorRequest request);
    GeneratorResult Generate(GeneratorContext context, double temperature, int maximumTokens);
}

namespace Playroma.Sound.Graph.Definitions;

public enum GraphPortType
{
    AudioMono,
    AudioStereo,
    AudioAnyChannels,
    ControlScalar,
    Trigger,
    Event
}

public enum GraphPortDirection
{
    Input,
    Output
}

public enum GraphPortCardinality
{
    Single,
    Multiple
}

public enum GraphNodeCategory
{
    Generator,
    Processor,
    Router,
    Control,
    Event,
    Output,
    Utility
}

public enum GraphParameterType
{
    Number,
    Integer,
    Boolean,
    Enumeration,
    Text,
    Seed
}

public sealed record GraphPortDescriptor(
    string Id,
    string DisplayName,
    GraphPortDirection Direction,
    GraphPortType Type,
    bool Required = false,
    GraphPortCardinality Cardinality = GraphPortCardinality.Single);

public sealed record GraphParameterDescriptor(
    string Id,
    string DisplayName,
    GraphParameterType Type,
    GraphValue DefaultValue,
    double? Minimum = null,
    double? Maximum = null,
    string Unit = "",
    IReadOnlyList<string>? AllowedValues = null,
    bool MacroAddressable = true);

public sealed record NodeTypeDescriptor(
    string TypeId,
    string DisplayName,
    GraphNodeCategory Category,
    int CurrentSchemaVersion,
    IReadOnlyList<GraphPortDescriptor> Ports,
    IReadOnlyList<GraphParameterDescriptor> Parameters,
    string Description = "",
    bool AllowsAudioFeedbackBoundary = false);

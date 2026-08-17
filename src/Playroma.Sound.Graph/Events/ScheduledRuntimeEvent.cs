namespace Playroma.Sound.Graph.Events;

public enum RuntimeEventKind
{
    Parameter,
    Macro
}

public readonly record struct ScheduledRuntimeEvent(
    long SampleFrame,
    RuntimeEventKind Kind,
    int TargetIndex,
    int ParameterIndex,
    float Value);

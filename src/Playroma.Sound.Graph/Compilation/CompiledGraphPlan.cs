using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Compilation;

internal sealed record PendingCommand(
    int NodeIndex,
    int[] InputNodeIndices);

internal sealed record CompiledCommand(
    int NodeIndex,
    int[] InputSlots,
    int OutputSlot);

public sealed record CompiledGraphNodeInfo(
    string NodeId,
    string TypeId,
    int ExecutionIndex,
    int BufferSlot,
    int PathLatencyFrames,
    int PathTailFrames);

internal sealed record CompiledParameterAddress(int NodeIndex, int ParameterIndex);

internal sealed record CompiledMacroBinding(
    int NodeIndex,
    int ParameterIndex,
    float InputMinimum,
    float InputMaximum,
    float OutputMinimum,
    float OutputMaximum,
    MacroMappingCurve Curve,
    int Steps);

internal sealed record CompiledMacro(
    string Id,
    float Minimum,
    float Maximum,
    float DefaultValue,
    CompiledMacroBinding[] Bindings);

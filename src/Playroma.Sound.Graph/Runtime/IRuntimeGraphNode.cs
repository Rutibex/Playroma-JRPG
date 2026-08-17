using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public interface IRuntimeGraphNode
{
    NodeTypeDescriptor Descriptor { get; }
    int LatencyFrames { get; }
    int MaximumTailFrames { get; }
    bool IsGenerator { get; }
    bool IsInfinite { get; }
    bool IsComplete { get; }
    bool SupportsInPlace { get; }
    int ParameterCount { get; }

    void Prepare(AudioFormat format);
    void Reset();
    void Process(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount);
    bool TryGetParameterIndex(string parameterId, out int index);
    void SetParameter(int parameterIndex, float value);
}

public interface IInputLatencyCompensator
{
    void ConfigureInputDelays(AudioFormat format, IReadOnlyList<int> delayFrames);
}

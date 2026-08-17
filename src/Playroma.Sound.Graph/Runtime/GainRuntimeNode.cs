using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class GainRuntimeNode : RuntimeGraphNodeBase
{
    private float _gain;

    public GainRuntimeNode(NodeTypeDescriptor descriptor, float gain)
        : base(descriptor)
    {
        _gain = gain;
        RegisterParameter("gain", 0f, 8f, value => _gain = value);
    }

    public override bool SupportsInPlace => true;
    public override void Reset()
    {
    }

    protected override void ProcessCore(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount)
    {
        Span<float> output = GetOutput(buffers, outputSlot, frameCount);
        ReadOnlySpan<float> input = GetInput(buffers, inputSlots, 0, frameCount);
        if (input.IsEmpty)
        {
            output.Clear();
            return;
        }

        for (int i = 0; i < output.Length; i++)
        {
            output[i] = input[i] * _gain;
        }
    }
}

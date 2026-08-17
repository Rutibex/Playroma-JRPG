using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class PassThroughRuntimeNode : RuntimeGraphNodeBase
{
    public PassThroughRuntimeNode(NodeTypeDescriptor descriptor)
        : base(descriptor)
    {
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
        }
        else
        {
            input.CopyTo(output);
        }
    }
}

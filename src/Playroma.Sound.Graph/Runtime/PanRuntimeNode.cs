using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class PanRuntimeNode : RuntimeGraphNodeBase
{
    private float _pan;

    public PanRuntimeNode(NodeTypeDescriptor descriptor, float pan)
        : base(descriptor)
    {
        _pan = pan;
        RegisterParameter("pan", -1f, 1f, value => _pan = value);
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

        input.CopyTo(output);
        if (Format.Channels != 2)
        {
            return;
        }

        float angle = (Math.Clamp(_pan, -1f, 1f) + 1f) * (MathF.PI / 4f);
        float left = MathF.Cos(angle);
        float right = MathF.Sin(angle);
        for (int frame = 0, sample = 0; frame < frameCount; frame++, sample += 2)
        {
            output[sample] *= left;
            output[sample + 1] *= right;
        }
    }
}

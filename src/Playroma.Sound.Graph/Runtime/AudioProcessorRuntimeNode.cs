using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Processing;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class AudioProcessorRuntimeNode : RuntimeGraphNodeBase
{
    private readonly IAudioProcessor _processor;
    private readonly double _tailSeconds;

    public AudioProcessorRuntimeNode(NodeTypeDescriptor descriptor, IAudioProcessor processor, double tailSeconds = 0)
        : base(descriptor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _tailSeconds = Math.Max(0, tailSeconds);
    }

    public override int LatencyFrames => _processor.LatencyFrames;
    public override int MaximumTailFrames => Format.SampleRate == 0 ? 0 : checked((int)Math.Ceiling(_tailSeconds * Format.SampleRate));
    public override bool SupportsInPlace => true;

    public int AddParameter(string id, float minimum, float maximum, Action<float> setter) =>
        RegisterParameter(id, minimum, maximum, setter);

    protected override void OnPrepare(AudioFormat format) => _processor.Prepare(format);

    public override void Reset() => _processor.Reset();

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

        _processor.Process(output, frameCount);
    }
}

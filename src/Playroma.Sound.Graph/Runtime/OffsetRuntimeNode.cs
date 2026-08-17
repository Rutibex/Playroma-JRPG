using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class OffsetRuntimeNode : RuntimeGraphNodeBase
{
    private readonly double _delaySeconds;
    private float[] _buffer = [];
    private int _delayFrames;
    private int _writeFrame;

    public OffsetRuntimeNode(NodeTypeDescriptor descriptor, double delaySeconds)
        : base(descriptor)
    {
        _delaySeconds = Math.Max(0, delaySeconds);
    }

    public override int LatencyFrames => _delayFrames;
    public override int MaximumTailFrames => _delayFrames;

    protected override void OnPrepare(Playroma.Sound.Core.Audio.AudioFormat format)
    {
        _delayFrames = checked((int)Math.Round(_delaySeconds * format.SampleRate));
        _buffer = _delayFrames == 0 ? [] : new float[checked(_delayFrames * format.Channels)];
    }

    public override void Reset()
    {
        Array.Clear(_buffer);
        _writeFrame = 0;
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

        if (_delayFrames == 0)
        {
            input.CopyTo(output);
            return;
        }

        int channels = Format.Channels;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int ringBase = _writeFrame * channels;
            int sampleBase = frame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                int ringIndex = ringBase + channel;
                output[sampleBase + channel] = _buffer[ringIndex];
                _buffer[ringIndex] = input[sampleBase + channel];
            }

            _writeFrame++;
            if (_writeFrame == _delayFrames)
            {
                _writeFrame = 0;
            }
        }
    }
}

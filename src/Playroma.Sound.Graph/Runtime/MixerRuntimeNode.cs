using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class MixerRuntimeNode : RuntimeGraphNodeBase, IInputLatencyCompensator
{
    private FixedInputDelay[] _delays = [];
    private float _gain = 1f;

    public MixerRuntimeNode(NodeTypeDescriptor descriptor, float gain)
        : base(descriptor)
    {
        _gain = gain;
        RegisterParameter("gain", 0f, 8f, value => _gain = value);
    }

    public void ConfigureInputDelays(AudioFormat format, IReadOnlyList<int> delayFrames)
    {
        ArgumentNullException.ThrowIfNull(delayFrames);
        _delays = new FixedInputDelay[delayFrames.Count];
        for (int i = 0; i < delayFrames.Count; i++)
        {
            _delays[i] = new FixedInputDelay(format.Channels, delayFrames[i]);
        }
    }

    public override void Reset()
    {
        foreach (FixedInputDelay delay in _delays)
        {
            delay.Reset();
        }
    }

    protected override void ProcessCore(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount)
    {
        Span<float> output = GetOutput(buffers, outputSlot, frameCount);
        output.Clear();
        int channels = Format.Channels;

        for (int inputIndex = 0; inputIndex < inputSlots.Length; inputIndex++)
        {
            ReadOnlySpan<float> input = GetInput(buffers, inputSlots, inputIndex, frameCount);
            if (input.IsEmpty)
            {
                continue;
            }

            FixedInputDelay? delay = inputIndex < _delays.Length ? _delays[inputIndex] : null;
            if (delay is null || delay.DelayFrames == 0)
            {
                for (int sample = 0; sample < output.Length; sample++)
                {
                    output[sample] += input[sample] * _gain;
                }
            }
            else
            {
                delay.AddDelayed(input, output, frameCount, channels, _gain);
            }
        }
    }

    private sealed class FixedInputDelay
    {
        private readonly float[] _buffer;
        private int _writeFrame;

        public FixedInputDelay(int channels, int delayFrames)
        {
            DelayFrames = Math.Max(0, delayFrames);
            _buffer = DelayFrames == 0 ? [] : new float[checked(DelayFrames * channels)];
        }

        public int DelayFrames { get; }

        public void Reset()
        {
            Array.Clear(_buffer);
            _writeFrame = 0;
        }

        public void AddDelayed(ReadOnlySpan<float> input, Span<float> output, int frameCount, int channels, float gain)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                int ringBase = _writeFrame * channels;
                int sampleBase = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    int ringIndex = ringBase + channel;
                    float delayed = _buffer[ringIndex];
                    _buffer[ringIndex] = input[sampleBase + channel];
                    output[sampleBase + channel] += delayed * gain;
                }

                _writeFrame++;
                if (_writeFrame == DelayFrames)
                {
                    _writeFrame = 0;
                }
            }
        }
    }
}

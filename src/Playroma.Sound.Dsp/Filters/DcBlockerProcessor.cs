using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Filters;

public sealed class DcBlockerProcessor : AudioProcessorBase
{
    private float[] _previousInput = [];
    private float[] _previousOutput = [];

    public DcBlockerProcessor()
    {
        CutoffFrequency = new AudioParameter(
            new ParameterMetadata("cutoff", "Cutoff", "Hz", 1f, 200f, 20f, ParameterScale.Logarithmic),
            SmoothingMode.Exponential,
            0.02);
    }

    public AudioParameter CutoffFrequency { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        _previousInput = new float[format.Channels];
        _previousOutput = new float[format.Channels];
        CutoffFrequency.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_previousInput);
        Array.Clear(_previousOutput);
        CutoffFrequency.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float cutoff = Math.Clamp(CutoffFrequency.NextValue(), 1f, 200f);
            float coefficient = MathF.Exp(-MathF.Tau * cutoff / Format.SampleRate);

            for (int channel = 0; channel < channels; channel++)
            {
                float input = samples[index];
                float output = input - _previousInput[channel] + (coefficient * _previousOutput[channel]);
                _previousInput[channel] = input;
                _previousOutput[channel] = output;
                samples[index++] = float.IsFinite(output) ? output : 0f;
            }
        }
    }
}

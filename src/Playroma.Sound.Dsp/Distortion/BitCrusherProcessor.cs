using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;
using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Distortion;

public sealed class BitCrusherProcessor : AudioProcessorBase
{
    private float[] _heldSamples = [];
    private int _holdCounter;

    public BitCrusherProcessor()
    {
        BitDepth = new AudioParameter(
            new ParameterMetadata("bits", "Bit Depth", "bits", 2f, 24f, 8f));
        SampleRateReduction = new AudioParameter(
            new ParameterMetadata("rateReduction", "Rate Reduction", "x", 1f, 128f, 4f, ParameterScale.Logarithmic));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter BitDepth { get; }
    public AudioParameter SampleRateReduction { get; }
    public AudioParameter Mix { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        _heldSamples = new float[format.Channels];
        BitDepth.Prepare(format.SampleRate);
        SampleRateReduction.Prepare(format.SampleRate);
        Mix.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_heldSamples);
        _holdCounter = 0;
        BitDepth.Reset();
        SampleRateReduction.Reset();
        Mix.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int bits = Math.Clamp((int)MathF.Round(BitDepth.NextValue()), 2, 24);
            int reduction = Math.Clamp((int)MathF.Round(SampleRateReduction.NextValue()), 1, 128);
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);
            float levels = (1 << Math.Min(bits - 1, 23)) - 1f;
            bool capture = _holdCounter <= 0;

            for (int channel = 0; channel < channels; channel++)
            {
                float dry = samples[index];
                if (capture)
                {
                    float clamped = Math.Clamp(dry, -1f, 1f);
                    _heldSamples[channel] = MathF.Round(clamped * levels) / levels;
                }

                samples[index++] = dry + ((_heldSamples[channel] - dry) * mix);
            }

            if (capture)
            {
                _holdCounter = reduction - 1;
            }
            else
            {
                _holdCounter--;
            }
        }
    }
}

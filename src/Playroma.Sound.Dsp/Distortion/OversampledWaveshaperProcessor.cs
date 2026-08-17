using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Distortion;

/// <summary>
/// Lightweight 1x/2x/4x nonlinear processor. Intermediate samples are linearly
/// interpolated and low-pass filtered before decimation. This is intentionally
/// compact; a later spectral phase can add higher-order polyphase half-band filters.
/// </summary>
public sealed class OversampledWaveshaperProcessor : AudioProcessorBase
{
    private float[] _previousInput = [];
    private float[] _lowPassState = [];

    public OversampledWaveshaperProcessor(
        WaveshaperMode mode = WaveshaperMode.HyperbolicTangent,
        OversamplingFactor factor = OversamplingFactor.X2)
    {
        Mode = mode;
        Factor = factor;
        DriveDecibels = new AudioParameter(
            new ParameterMetadata("drive", "Drive", "dB", -24f, 48f, 12f, ParameterScale.Bipolar));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
        OutputDecibels = new AudioParameter(
            new ParameterMetadata("output", "Output", "dB", -48f, 24f, 0f, ParameterScale.Bipolar));
    }

    public WaveshaperMode Mode { get; set; }
    public OversamplingFactor Factor { get; set; }
    public AudioParameter DriveDecibels { get; }
    public AudioParameter Mix { get; }
    public AudioParameter OutputDecibels { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        _previousInput = new float[format.Channels];
        _lowPassState = new float[format.Channels];
        DriveDecibels.Prepare(format.SampleRate);
        Mix.Prepare(format.SampleRate);
        OutputDecibels.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_previousInput);
        Array.Clear(_lowPassState);
        DriveDecibels.Reset();
        Mix.Reset();
        OutputDecibels.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int factor = (int)Factor;
        int channels = Format.Channels;
        int index = 0;
        float lowPassAlpha = 1f - MathF.Exp(-MathF.Tau * 0.45f / factor);

        for (int frame = 0; frame < frameCount; frame++)
        {
            float drive = AudioMath.DecibelsToLinear(DriveDecibels.NextValue());
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);
            float outputGain = AudioMath.DecibelsToLinear(OutputDecibels.NextValue());

            for (int channel = 0; channel < channels; channel++)
            {
                float dry = samples[index];
                float shaped = 0f;

                if (factor <= 1)
                {
                    shaped = Waveshaper.Shape(dry * drive, Mode);
                }
                else
                {
                    float previous = _previousInput[channel];
                    for (int step = 1; step <= factor; step++)
                    {
                        float t = step / (float)factor;
                        float interpolated = previous + ((dry - previous) * t);
                        float nonlinear = Waveshaper.Shape(interpolated * drive, Mode);
                        _lowPassState[channel] += (nonlinear - _lowPassState[channel]) * lowPassAlpha;
                        shaped = _lowPassState[channel];
                    }
                }

                _previousInput[channel] = dry;
                float wet = shaped * outputGain;
                samples[index++] = dry + ((wet - dry) * mix);
            }
        }
    }
}

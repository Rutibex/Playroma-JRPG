using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Distortion;

public sealed class WaveshaperProcessor : AudioProcessorBase
{
    public WaveshaperProcessor(WaveshaperMode mode = WaveshaperMode.HyperbolicTangent)
    {
        Mode = mode;
        DriveDecibels = new AudioParameter(
            new ParameterMetadata("drive", "Drive", "dB", -24f, 48f, 6f, ParameterScale.Bipolar));
        Bias = new AudioParameter(
            new ParameterMetadata("bias", "Bias", string.Empty, -1f, 1f, 0f, ParameterScale.Bipolar));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
        OutputDecibels = new AudioParameter(
            new ParameterMetadata("output", "Output", "dB", -48f, 24f, 0f, ParameterScale.Bipolar));
    }

    public WaveshaperMode Mode { get; set; }
    public AudioParameter DriveDecibels { get; }
    public AudioParameter Bias { get; }
    public AudioParameter Mix { get; }
    public AudioParameter OutputDecibels { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        DriveDecibels.Prepare(format.SampleRate);
        Bias.Prepare(format.SampleRate);
        Mix.Prepare(format.SampleRate);
        OutputDecibels.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        DriveDecibels.Reset();
        Bias.Reset();
        Mix.Reset();
        OutputDecibels.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float drive = AudioMath.DecibelsToLinear(DriveDecibels.NextValue());
            float bias = Bias.NextValue();
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);
            float outputGain = AudioMath.DecibelsToLinear(OutputDecibels.NextValue());

            for (int channel = 0; channel < channels; channel++)
            {
                float dry = samples[index];
                float wet = Waveshaper.Shape((dry * drive) + bias, Mode) * outputGain;
                samples[index++] = dry + ((wet - dry) * mix);
            }
        }
    }
}

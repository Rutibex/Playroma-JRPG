using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Delay;

/// <summary>
/// Fractionally modulated delay suitable for chorus and flanging.
/// </summary>
public sealed class ModulatedDelayProcessor : AudioProcessorBase
{
    private readonly FractionalDelayLine _delayLine = new();
    private double _phase;

    public ModulatedDelayProcessor()
    {
        BaseDelayMilliseconds = new AudioParameter(
            new ParameterMetadata("baseDelay", "Base Delay", "ms", 0.1f, 50f, 12f, ParameterScale.Logarithmic));
        DepthMilliseconds = new AudioParameter(
            new ParameterMetadata("depth", "Depth", "ms", 0f, 30f, 4f));
        RateHz = new AudioParameter(
            new ParameterMetadata("rate", "Rate", "Hz", 0.01f, 20f, 0.35f, ParameterScale.Logarithmic));
        Feedback = new AudioParameter(
            new ParameterMetadata("feedback", "Feedback", string.Empty, -0.95f, 0.95f, 0.05f, ParameterScale.Bipolar));
        Wet = new AudioParameter(
            new ParameterMetadata("wet", "Wet", string.Empty, 0f, 1f, 0.45f));
        Dry = new AudioParameter(
            new ParameterMetadata("dry", "Dry", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter BaseDelayMilliseconds { get; }
    public AudioParameter DepthMilliseconds { get; }
    public AudioParameter RateHz { get; }
    public AudioParameter Feedback { get; }
    public AudioParameter Wet { get; }
    public AudioParameter Dry { get; }
    public float StereoPhaseOffsetDegrees { get; set; } = 90f;

    protected override void OnPrepare(AudioFormat format)
    {
        int maximumFrames = Math.Max(2, (int)Math.Ceiling(0.1 * format.SampleRate));
        _delayLine.Prepare(format.Channels, maximumFrames);
        BaseDelayMilliseconds.Prepare(format.SampleRate);
        DepthMilliseconds.Prepare(format.SampleRate);
        RateHz.Prepare(format.SampleRate);
        Feedback.Prepare(format.SampleRate);
        Wet.Prepare(format.SampleRate);
        Dry.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        _delayLine.Reset();
        _phase = 0;
        BaseDelayMilliseconds.Reset();
        DepthMilliseconds.Reset();
        RateHz.Reset();
        Feedback.Reset();
        Wet.Reset();
        Dry.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        Span<float> writeFrame = stackalloc float[32];
        writeFrame = writeFrame[..channels];
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float baseDelay = BaseDelayMilliseconds.NextValue();
            float depth = DepthMilliseconds.NextValue();
            float rate = RateHz.NextValue();
            float feedback = Math.Clamp(Feedback.NextValue(), -0.95f, 0.95f);
            float wet = Math.Clamp(Wet.NextValue(), 0f, 1f);
            float dry = Math.Clamp(Dry.NextValue(), 0f, 1f);

            for (int channel = 0; channel < channels; channel++)
            {
                double channelOffset = channels > 1
                    ? (StereoPhaseOffsetDegrees / 360.0) * channel
                    : 0.0;
                double channelPhase = _phase + channelOffset;
                channelPhase -= Math.Floor(channelPhase);
                float modulation = 0.5f + (0.5f * MathF.Sin((float)(channelPhase * Math.Tau)));
                float delayMilliseconds = baseDelay + (depth * modulation);
                float delayFrames = Math.Clamp(
                    delayMilliseconds * Format.SampleRate / 1_000f,
                    0f,
                    _delayLine.MaximumDelayFrames);

                float input = samples[index];
                float delayed = _delayLine.Read(channel, delayFrames);
                writeFrame[channel] = input + (delayed * feedback);
                samples[index++] = (input * dry) + (delayed * wet);
            }

            _delayLine.WriteFrame(writeFrame);
            _phase += rate / Format.SampleRate;
            _phase -= Math.Floor(_phase);
        }
    }
}

using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Delay;

public sealed record DelayTap(float DelayMilliseconds, float Gain, float Pan = 0f);

/// <summary>
/// Deterministic multi-tap delay. Taps should be configured before Prepare.
/// </summary>
public sealed class MultiTapDelayProcessor : AudioProcessorBase
{
    private readonly List<DelayTap> _taps = [];
    private readonly FractionalDelayLine _delayLine = new();
    private readonly double _maximumDelaySeconds;

    public MultiTapDelayProcessor(double maximumDelaySeconds = 8.0)
    {
        if (!double.IsFinite(maximumDelaySeconds) || maximumDelaySeconds <= 0 || maximumDelaySeconds > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelaySeconds));
        }

        _maximumDelaySeconds = maximumDelaySeconds;
        Wet = new AudioParameter(new ParameterMetadata("wet", "Wet", string.Empty, 0f, 1f, 0.5f));
        Dry = new AudioParameter(new ParameterMetadata("dry", "Dry", string.Empty, 0f, 1f, 1f));
        Feedback = new AudioParameter(
            new ParameterMetadata("feedback", "Feedback", string.Empty, -0.95f, 0.95f, 0f, ParameterScale.Bipolar));
    }

    public IReadOnlyList<DelayTap> Taps => _taps;
    public AudioParameter Wet { get; }
    public AudioParameter Dry { get; }
    public AudioParameter Feedback { get; }

    public MultiTapDelayProcessor AddTap(float delayMilliseconds, float gain, float pan = 0f)
    {
        if (!float.IsFinite(delayMilliseconds) || delayMilliseconds < 0 ||
            delayMilliseconds > _maximumDelaySeconds * 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        }

        if (!float.IsFinite(gain) || !float.IsFinite(pan))
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }

        _taps.Add(new DelayTap(delayMilliseconds, gain, Math.Clamp(pan, -1f, 1f)));
        return this;
    }

    protected override void OnPrepare(AudioFormat format)
    {
        int maximumFrames = Math.Max(1, (int)Math.Ceiling(_maximumDelaySeconds * format.SampleRate));
        _delayLine.Prepare(format.Channels, maximumFrames);
        Wet.Prepare(format.SampleRate);
        Dry.Prepare(format.SampleRate);
        Feedback.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        _delayLine.Reset();
        Wet.Reset();
        Dry.Reset();
        Feedback.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        Span<float> writeFrame = stackalloc float[32];
        Span<float> wetFrame = stackalloc float[32];
        writeFrame = writeFrame[..channels];
        wetFrame = wetFrame[..channels];
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            wetFrame.Clear();
            float wet = Math.Clamp(Wet.NextValue(), 0f, 1f);
            float dry = Math.Clamp(Dry.NextValue(), 0f, 1f);
            float feedback = Math.Clamp(Feedback.NextValue(), -0.95f, 0.95f);

            foreach (DelayTap tap in _taps)
            {
                float delayFrames = Math.Clamp(
                    tap.DelayMilliseconds * Format.SampleRate / 1_000f,
                    0f,
                    _delayLine.MaximumDelayFrames);

                for (int channel = 0; channel < channels; channel++)
                {
                    float channelGain = tap.Gain;
                    if (channels == 2)
                    {
                        float left = MathF.Cos((tap.Pan + 1f) * MathF.PI * 0.25f);
                        float right = MathF.Sin((tap.Pan + 1f) * MathF.PI * 0.25f);
                        channelGain *= channel == 0 ? left : right;
                    }

                    wetFrame[channel] += _delayLine.Read(channel, delayFrames) * channelGain;
                }
            }

            for (int channel = 0; channel < channels; channel++)
            {
                float input = samples[index + channel];
                writeFrame[channel] = input + (wetFrame[channel] * feedback);
                samples[index + channel] = (input * dry) + (wetFrame[channel] * wet);
            }

            _delayLine.WriteFrame(writeFrame);
            index += channels;
        }
    }
}

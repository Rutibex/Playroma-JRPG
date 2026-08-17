using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Delay;

public sealed class DelayProcessor : AudioProcessorBase
{
    private readonly FractionalDelayLine _delayLine = new();
    private readonly double _maximumDelaySeconds;

    public DelayProcessor(double maximumDelaySeconds = 4.0)
    {
        if (!double.IsFinite(maximumDelaySeconds) || maximumDelaySeconds <= 0 || maximumDelaySeconds > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelaySeconds));
        }

        _maximumDelaySeconds = maximumDelaySeconds;
        DelayMilliseconds = new AudioParameter(
            new ParameterMetadata("delay", "Delay", "ms", 0f, (float)(maximumDelaySeconds * 1_000), 250f));
        Feedback = new AudioParameter(
            new ParameterMetadata("feedback", "Feedback", string.Empty, -0.99f, 0.99f, 0.25f, ParameterScale.Bipolar));
        Wet = new AudioParameter(
            new ParameterMetadata("wet", "Wet", string.Empty, 0f, 1f, 0.35f));
        Dry = new AudioParameter(
            new ParameterMetadata("dry", "Dry", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter DelayMilliseconds { get; }
    public AudioParameter Feedback { get; }
    public AudioParameter Wet { get; }
    public AudioParameter Dry { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        int maximumFrames = Math.Max(1, (int)Math.Ceiling(_maximumDelaySeconds * format.SampleRate));
        _delayLine.Prepare(format.Channels, maximumFrames);
        DelayMilliseconds.Prepare(format.SampleRate);
        Feedback.Prepare(format.SampleRate);
        Wet.Prepare(format.SampleRate);
        Dry.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        _delayLine.Reset();
        DelayMilliseconds.Reset();
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
            float delayFrames = Math.Clamp(
                DelayMilliseconds.NextValue() * Format.SampleRate / 1_000f,
                0f,
                _delayLine.MaximumDelayFrames);
            float feedback = Math.Clamp(Feedback.NextValue(), -0.99f, 0.99f);
            float wet = Math.Clamp(Wet.NextValue(), 0f, 1f);
            float dry = Math.Clamp(Dry.NextValue(), 0f, 1f);

            for (int channel = 0; channel < channels; channel++)
            {
                float input = samples[index];
                float delayed = _delayLine.Read(channel, delayFrames);
                writeFrame[channel] = input + (delayed * feedback);
                samples[index++] = (input * dry) + (delayed * wet);
            }

            _delayLine.WriteFrame(writeFrame);
        }
    }
}

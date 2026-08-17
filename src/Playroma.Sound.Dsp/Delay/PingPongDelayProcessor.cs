using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Delay;

public sealed class PingPongDelayProcessor : AudioProcessorBase
{
    private readonly FractionalDelayLine _delayLine = new();
    private readonly double _maximumDelaySeconds;

    public PingPongDelayProcessor(double maximumDelaySeconds = 4.0)
    {
        if (!double.IsFinite(maximumDelaySeconds) || maximumDelaySeconds <= 0 || maximumDelaySeconds > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelaySeconds));
        }

        _maximumDelaySeconds = maximumDelaySeconds;
        DelayMilliseconds = new AudioParameter(
            new ParameterMetadata("delay", "Delay", "ms", 1f, (float)(maximumDelaySeconds * 1_000), 300f));
        Feedback = new AudioParameter(
            new ParameterMetadata("feedback", "Feedback", string.Empty, 0f, 0.98f, 0.45f));
        Wet = new AudioParameter(new ParameterMetadata("wet", "Wet", string.Empty, 0f, 1f, 0.4f));
        Dry = new AudioParameter(new ParameterMetadata("dry", "Dry", string.Empty, 0f, 1f, 1f));
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
            float feedback = Math.Clamp(Feedback.NextValue(), 0f, 0.98f);
            float wet = Math.Clamp(Wet.NextValue(), 0f, 1f);
            float dry = Math.Clamp(Dry.NextValue(), 0f, 1f);

            if (channels >= 2)
            {
                float leftInput = samples[index];
                float rightInput = samples[index + 1];
                float leftDelayed = _delayLine.Read(0, delayFrames);
                float rightDelayed = _delayLine.Read(1, delayFrames);
                writeFrame[0] = leftInput + (rightDelayed * feedback);
                writeFrame[1] = rightInput + (leftDelayed * feedback);
                samples[index] = (leftInput * dry) + (leftDelayed * wet);
                samples[index + 1] = (rightInput * dry) + (rightDelayed * wet);

                for (int channel = 2; channel < channels; channel++)
                {
                    float input = samples[index + channel];
                    float delayed = _delayLine.Read(channel, delayFrames);
                    writeFrame[channel] = input + (delayed * feedback);
                    samples[index + channel] = (input * dry) + (delayed * wet);
                }
            }
            else
            {
                float input = samples[index];
                float delayed = _delayLine.Read(0, delayFrames);
                writeFrame[0] = input + (delayed * feedback);
                samples[index] = (input * dry) + (delayed * wet);
            }

            _delayLine.WriteFrame(writeFrame);
            index += channels;
        }
    }
}

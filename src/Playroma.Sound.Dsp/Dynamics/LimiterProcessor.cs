using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

/// <summary>
/// Zero-lookahead linked peak limiter with immediate attack and smoothed release.
/// </summary>
public sealed class LimiterProcessor : AudioProcessorBase
{
    private float _gain = 1f;

    public LimiterProcessor()
    {
        CeilingDecibels = new AudioParameter(
            new ParameterMetadata("ceiling", "Ceiling", "dB", -24f, 0f, -0.5f));
        ReleaseMilliseconds = new AudioParameter(
            new ParameterMetadata("release", "Release", "ms", 1f, 5_000f, 100f, ParameterScale.Logarithmic));
    }

    public AudioParameter CeilingDecibels { get; }
    public AudioParameter ReleaseMilliseconds { get; }
    public float CurrentGain => _gain;

    protected override void OnPrepare(AudioFormat format)
    {
        CeilingDecibels.Prepare(format.SampleRate);
        ReleaseMilliseconds.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        _gain = 1f;
        CeilingDecibels.Reset();
        ReleaseMilliseconds.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float ceiling = AudioMath.DecibelsToLinear(CeilingDecibels.NextValue());
            float releaseSeconds = ReleaseMilliseconds.NextValue() / 1_000f;
            float releaseCoefficient = releaseSeconds <= 0f
                ? 0f
                : MathF.Exp(-1f / (releaseSeconds * Format.SampleRate));

            float peak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                peak = MathF.Max(peak, MathF.Abs(samples[index + channel]));
            }

            float desiredGain = peak > ceiling && peak > 0f ? ceiling / peak : 1f;
            if (desiredGain < _gain)
            {
                _gain = desiredGain;
            }
            else
            {
                _gain = desiredGain + (releaseCoefficient * (_gain - desiredGain));
            }

            for (int channel = 0; channel < channels; channel++)
            {
                samples[index++] *= _gain;
            }
        }
    }
}

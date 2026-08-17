using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

/// <summary>
/// Linked downward expander. Signals below threshold are attenuated according to Ratio,
/// with RangeDecibels limiting the maximum attenuation.
/// </summary>
public sealed class ExpanderProcessor : AudioProcessorBase
{
    private readonly EnvelopeFollower _detector = new();

    public ExpanderProcessor()
    {
        ThresholdDecibels = new AudioParameter(
            new ParameterMetadata("threshold", "Threshold", "dB", -100f, 0f, -42f));
        Ratio = new AudioParameter(
            new ParameterMetadata("ratio", "Ratio", ":1", 1f, 20f, 2f, ParameterScale.Logarithmic));
        AttackMilliseconds = new AudioParameter(
            new ParameterMetadata("attack", "Attack", "ms", 0.1f, 500f, 8f, ParameterScale.Logarithmic));
        ReleaseMilliseconds = new AudioParameter(
            new ParameterMetadata("release", "Release", "ms", 1f, 5_000f, 150f, ParameterScale.Logarithmic));
        RangeDecibels = new AudioParameter(
            new ParameterMetadata("range", "Range", "dB", 0f, 120f, 48f));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter ThresholdDecibels { get; }
    public AudioParameter Ratio { get; }
    public AudioParameter AttackMilliseconds { get; }
    public AudioParameter ReleaseMilliseconds { get; }
    public AudioParameter RangeDecibels { get; }
    public AudioParameter Mix { get; }
    public float CurrentGainReductionDecibels { get; private set; }

    protected override void OnPrepare(AudioFormat format)
    {
        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Prepare(format.SampleRate);
        }

        _detector.Prepare(format.SampleRate, 0.008, 0.15);
    }

    public override void Reset()
    {
        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Reset();
        }

        _detector.Reset();
        CurrentGainReductionDecibels = 0f;
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float threshold = ThresholdDecibels.NextValue();
            float ratio = Math.Max(1f, Ratio.NextValue());
            float attack = AttackMilliseconds.NextValue() / 1_000f;
            float release = ReleaseMilliseconds.NextValue() / 1_000f;
            float range = Math.Max(0f, RangeDecibels.NextValue());
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);
            _detector.SetTimes(attack, release);

            float peak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                peak = MathF.Max(peak, MathF.Abs(samples[index + channel]));
            }

            float envelope = _detector.Process(peak);
            float inputDb = envelope > 0.0000001f ? AudioMath.LinearToDecibels(envelope) : -140f;
            float gainDb = 0f;
            if (inputDb < threshold)
            {
                gainDb = (ratio - 1f) * (inputDb - threshold);
                gainDb = Math.Max(gainDb, -range);
            }

            CurrentGainReductionDecibels = -gainDb;
            float gain = AudioMath.DecibelsToLinear(gainDb);

            for (int channel = 0; channel < channels; channel++)
            {
                float dry = samples[index];
                float wet = dry * gain;
                samples[index++] = dry + ((wet - dry) * mix);
            }
        }
    }

    private IEnumerable<AudioParameter> Parameters()
    {
        yield return ThresholdDecibels;
        yield return Ratio;
        yield return AttackMilliseconds;
        yield return ReleaseMilliseconds;
        yield return RangeDecibels;
        yield return Mix;
    }
}

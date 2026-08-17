using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

public sealed class CompressorProcessor : AudioProcessorBase
{
    private readonly EnvelopeFollower _detector = new();

    public CompressorProcessor()
    {
        ThresholdDecibels = new AudioParameter(
            new ParameterMetadata("threshold", "Threshold", "dB", -80f, 0f, -18f));
        Ratio = new AudioParameter(
            new ParameterMetadata("ratio", "Ratio", ":1", 1f, 100f, 4f, ParameterScale.Logarithmic));
        KneeDecibels = new AudioParameter(
            new ParameterMetadata("knee", "Knee", "dB", 0f, 24f, 6f));
        AttackMilliseconds = new AudioParameter(
            new ParameterMetadata("attack", "Attack", "ms", 0.01f, 500f, 10f, ParameterScale.Logarithmic));
        ReleaseMilliseconds = new AudioParameter(
            new ParameterMetadata("release", "Release", "ms", 1f, 5_000f, 120f, ParameterScale.Logarithmic));
        MakeupGainDecibels = new AudioParameter(
            new ParameterMetadata("makeup", "Makeup", "dB", -24f, 36f, 0f, ParameterScale.Bipolar));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter ThresholdDecibels { get; }
    public AudioParameter Ratio { get; }
    public AudioParameter KneeDecibels { get; }
    public AudioParameter AttackMilliseconds { get; }
    public AudioParameter ReleaseMilliseconds { get; }
    public AudioParameter MakeupGainDecibels { get; }
    public AudioParameter Mix { get; }
    public float CurrentGainReductionDecibels { get; private set; }

    protected override void OnPrepare(AudioFormat format)
    {
        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Prepare(format.SampleRate);
        }

        _detector.Prepare(format.SampleRate, 0.01, 0.12);
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
            float knee = Math.Max(0f, KneeDecibels.NextValue());
            float attack = AttackMilliseconds.NextValue() / 1_000f;
            float release = ReleaseMilliseconds.NextValue() / 1_000f;
            float makeup = AudioMath.DecibelsToLinear(MakeupGainDecibels.NextValue());
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);
            _detector.SetTimes(attack, release);

            float detectorInput = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                detectorInput = MathF.Max(detectorInput, MathF.Abs(samples[index + channel]));
            }

            float envelope = _detector.Process(detectorInput);
            float inputDb = envelope > 0.0000001f ? AudioMath.LinearToDecibels(envelope) : -140f;
            float gainReductionDb = CalculateGainReduction(inputDb, threshold, ratio, knee);
            CurrentGainReductionDecibels = -gainReductionDb;
            float gain = AudioMath.DecibelsToLinear(gainReductionDb) * makeup;

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
        yield return KneeDecibels;
        yield return AttackMilliseconds;
        yield return ReleaseMilliseconds;
        yield return MakeupGainDecibels;
        yield return Mix;
    }

    private static float CalculateGainReduction(float inputDb, float threshold, float ratio, float knee)
    {
        float over = inputDb - threshold;
        if (knee <= 0f)
        {
            return over > 0f ? ((1f / ratio) - 1f) * over : 0f;
        }

        float halfKnee = knee * 0.5f;
        if (over <= -halfKnee)
        {
            return 0f;
        }

        if (over >= halfKnee)
        {
            return ((1f / ratio) - 1f) * over;
        }

        float curved = over + halfKnee;
        return ((1f / ratio) - 1f) * curved * curved / (2f * knee);
    }
}

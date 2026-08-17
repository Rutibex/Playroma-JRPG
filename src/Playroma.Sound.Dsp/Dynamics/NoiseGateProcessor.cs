using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

public sealed class NoiseGateProcessor : AudioProcessorBase
{
    private readonly EnvelopeFollower _detector = new();
    private float _gain;
    private int _holdFramesRemaining;
    private bool _open;

    public NoiseGateProcessor()
    {
        ThresholdDecibels = new AudioParameter(
            new ParameterMetadata("threshold", "Threshold", "dB", -100f, 0f, -45f));
        HysteresisDecibels = new AudioParameter(
            new ParameterMetadata("hysteresis", "Hysteresis", "dB", 0f, 24f, 6f));
        AttackMilliseconds = new AudioParameter(
            new ParameterMetadata("attack", "Attack", "ms", 0.1f, 500f, 5f, ParameterScale.Logarithmic));
        ReleaseMilliseconds = new AudioParameter(
            new ParameterMetadata("release", "Release", "ms", 1f, 5_000f, 120f, ParameterScale.Logarithmic));
        HoldMilliseconds = new AudioParameter(
            new ParameterMetadata("hold", "Hold", "ms", 0f, 2_000f, 40f));
        FloorDecibels = new AudioParameter(
            new ParameterMetadata("floor", "Floor", "dB", -120f, 0f, -80f));
    }

    public AudioParameter ThresholdDecibels { get; }
    public AudioParameter HysteresisDecibels { get; }
    public AudioParameter AttackMilliseconds { get; }
    public AudioParameter ReleaseMilliseconds { get; }
    public AudioParameter HoldMilliseconds { get; }
    public AudioParameter FloorDecibels { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Prepare(format.SampleRate);
        }

        _detector.Prepare(format.SampleRate, 0.002, 0.05);
    }

    public override void Reset()
    {
        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Reset();
        }

        _detector.Reset();
        _gain = 0f;
        _holdFramesRemaining = 0;
        _open = false;
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float threshold = ThresholdDecibels.NextValue();
            float hysteresis = HysteresisDecibels.NextValue();
            float attackSeconds = AttackMilliseconds.NextValue() / 1_000f;
            float releaseSeconds = ReleaseMilliseconds.NextValue() / 1_000f;
            int holdFrames = (int)(HoldMilliseconds.NextValue() * Format.SampleRate / 1_000f);
            float floorGain = AudioMath.DecibelsToLinear(FloorDecibels.NextValue());

            float peak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                peak = MathF.Max(peak, MathF.Abs(samples[index + channel]));
            }

            float envelope = _detector.Process(peak);
            float levelDb = envelope > 0.0000001f ? AudioMath.LinearToDecibels(envelope) : -140f;

            if (!_open && levelDb >= threshold)
            {
                _open = true;
                _holdFramesRemaining = holdFrames;
            }
            else if (_open)
            {
                if (levelDb >= threshold - hysteresis)
                {
                    _holdFramesRemaining = holdFrames;
                }
                else if (_holdFramesRemaining > 0)
                {
                    _holdFramesRemaining--;
                }
                else
                {
                    _open = false;
                }
            }

            float targetGain = _open ? 1f : floorGain;
            float time = targetGain > _gain ? attackSeconds : releaseSeconds;
            float coefficient = time <= 0f ? 0f : MathF.Exp(-1f / (time * Format.SampleRate));
            _gain = targetGain + (coefficient * (_gain - targetGain));

            for (int channel = 0; channel < channels; channel++)
            {
                samples[index++] *= _gain;
            }
        }
    }

    private IEnumerable<AudioParameter> Parameters()
    {
        yield return ThresholdDecibels;
        yield return HysteresisDecibels;
        yield return AttackMilliseconds;
        yield return ReleaseMilliseconds;
        yield return HoldMilliseconds;
        yield return FloorDecibels;
    }
}

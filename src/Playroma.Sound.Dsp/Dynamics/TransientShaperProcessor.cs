using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

/// <summary>
/// Envelope-difference transient shaper. Attack changes fast transients while Sustain
/// changes the slower body of the signal.
/// </summary>
public sealed class TransientShaperProcessor : AudioProcessorBase
{
    private readonly EnvelopeFollower _fastEnvelope = new();
    private readonly EnvelopeFollower _slowEnvelope = new();

    public TransientShaperProcessor()
    {
        AttackDecibels = new AudioParameter(
            new ParameterMetadata("attackAmount", "Attack", "dB", -24f, 24f, 0f, ParameterScale.Bipolar));
        SustainDecibels = new AudioParameter(
            new ParameterMetadata("sustainAmount", "Sustain", "dB", -24f, 24f, 0f, ParameterScale.Bipolar));
        Sensitivity = new AudioParameter(
            new ParameterMetadata("sensitivity", "Sensitivity", string.Empty, 0.1f, 8f, 1f, ParameterScale.Logarithmic));
        Mix = new AudioParameter(
            new ParameterMetadata("mix", "Mix", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter AttackDecibels { get; }
    public AudioParameter SustainDecibels { get; }
    public AudioParameter Sensitivity { get; }
    public AudioParameter Mix { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        AttackDecibels.Prepare(format.SampleRate);
        SustainDecibels.Prepare(format.SampleRate);
        Sensitivity.Prepare(format.SampleRate);
        Mix.Prepare(format.SampleRate);
        _fastEnvelope.Prepare(format.SampleRate, 0.001, 0.025);
        _slowEnvelope.Prepare(format.SampleRate, 0.020, 0.250);
    }

    public override void Reset()
    {
        AttackDecibels.Reset();
        SustainDecibels.Reset();
        Sensitivity.Reset();
        Mix.Reset();
        _fastEnvelope.Reset();
        _slowEnvelope.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float peak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                peak = MathF.Max(peak, MathF.Abs(samples[index + channel]));
            }

            float fast = _fastEnvelope.Process(peak);
            float slow = _slowEnvelope.Process(peak);
            float sensitivity = Sensitivity.NextValue();
            float transientRatio = slow > 0.000001f
                ? Math.Clamp(((fast - slow) / slow) * sensitivity, -1f, 1f)
                : 0f;
            float attackDb = AttackDecibels.NextValue() * MathF.Max(0f, transientRatio);
            float bodyAmount = Math.Clamp(slow * 2f, 0f, 1f);
            float sustainDb = SustainDecibels.NextValue() * bodyAmount;
            float gain = AudioMath.DecibelsToLinear(attackDb + sustainDb);
            float mix = Math.Clamp(Mix.NextValue(), 0f, 1f);

            for (int channel = 0; channel < channels; channel++)
            {
                float dry = samples[index];
                float wet = dry * gain;
                samples[index++] = dry + ((wet - dry) * mix);
            }
        }
    }
}

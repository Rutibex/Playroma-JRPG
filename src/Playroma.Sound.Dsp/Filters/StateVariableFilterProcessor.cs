using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Filters;

/// <summary>
/// Topology-preserving-transform state-variable filter with simultaneous low,
/// band, high, and notch responses.
/// </summary>
public sealed class StateVariableFilterProcessor : AudioProcessorBase
{
    private float[] _integratorOne = [];
    private float[] _integratorTwo = [];

    public StateVariableFilterProcessor(StateVariableFilterMode mode = StateVariableFilterMode.LowPass)
    {
        Mode = mode;
        Frequency = new AudioParameter(
            new ParameterMetadata("frequency", "Frequency", "Hz", 10f, 40_000f, 1_500f, ParameterScale.Logarithmic));
        Resonance = new AudioParameter(
            new ParameterMetadata("resonance", "Resonance", string.Empty, 0.1f, 20f, 0.7071068f, ParameterScale.Logarithmic));
        Drive = new AudioParameter(
            new ParameterMetadata("drive", "Drive", string.Empty, 0f, 4f, 0f));
    }

    public StateVariableFilterMode Mode { get; set; }
    public AudioParameter Frequency { get; }
    public AudioParameter Resonance { get; }
    public AudioParameter Drive { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        _integratorOne = new float[format.Channels];
        _integratorTwo = new float[format.Channels];
        Frequency.Prepare(format.SampleRate);
        Resonance.Prepare(format.SampleRate);
        Drive.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_integratorOne);
        Array.Clear(_integratorTwo);
        Frequency.Reset();
        Resonance.Reset();
        Drive.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float frequency = Math.Clamp(Frequency.NextValue(), 10f, Format.SampleRate * 0.49f);
            float resonance = Math.Clamp(Resonance.NextValue(), 0.1f, 20f);
            float drive = Math.Clamp(Drive.NextValue(), 0f, 4f);
            float g = MathF.Tan(MathF.PI * frequency / Format.SampleRate);
            float k = 1f / resonance;
            float a1 = 1f / (1f + (g * (g + k)));
            float a2 = g * a1;
            float a3 = g * a2;

            for (int channel = 0; channel < channels; channel++)
            {
                float input = samples[index];
                if (drive > 0f)
                {
                    input = MathF.Tanh(input * (1f + drive));
                }

                float v3 = input - _integratorTwo[channel];
                float band = (a1 * _integratorOne[channel]) + (a2 * v3);
                float low = _integratorTwo[channel] + (a2 * _integratorOne[channel]) + (a3 * v3);
                _integratorOne[channel] = (2f * band) - _integratorOne[channel];
                _integratorTwo[channel] = (2f * low) - _integratorTwo[channel];
                float high = input - (k * band) - low;

                float output = Mode switch
                {
                    StateVariableFilterMode.HighPass => high,
                    StateVariableFilterMode.BandPass => band,
                    StateVariableFilterMode.Notch => high + low,
                    _ => low
                };

                samples[index++] = float.IsFinite(output) ? output : 0f;
            }
        }
    }
}

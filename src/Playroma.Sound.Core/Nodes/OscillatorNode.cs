using Playroma.Sound.Core.Generation;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Band-limited periodic oscillator using PolyBLEP correction for discontinuous waveforms.
/// </summary>
public sealed class OscillatorNode : AudioNodeBase
{
    private double _phase;
    private float _triangleState;

    public OscillatorNode(
        Waveform waveform = Waveform.Sine,
        double frequencyHz = 440.0,
        float amplitude = 1f,
        double initialPhase = 0.0,
        float pulseWidth = 0.5f)
    {
        Waveform = waveform;
        FrequencyHz = frequencyHz;
        Amplitude = amplitude;
        InitialPhase = initialPhase;
        PulseWidth = pulseWidth;
    }

    public Waveform Waveform { get; set; }
    public double FrequencyHz { get; set; }
    public float Amplitude { get; set; }
    public double InitialPhase { get; set; }
    public float PulseWidth { get; set; }

    public override void Reset()
    {
        _phase = WrapPhase(InitialPhase);
        _triangleState = 0f;
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        double nyquistSafeFrequency = Math.Clamp(FrequencyHz, 0.0, Format.SampleRate * 0.49);
        double phaseIncrement = nyquistSafeFrequency / Format.SampleRate;
        float amplitude = Amplitude;
        float pulseWidth = Math.Clamp(PulseWidth, 0.01f, 0.99f);
        int channels = Format.Channels;
        int sampleIndex = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float value = GenerateSample(_phase, phaseIncrement, pulseWidth) * amplitude;

            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] = value;
            }

            _phase += phaseIncrement;
            _phase -= Math.Floor(_phase);
        }
    }

    private float GenerateSample(double phase, double phaseIncrement, float pulseWidth)
    {
        if (phaseIncrement <= 0.0)
        {
            return 0f;
        }

        return Waveform switch
        {
            Waveform.Sine => MathF.Sin((float)(phase * Math.Tau)),
            Waveform.Saw => Saw(phase, phaseIncrement),
            Waveform.Square => Square(phase, phaseIncrement, 0.5),
            Waveform.Pulse => Square(phase, phaseIncrement, pulseWidth),
            Waveform.Triangle => Triangle(phase, phaseIncrement),
            _ => 0f
        };
    }

    private static float Saw(double phase, double phaseIncrement)
    {
        double value = (2.0 * phase) - 1.0;
        value -= PolyBlep(phase, phaseIncrement);
        return (float)value;
    }

    private static float Square(double phase, double phaseIncrement, double width)
    {
        double value = phase < width ? 1.0 : -1.0;
        value += PolyBlep(phase, phaseIncrement);

        double fallingPhase = phase - width;
        if (fallingPhase < 0.0)
        {
            fallingPhase += 1.0;
        }

        value -= PolyBlep(fallingPhase, phaseIncrement);
        return (float)value;
    }

    private float Triangle(double phase, double phaseIncrement)
    {
        float square = Square(phase, phaseIncrement, 0.5);
        _triangleState += (float)(4.0 * phaseIncrement) * square;
        _triangleState *= 0.9995f;
        return Math.Clamp(_triangleState, -1f, 1f);
    }

    private static double PolyBlep(double phase, double phaseIncrement)
    {
        if (phase < phaseIncrement)
        {
            double t = phase / phaseIncrement;
            return t + t - (t * t) - 1.0;
        }

        if (phase > 1.0 - phaseIncrement)
        {
            double t = (phase - 1.0) / phaseIncrement;
            return (t * t) + t + t + 1.0;
        }

        return 0.0;
    }

    private static double WrapPhase(double phase)
    {
        phase -= Math.Floor(phase);
        return phase < 0.0 ? phase + 1.0 : phase;
    }
}

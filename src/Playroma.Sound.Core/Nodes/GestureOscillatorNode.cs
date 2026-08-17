using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// A finite, allocation-free oscillator with pitch contour, amplitude contour, vibrato,
/// tremolo, organic drift, optional quantization, and PolyBLEP waveforms.
/// </summary>
public sealed class GestureOscillatorNode : AudioNodeBase
{
    private readonly ulong _seed;
    private double _phase;
    private float _triangleState;
    private long _elapsedFrames;
    private long _durationFrames;
    private double _driftPhaseA;
    private double _driftPhaseB;

    public GestureOscillatorNode(
        Waveform waveform,
        double rootFrequencyHz,
        float amplitude,
        double durationSeconds,
        GestureCurve pitchContour,
        GestureCurve amplitudeContour,
        ulong seed = 1)
    {
        Waveform = waveform;
        RootFrequencyHz = rootFrequencyHz;
        Amplitude = amplitude;
        DurationSeconds = durationSeconds;
        PitchContour = pitchContour ?? throw new ArgumentNullException(nameof(pitchContour));
        AmplitudeContour = amplitudeContour ?? throw new ArgumentNullException(nameof(amplitudeContour));
        _seed = seed;
    }

    public Waveform Waveform { get; set; }
    public double RootFrequencyHz { get; set; }
    public float Amplitude { get; set; }
    public double DurationSeconds { get; }
    public GestureCurve PitchContour { get; }
    public GestureCurve AmplitudeContour { get; }
    public float PulseWidth { get; set; } = 0.5f;
    public double AttackSeconds { get; set; } = 0.002;
    public double ReleaseSeconds { get; set; } = 0.05;
    public float VibratoDepthSemitones { get; set; }
    public float VibratoRateHz { get; set; } = 5.5f;
    public double VibratoDelaySeconds { get; set; }
    public float TremoloDepth { get; set; }
    public float TremoloRateHz { get; set; } = 5f;
    public float DriftDepthSemitones { get; set; }
    public float DriftRateHz { get; set; } = 1.4f;
    public PitchQuantizationMode Quantization { get; set; }

    public override bool IsComplete => _durationFrames > 0 && _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _durationFrames = DurationSeconds <= 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(DurationSeconds * format.SampleRate)));
    }

    public override void Reset()
    {
        _phase = 0;
        _triangleState = 0;
        _elapsedFrames = 0;
        _driftPhaseA = SeedPhase(_seed, 0x9E3779B97F4A7C15UL);
        _driftPhaseB = SeedPhase(_seed, 0xD1B54A32D192ED03UL);
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete) return;

        int channels = Format.Channels;
        int sampleIndex = 0;
        double sampleRate = Format.SampleRate;
        float pulseWidth = Math.Clamp(PulseWidth, 0.01f, 0.99f);
        float tremoloDepth = Math.Clamp(TremoloDepth, 0, 1);
        double attackFrames = Math.Max(0, AttackSeconds * sampleRate);
        double releaseFrames = Math.Max(0, ReleaseSeconds * sampleRate);

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            double elapsedSeconds = _elapsedFrames / sampleRate;
            double position = _durationFrames <= 1
                ? 0
                : Math.Clamp(_elapsedFrames / (double)(_durationFrames - 1), 0, 1);

            double contourSemitones = PitchContour.Evaluate(position);
            double drift = DriftDepthSemitones * (
                Math.Sin((Math.Tau * DriftRateHz * elapsedSeconds) + _driftPhaseA) * 0.67 +
                Math.Sin((Math.Tau * DriftRateHz * 0.431 * elapsedSeconds) + _driftPhaseB) * 0.33);
            double quantized = Quantize(contourSemitones + drift, Quantization);

            double vibrato = 0;
            if (VibratoDepthSemitones != 0 && elapsedSeconds >= VibratoDelaySeconds)
            {
                double fade = Math.Clamp((elapsedSeconds - VibratoDelaySeconds) * 8, 0, 1);
                vibrato = VibratoDepthSemitones * fade * Math.Sin(Math.Tau * VibratoRateHz * elapsedSeconds);
            }

            double semitones = quantized + vibrato;
            double frequency = Math.Clamp(RootFrequencyHz * Math.Pow(2, semitones / 12), 0, sampleRate * 0.49);
            double phaseIncrement = frequency / sampleRate;

            double envelope = AmplitudeContour.Evaluate(position);
            if (attackFrames > 0 && _elapsedFrames < attackFrames)
                envelope *= _elapsedFrames / attackFrames;
            if (_durationFrames > 0 && releaseFrames > 0 && _elapsedFrames > _durationFrames - releaseFrames)
                envelope *= Math.Clamp((_durationFrames - _elapsedFrames) / releaseFrames, 0, 1);

            double tremolo = tremoloDepth <= 0
                ? 1
                : 1 - (tremoloDepth * 0.5) + (Math.Sin(Math.Tau * TremoloRateHz * elapsedSeconds) * tremoloDepth * 0.5);
            float value = GenerateSample(_phase, phaseIncrement, pulseWidth) * Amplitude * (float)(envelope * tremolo);

            for (int channel = 0; channel < channels; channel++) output[sampleIndex++] = value;

            _phase += phaseIncrement;
            _phase -= Math.Floor(_phase);
            _elapsedFrames++;
        }
    }

    private float GenerateSample(double phase, double phaseIncrement, float pulseWidth)
    {
        if (phaseIncrement <= 0) return 0;
        return Waveform switch
        {
            Waveform.Sine => MathF.Sin((float)(phase * Math.Tau)),
            Waveform.Saw => Saw(phase, phaseIncrement),
            Waveform.Square => Square(phase, phaseIncrement, 0.5),
            Waveform.Pulse => Square(phase, phaseIncrement, pulseWidth),
            Waveform.Triangle => Triangle(phase, phaseIncrement),
            _ => 0
        };
    }

    private static float Saw(double phase, double increment)
    {
        double value = (2 * phase) - 1;
        value -= PolyBlep(phase, increment);
        return (float)value;
    }

    private static float Square(double phase, double increment, double width)
    {
        double value = phase < width ? 1 : -1;
        value += PolyBlep(phase, increment);
        double falling = phase - width;
        if (falling < 0) falling += 1;
        value -= PolyBlep(falling, increment);
        return (float)value;
    }

    private float Triangle(double phase, double increment)
    {
        float square = Square(phase, increment, 0.5);
        _triangleState += (float)(4 * increment) * square;
        _triangleState *= 0.9995f;
        return Math.Clamp(_triangleState, -1, 1);
    }

    private static double PolyBlep(double phase, double increment)
    {
        if (phase < increment)
        {
            double t = phase / increment;
            return t + t - (t * t) - 1;
        }
        if (phase > 1 - increment)
        {
            double t = (phase - 1) / increment;
            return (t * t) + t + t + 1;
        }
        return 0;
    }

    private static double Quantize(double semitones, PitchQuantizationMode mode)
    {
        double step = mode switch
        {
            PitchQuantizationMode.QuarterTone => 0.5,
            PitchQuantizationMode.Chromatic => 1,
            PitchQuantizationMode.WholeTone => 2,
            _ => 0
        };
        return step <= 0 ? semitones : Math.Round(semitones / step) * step;
    }

    private static double SeedPhase(ulong seed, ulong salt)
    {
        ulong value;
        unchecked
        {
            value = seed ^ salt;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
        }

        return (value / (double)ulong.MaxValue) * Math.Tau;
    }
}

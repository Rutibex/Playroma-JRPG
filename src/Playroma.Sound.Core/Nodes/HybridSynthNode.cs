using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;
using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// A finite, deterministic source that collects the most useful compact synthesis
/// techniques behind one graph node: conventional oscillation, FM, additive,
/// wavetable morphing, Karplus-Strong, modal resonance, synthetic granular clouds,
/// formant tones, one-bit sound, and resonated noise.
/// </summary>
public sealed class HybridSynthNode : AudioNodeBase
{
    private Pcg32 _random;
    private double _phase;
    private double _modulatorPhase;
    private double[] _partialPhases = [];
    private float[] _delay = [];
    private int _delayIndex;
    private float[] _modeStateA = [];
    private float[] _modeStateB = [];
    private double[] _grainPhases = [];
    private double[] _grainRates = [];
    private float[] _grainLevels = [];
    private long _elapsedFrames;
    private long _durationFrames;
    private float _noiseState;

    public HybridSynthNode(
        HybridSynthesisMode mode,
        Waveform waveform,
        double frequencyHz,
        float amplitude,
        double durationSeconds,
        ulong seed = 1)
    {
        Mode = mode;
        Waveform = waveform;
        FrequencyHz = frequencyHz;
        Amplitude = amplitude;
        DurationSeconds = durationSeconds;
        Seed = seed;
    }

    public HybridSynthesisMode Mode { get; set; }
    public Waveform Waveform { get; set; }
    public double FrequencyHz { get; set; }
    public float Amplitude { get; set; }
    public double DurationSeconds { get; }
    public ulong Seed { get; set; }
    public double AttackSeconds { get; set; } = 0.002;
    public double ReleaseSeconds { get; set; } = 0.08;
    public float Brightness { get; set; } = 0.55f;
    public float Timbre { get; set; } = 0.5f;
    public float Modulation { get; set; } = 0.35f;
    public float Feedback { get; set; } = 0.25f;
    public int Harmonics { get; set; } = 8;
    public float Damping { get; set; } = 0.45f;
    public float Spread { get; set; } = 0.2f;
    public float PulseWidth { get; set; } = 0.5f;
    public float PitchSweepSemitones { get; set; }

    public override bool IsComplete => _durationFrames > 0 && _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _durationFrames = DurationSeconds <= 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(DurationSeconds * format.SampleRate)));

        int count = Math.Clamp(Harmonics, 1, 32);
        _partialPhases = new double[count];
        _modeStateA = new float[count];
        _modeStateB = new float[count];
        int grainCount = Math.Clamp(3 + (int)Math.Round(Spread * 21), 3, 24);
        _grainPhases = new double[grainCount];
        _grainRates = new double[grainCount];
        _grainLevels = new float[grainCount];

        int delayLength = Math.Clamp((int)Math.Round(format.SampleRate / Math.Max(20, FrequencyHz)), 2, format.SampleRate * 2);
        _delay = new float[delayLength];
    }

    public override void Reset()
    {
        _random = new Pcg32(Seed == 0 ? 1UL : Seed);
        _phase = 0;
        _modulatorPhase = 0;
        _delayIndex = 0;
        _elapsedFrames = 0;
        _noiseState = 0;
        Array.Clear(_partialPhases);
        Array.Clear(_modeStateA);
        Array.Clear(_modeStateB);

        for (int index = 0; index < _delay.Length; index++)
            _delay[index] = _random.NextSignedFloat() * (0.45f + (Brightness * 0.5f));
        for (int index = 0; index < _grainPhases.Length; index++)
        {
            _grainPhases[index] = _random.NextDouble();
            _grainRates[index] = Math.Pow(2, ((_random.NextDouble() * 2 - 1) * Spread * 18) / 12);
            _grainLevels[index] = 0.35f + (_random.NextFloat() * 0.65f);
        }
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete) return;

        int channels = Format.Channels;
        int sampleIndex = 0;
        double sampleRate = Format.SampleRate;
        double attackFrames = Math.Max(0, AttackSeconds * sampleRate);
        double releaseFrames = Math.Max(0, ReleaseSeconds * sampleRate);

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            double position = _durationFrames <= 1 ? 0 : Math.Clamp(_elapsedFrames / (double)(_durationFrames - 1), 0, 1);
            double sweep = PitchSweepSemitones * position;
            double frequency = Math.Clamp(FrequencyHz * Math.Pow(2, sweep / 12), 5, sampleRate * 0.45);
            float value = Mode switch
            {
                HybridSynthesisMode.Basic => RenderBasic(frequency, sampleRate),
                HybridSynthesisMode.FrequencyModulation => RenderFm(frequency, sampleRate),
                HybridSynthesisMode.Additive => RenderAdditive(frequency, sampleRate),
                HybridSynthesisMode.Wavetable => RenderWavetable(frequency, sampleRate),
                HybridSynthesisMode.KarplusStrong => RenderKarplusStrong(),
                HybridSynthesisMode.Modal => RenderModal(frequency, sampleRate, position),
                HybridSynthesisMode.Granular => RenderGranular(frequency, sampleRate, position),
                HybridSynthesisMode.Formant => RenderFormant(frequency, sampleRate),
                HybridSynthesisMode.OneBit => RenderOneBit(frequency, sampleRate),
                HybridSynthesisMode.NoiseResonator => RenderNoiseResonator(frequency, sampleRate),
                _ => 0f
            };

            double envelope = 1;
            if (attackFrames > 0 && _elapsedFrames < attackFrames) envelope *= _elapsedFrames / attackFrames;
            if (_durationFrames > 0 && releaseFrames > 0 && _elapsedFrames > _durationFrames - releaseFrames)
                envelope *= Math.Clamp((_durationFrames - _elapsedFrames) / releaseFrames, 0, 1);
            value *= Amplitude * (float)envelope;

            for (int channel = 0; channel < channels; channel++) output[sampleIndex++] = value;
            _elapsedFrames++;
        }
    }

    private float RenderBasic(double frequency, double sampleRate)
    {
        double increment = frequency / sampleRate;
        float value = Wave(_phase, increment, Math.Clamp(PulseWidth, 0.01f, 0.99f), Waveform);
        Advance(ref _phase, increment);
        return value;
    }

    private float RenderFm(double frequency, double sampleRate)
    {
        double ratio = 0.25 + (Timbre * 7.75);
        double modFrequency = frequency * ratio;
        double index = Modulation * (1.5 + (Brightness * 14));
        double feedback = Feedback * Math.Sin(_phase * Math.Tau);
        double phase = _phase + (Math.Sin(_modulatorPhase * Math.Tau) * index / Math.Tau) + (feedback * 0.08);
        float value = MathF.Sin((float)(phase * Math.Tau));
        Advance(ref _phase, frequency / sampleRate);
        Advance(ref _modulatorPhase, modFrequency / sampleRate);
        return value;
    }

    private float RenderAdditive(double frequency, double sampleRate)
    {
        int count = Math.Min(_partialPhases.Length, Math.Clamp(Harmonics, 1, 32));
        double falloff = 0.55 + ((1 - Brightness) * 2.6);
        double stretch = 1 + ((Timbre - 0.5) * 0.08);
        double sum = 0;
        double normalization = 0;
        for (int index = 0; index < count; index++)
        {
            double harmonic = Math.Pow(index + 1, stretch);
            double level = 1 / Math.Pow(index + 1, falloff);
            if (Timbre < 0.28f && (index & 1) == 1) level *= 0.12;
            if (Timbre > 0.72f && (index & 1) == 0) level *= 0.45;
            sum += Math.Sin(_partialPhases[index] * Math.Tau) * level;
            normalization += level;
            Advance(ref _partialPhases[index], frequency * harmonic / sampleRate);
        }
        return normalization <= 0 ? 0 : (float)(sum / normalization);
    }

    private float RenderWavetable(double frequency, double sampleRate)
    {
        double increment = frequency / sampleRate;
        float sine = MathF.Sin((float)(_phase * Math.Tau));
        float triangle = (float)(1 - (4 * Math.Abs(_phase - 0.5)));
        float saw = (float)((2 * _phase) - 1);
        float pulse = _phase < Math.Clamp(PulseWidth, 0.01f, 0.99f) ? 1f : -1f;
        float a = Lerp(sine, triangle, Math.Clamp(Timbre * 2, 0, 1));
        float b = Lerp(saw, pulse, Math.Clamp((Timbre - 0.5f) * 2, 0, 1));
        float value = Lerp(a, b, Brightness);
        Advance(ref _phase, increment);
        return value;
    }

    private float RenderKarplusStrong()
    {
        if (_delay.Length == 0) return 0;
        int next = (_delayIndex + 1) % _delay.Length;
        float current = _delay[_delayIndex];
        float averaged = (current + _delay[next]) * 0.5f;
        float loss = 0.985f - (Math.Clamp(Damping, 0, 1) * 0.08f);
        float excitation = _elapsedFrames < 2 ? _random.NextSignedFloat() * Modulation * 0.2f : 0;
        _delay[_delayIndex] = (averaged * loss) + excitation;
        _delayIndex = next;
        return current;
    }

    private float RenderModal(double frequency, double sampleRate, double position)
    {
        int count = Math.Min(_modeStateA.Length, Math.Clamp(Harmonics, 2, 24));
        float impulse = _elapsedFrames == 0 ? 1 : (_elapsedFrames < 8 ? _random.NextSignedFloat() * 0.15f : 0);
        double sum = 0;
        for (int index = 0; index < count; index++)
        {
            double inharmonic = 1 + (Timbre * index * 0.045);
            double modeFrequency = Math.Min(sampleRate * 0.45, frequency * (index + 1) * inharmonic);
            double radius = Math.Clamp(0.9996 - (Damping * 0.012) - (index * 0.00045), 0.85, 0.9998);
            double coefficient = 2 * radius * Math.Cos(Math.Tau * modeFrequency / sampleRate);
            float next = (float)((impulse / (index + 1.0)) + (coefficient * _modeStateA[index]) - (radius * radius * _modeStateB[index]));
            _modeStateB[index] = _modeStateA[index];
            _modeStateA[index] = next;
            sum += next / Math.Pow(index + 1, 0.55 + (1 - Brightness));
        }
        return (float)Math.Clamp(sum * (0.16 + ((1 - position) * 0.12)), -1.5, 1.5);
    }

    private float RenderGranular(double frequency, double sampleRate, double position)
    {
        double sum = 0;
        for (int index = 0; index < _grainPhases.Length; index++)
        {
            double local = (_grainPhases[index] + (position * (1 + (index * 0.071)))) % 1;
            double window = 0.5 - (0.5 * Math.Cos(Math.Tau * local));
            double detune = _grainRates[index];
            sum += Math.Sin(_grainPhases[index] * Math.Tau) * window * _grainLevels[index];
            Advance(ref _grainPhases[index], frequency * detune / sampleRate);
            if (local > 0.995 && _random.NextFloat() < Modulation)
                _grainRates[index] = Math.Pow(2, ((_random.NextDouble() * 2 - 1) * Spread * 24) / 12);
        }
        return (float)(sum / Math.Max(1, _grainPhases.Length * 0.48));
    }

    private float RenderFormant(double frequency, double sampleRate)
    {
        double increment = frequency / sampleRate;
        double source = Wave(_phase, increment, 0.42f, Waveform);
        double vowel = Timbre;
        double f1 = 300 + (vowel * 650);
        double f2 = 900 + ((1 - vowel) * 1_550);
        double f3 = 2_400 + (Brightness * 900);
        double envelope = 0.5 + (0.5 * source);
        double value = source * 0.32
            + Math.Sin(_partialPhases[0] * Math.Tau) * envelope * 0.34
            + Math.Sin(_partialPhases[Math.Min(1, _partialPhases.Length - 1)] * Math.Tau) * envelope * 0.22
            + Math.Sin(_partialPhases[Math.Min(2, _partialPhases.Length - 1)] * Math.Tau) * envelope * 0.12;
        Advance(ref _phase, increment);
        Advance(ref _partialPhases[0], f1 / sampleRate);
        if (_partialPhases.Length > 1) Advance(ref _partialPhases[1], f2 / sampleRate);
        if (_partialPhases.Length > 2) Advance(ref _partialPhases[2], f3 / sampleRate);
        return (float)value;
    }

    private float RenderOneBit(double frequency, double sampleRate)
    {
        double increment = frequency / sampleRate;
        bool gate = _phase < Math.Clamp(PulseWidth, 0.02f, 0.98f);
        float value = gate ? 1f : -1f;
        if (Modulation > 0.15f)
        {
            int divider = Math.Max(1, (int)Math.Round(1 + (Modulation * 15)));
            if ((_elapsedFrames / divider & 1) != 0) value = -value;
        }
        Advance(ref _phase, increment);
        return value;
    }

    private float RenderNoiseResonator(double frequency, double sampleRate)
    {
        float white = _random.NextSignedFloat();
        float damping = 0.01f + (Damping * 0.18f);
        _noiseState += (white - _noiseState) * damping;
        float high = white - _noiseState;
        double resonance = Math.Sin(_phase * Math.Tau);
        float value = Lerp(_noiseState, high, Brightness) * (0.35f + (0.65f * Math.Abs((float)resonance)));
        Advance(ref _phase, frequency / sampleRate);
        return value;
    }

    private float Wave(double phase, double increment, float pulseWidth, Waveform waveform) => waveform switch
    {
        Waveform.Sine => MathF.Sin((float)(phase * Math.Tau)),
        Waveform.Triangle => (float)(1 - (4 * Math.Abs(phase - 0.5))),
        Waveform.Saw => (float)((2 * phase) - 1),
        Waveform.Square => phase < 0.5 ? 1f : -1f,
        Waveform.Pulse => phase < pulseWidth ? 1f : -1f,
        _ => 0f
    };

    private static void Advance(ref double phase, double increment)
    {
        phase += increment;
        phase -= Math.Floor(phase);
    }

    private static float Lerp(float a, float b, float amount) => a + ((b - a) * Math.Clamp(amount, 0, 1));
}

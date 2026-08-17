using System.Globalization;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Compact deterministic polyphonic note sequencer. The program format is a
/// semicolon-separated list of start|duration|semitones|amplitude entries.
/// Each event can use the same hybrid synthesis families as a one-shot source,
/// so phrases are not limited to plain oscillator notes.
/// </summary>
public sealed class ToneSequenceNode : AudioNodeBase
{
    private readonly SequenceEvent[] _events;
    private RuntimeEvent[] _runtime = [];
    private long _elapsedFrames;
    private long _durationFrames;

    public ToneSequenceNode(
        string program,
        HybridSynthesisMode synthesisMode,
        Waveform waveform,
        double rootFrequencyHz,
        float amplitude,
        double attackSeconds,
        double releaseSeconds,
        float pulseWidth = 0.5f)
    {
        Program = program ?? string.Empty;
        SynthesisMode = synthesisMode;
        Waveform = waveform;
        RootFrequencyHz = rootFrequencyHz;
        Amplitude = amplitude;
        AttackSeconds = attackSeconds;
        ReleaseSeconds = releaseSeconds;
        PulseWidth = pulseWidth;
        _events = ParseProgram(Program);
    }

    public string Program { get; }
    public HybridSynthesisMode SynthesisMode { get; set; }
    public Waveform Waveform { get; set; }
    public double RootFrequencyHz { get; set; }
    public float Amplitude { get; set; }
    public double AttackSeconds { get; set; }
    public double ReleaseSeconds { get; set; }
    public float PulseWidth { get; set; }
    public float VibratoDepthSemitones { get; set; }
    public float VibratoRateHz { get; set; } = 5.5f;
    public float QuantizeSemitones { get; set; }
    public float Brightness { get; set; } = 0.55f;
    public float Timbre { get; set; } = 0.5f;
    public float Modulation { get; set; } = 0.35f;
    public float Damping { get; set; } = 0.45f;
    public int Harmonics { get; set; } = 8;
    public int EventCount => _events.Length;
    public override bool IsComplete => _durationFrames == 0 || _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _runtime = new RuntimeEvent[_events.Length];
        long maximum = 0;
        for (int index = 0; index < _events.Length; index++)
        {
            SequenceEvent item = _events[index];
            long start = Math.Max(0, (long)Math.Round(item.StartSeconds * format.SampleRate));
            long duration = Math.Max(1, (long)Math.Round(item.DurationSeconds * format.SampleRate));
            _runtime[index] = new RuntimeEvent(start, duration, item.Semitones, item.Amplitude, 0);
            maximum = Math.Max(maximum, start + duration);
        }
        _durationFrames = maximum;
    }

    public override void Reset()
    {
        _elapsedFrames = 0;
        for (int index = 0; index < _runtime.Length; index++)
            _runtime[index] = _runtime[index] with { Phase = 0 };
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete) return;
        int channels = Format.Channels;
        int sampleIndex = 0;
        double sampleRate = Format.SampleRate;
        long attackFrames = Math.Max(0, (long)Math.Round(AttackSeconds * sampleRate));
        long releaseFrames = Math.Max(0, (long)Math.Round(ReleaseSeconds * sampleRate));

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            double sum = 0;
            int active = 0;
            double seconds = _elapsedFrames / sampleRate;
            for (int index = 0; index < _runtime.Length; index++)
            {
                RuntimeEvent item = _runtime[index];
                long local = _elapsedFrames - item.StartFrame;
                if (local < 0 || local >= item.DurationFrames) continue;

                double semitones = item.Semitones;
                if (QuantizeSemitones > 0) semitones = Math.Round(semitones / QuantizeSemitones) * QuantizeSemitones;
                if (VibratoDepthSemitones != 0)
                    semitones += Math.Sin(Math.Tau * VibratoRateHz * seconds) * VibratoDepthSemitones;
                double frequency = Math.Clamp(RootFrequencyHz * Math.Pow(2, semitones / 12), 5, sampleRate * 0.45);
                double increment = frequency / sampleRate;
                double envelope = 1;
                if (attackFrames > 0 && local < attackFrames) envelope *= local / (double)attackFrames;
                if (releaseFrames > 0 && local > item.DurationFrames - releaseFrames)
                    envelope *= Math.Clamp((item.DurationFrames - local) / (double)releaseFrames, 0, 1);

                float voice = RenderVoice(item.Phase, index, local, item.DurationFrames, frequency, sampleRate);
                sum += voice * item.Amplitude * envelope;
                item = item with { Phase = Wrap(item.Phase + increment) };
                _runtime[index] = item;
                active++;
            }

            float value = active == 0 ? 0 : (float)(sum / Math.Sqrt(active)) * Amplitude;
            for (int channel = 0; channel < channels; channel++) output[sampleIndex++] = value;
            _elapsedFrames++;
        }
    }

    private float RenderVoice(double phase, int eventIndex, long localFrame, long durationFrames, double frequency, double sampleRate)
    {
        double position = durationFrames <= 1 ? 0 : localFrame / (double)(durationFrames - 1);
        double localSeconds = localFrame / sampleRate;
        return SynthesisMode switch
        {
            HybridSynthesisMode.Basic => Wave(phase, Math.Clamp(PulseWidth, 0.01f, 0.99f)),
            HybridSynthesisMode.FrequencyModulation => RenderFm(phase),
            HybridSynthesisMode.Additive => RenderAdditive(phase),
            HybridSynthesisMode.Wavetable => RenderWavetable(phase),
            HybridSynthesisMode.KarplusStrong => RenderPlucked(phase, eventIndex, localFrame, position),
            HybridSynthesisMode.Modal => RenderModal(phase, eventIndex, localFrame, position),
            HybridSynthesisMode.Granular => RenderGranular(phase, eventIndex, localFrame, position),
            HybridSynthesisMode.Formant => RenderFormant(phase, localSeconds, frequency),
            HybridSynthesisMode.OneBit => phase < Math.Clamp(PulseWidth, 0.02f, 0.98f) ? 1f : -1f,
            HybridSynthesisMode.NoiseResonator => RenderNoiseResonator(phase, eventIndex, localFrame),
            _ => 0
        };
    }

    private float RenderFm(double phase)
    {
        double ratio = 0.25 + (Timbre * 7.75);
        double index = Modulation * (1.5 + (Brightness * 12));
        double modulator = Math.Sin(phase * ratio * Math.Tau);
        return MathF.Sin((float)((phase * Math.Tau) + (modulator * index)));
    }

    private float RenderAdditive(double phase)
    {
        int count = Math.Clamp(Harmonics, 1, 16);
        double falloff = 0.65 + ((1 - Brightness) * 2.35);
        double stretch = 1 + ((Timbre - 0.5) * 0.08);
        double sum = 0;
        double normalization = 0;
        for (int index = 0; index < count; index++)
        {
            double harmonic = Math.Pow(index + 1, stretch);
            double level = 1 / Math.Pow(index + 1, falloff);
            if (Timbre < 0.28f && (index & 1) == 1) level *= 0.14;
            if (Timbre > 0.72f && (index & 1) == 0) level *= 0.48;
            sum += Math.Sin(phase * harmonic * Math.Tau) * level;
            normalization += level;
        }
        return normalization <= 0 ? 0 : (float)(sum / normalization);
    }

    private float RenderWavetable(double phase)
    {
        float sine = MathF.Sin((float)(phase * Math.Tau));
        float triangle = (float)(1 - (4 * Math.Abs(phase - 0.5)));
        float saw = (float)((2 * phase) - 1);
        float pulse = phase < Math.Clamp(PulseWidth, 0.01f, 0.99f) ? 1f : -1f;
        float a = Lerp(sine, triangle, Math.Clamp(Timbre * 2, 0, 1));
        float b = Lerp(saw, pulse, Math.Clamp((Timbre - 0.5f) * 2, 0, 1));
        return Lerp(a, b, Brightness);
    }

    private float RenderPlucked(double phase, int eventIndex, long localFrame, double position)
    {
        float attackNoise = localFrame < 12 ? HashNoise(localFrame, eventIndex) * (0.18f + Modulation * 0.45f) : 0;
        double decay = Math.Exp(-position * (3 + Damping * 14));
        double body = Math.Sin(phase * Math.Tau)
            + Math.Sin(phase * Math.Tau * 2.01) * 0.42
            + Math.Sin(phase * Math.Tau * (3.95 + Timbre * 0.18)) * 0.2;
        return (float)(body * 0.58 * decay) + attackNoise;
    }

    private float RenderModal(double phase, int eventIndex, long localFrame, double position)
    {
        float impulse = localFrame < 8 ? HashNoise(localFrame, eventIndex) * 0.22f : 0;
        double decay = Math.Exp(-position * (2 + Damping * 11));
        double stretch = 0.02 + Timbre * 0.12;
        double sum = 0;
        double normalization = 0;
        int count = Math.Clamp(Harmonics, 2, 12);
        for (int index = 0; index < count; index++)
        {
            double ratio = (index + 1) * (1 + index * stretch);
            double level = 1 / Math.Pow(index + 1, 0.7 + (1 - Brightness));
            sum += Math.Sin(phase * ratio * Math.Tau) * level;
            normalization += level;
        }
        return normalization <= 0 ? impulse : (float)(sum / normalization * decay) + impulse;
    }

    private float RenderGranular(double phase, int eventIndex, long localFrame, double position)
    {
        double grainRate = 7 + (Modulation * 54);
        double grainPhase = (position * grainRate + eventIndex * 0.173) % 1;
        double window = 0.5 - (0.5 * Math.Cos(Math.Tau * grainPhase));
        double jitter = HashNoise(localFrame / 24, eventIndex) * Modulation * 0.16;
        double carrier = Math.Sin((phase + jitter) * Math.Tau)
            + Math.Sin((phase * (1.5 + Timbre * 2.5) - jitter) * Math.Tau) * 0.32;
        return (float)(carrier * window * 0.76);
    }

    private float RenderFormant(double phase, double localSeconds, double frequency)
    {
        double source = Wave(phase, 0.42f);
        double vowel = Timbre;
        double f1 = 300 + (vowel * 650);
        double f2 = 900 + ((1 - vowel) * 1_550);
        double f3 = 2_400 + (Brightness * 900);
        double formant1 = Math.Sin(Math.Tau * f1 * localSeconds);
        double formant2 = Math.Sin(Math.Tau * f2 * localSeconds);
        double formant3 = Math.Sin(Math.Tau * f3 * localSeconds);
        double tracking = Math.Clamp(220 / Math.Max(40, frequency), 0.25, 1.4);
        return (float)((source * 0.36) + ((formant1 * 0.3 + formant2 * 0.2 + formant3 * 0.1) * tracking));
    }

    private float RenderNoiseResonator(double phase, int eventIndex, long localFrame)
    {
        float noise = HashNoise(localFrame, eventIndex);
        float resonance = 0.25f + (0.75f * Math.Abs(MathF.Sin((float)(phase * Math.Tau))));
        float color = Lerp(noise, noise * noise * MathF.Sign(noise), Brightness);
        return color * resonance;
    }

    private float Wave(double phase, float pulseWidth) => Waveform switch
    {
        Waveform.Sine => MathF.Sin((float)(phase * Math.Tau)),
        Waveform.Triangle => (float)(1 - (4 * Math.Abs(phase - 0.5))),
        Waveform.Saw => (float)((2 * phase) - 1),
        Waveform.Square => phase < 0.5 ? 1f : -1f,
        Waveform.Pulse => phase < pulseWidth ? 1f : -1f,
        _ => 0
    };

    private static float HashNoise(long frame, int eventIndex)
    {
        ulong value;
        unchecked
        {
            value = (ulong)frame + 0x9E3779B97F4A7C15UL * (ulong)(eventIndex + 1);
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
        }

        return ((value >> 40) * (1f / 8_388_608f) * 2f) - 1f;
    }

    private static float Lerp(float a, float b, float amount) => a + ((b - a) * Math.Clamp(amount, 0, 1));
    private static double Wrap(double phase) => phase - Math.Floor(phase);

    private static SequenceEvent[] ParseProgram(string program)
    {
        if (string.IsNullOrWhiteSpace(program)) return [];
        var events = new List<SequenceEvent>();
        foreach (string command in program.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = command.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 4) throw new FormatException($"Invalid tone-sequence command '{command}'. Expected start|duration|semitones|amplitude.");
            double start = Parse(parts[0]);
            double duration = Math.Max(0.001, Parse(parts[1]));
            double semitones = Parse(parts[2]);
            float amplitude = (float)Math.Clamp(Parse(parts[3]), 0, 4);
            events.Add(new SequenceEvent(Math.Max(0, start), duration, semitones, amplitude));
        }
        return [.. events.OrderBy(item => item.StartSeconds)];
    }

    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private readonly record struct SequenceEvent(double StartSeconds, double DurationSeconds, double Semitones, float Amplitude);
    private readonly record struct RuntimeEvent(long StartFrame, long DurationFrames, double Semitones, float Amplitude, double Phase);
}

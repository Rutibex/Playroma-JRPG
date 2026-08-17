namespace Playroma.Sound.Dsp.Modulation;

public sealed class Lfo : IControlSource
{
    private int _sampleRate;
    private double _phase;

    public Lfo(LfoWaveform waveform = LfoWaveform.Sine, double frequencyHz = 1.0)
    {
        Waveform = waveform;
        FrequencyHz = frequencyHz;
    }

    public LfoWaveform Waveform { get; set; }
    public double FrequencyHz { get; set; }
    public double InitialPhase { get; set; }
    public float PulseWidth { get; set; } = 0.5f;

    public void Prepare(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        Reset();
    }

    public void Reset()
    {
        _phase = InitialPhase - Math.Floor(InitialPhase);
    }

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("The LFO must be prepared before use.");
        }

        float value = Waveform switch
        {
            LfoWaveform.Triangle => 1f - (4f * MathF.Abs((float)_phase - 0.5f)),
            LfoWaveform.Saw => (2f * (float)_phase) - 1f,
            LfoWaveform.Square => _phase < 0.5 ? 1f : -1f,
            LfoWaveform.Pulse => _phase < Math.Clamp(PulseWidth, 0.01f, 0.99f) ? 1f : -1f,
            _ => MathF.Sin((float)(_phase * Math.Tau))
        };

        double frequency = Math.Clamp(FrequencyHz, 0.0, _sampleRate * 0.49);
        _phase += frequency / _sampleRate;
        _phase -= Math.Floor(_phase);
        return value;
    }
}

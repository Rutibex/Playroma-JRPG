using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Finite, deliberately hard-edged pulse source modelled after the classic
/// handheld APU pulse channels. Frequency is selected with the native 11-bit
/// period value, amplitude is quantized to four bits, and envelope/sweep changes
/// occur on hardware-style 64 Hz and 128 Hz clocks.
/// </summary>
public sealed class HandheldPulseNode : AudioNodeBase
{
    private static readonly byte[][] DutyPatterns =
    [
        [0, 0, 0, 0, 0, 0, 0, 1],
        [1, 0, 0, 0, 0, 0, 0, 1],
        [1, 0, 0, 0, 0, 1, 1, 1],
        [0, 1, 1, 1, 1, 1, 1, 0]
    ];

    private double _phase;
    private long _elapsedFrames;
    private long _durationFrames;
    private int _currentVolume;
    private float _amplitude;
    private long _envelopeAccumulator;
    private long _envelopeStepFrames;
    private long _sweepAccumulator;
    private long _sweepStepFrames;
    private int _currentPeriod;
    private int _period;

    public HandheldPulseNode(
        int period = 1_600,
        float amplitude = 0.8f,
        HandheldPulseDuty duty = HandheldPulseDuty.Duty50,
        double durationSeconds = 0.25,
        int envelopeSweep = -2,
        int periodSlide = 0,
        double sweepRateHz = 128)
    {
        Period = period;
        Amplitude = amplitude;
        Duty = duty;
        DurationSeconds = durationSeconds;
        EnvelopeSweep = envelopeSweep;
        PeriodSlide = periodSlide;
        SweepRateHz = sweepRateHz;
    }

    public int Period
    {
        get => _period;
        set
        {
            _period = value;
            if (IsPrepared)
            {
                _currentPeriod = Math.Clamp(value, 0, 2_047);
            }
        }
    }
    public float Amplitude
    {
        get => _amplitude;
        set
        {
            _amplitude = value;
            if (IsPrepared)
            {
                _currentVolume = QuantizeVolume(value);
            }
        }
    }
    public HandheldPulseDuty Duty { get; set; }
    public double DurationSeconds { get; set; }
    public int EnvelopeSweep { get; set; }
    public int PeriodSlide { get; set; }
    public double SweepRateHz { get; set; }

    public override bool IsComplete => _durationFrames > 0 && _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _durationFrames = DurationSeconds <= 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(DurationSeconds * format.SampleRate)));
        _envelopeStepFrames = EnvelopeSweep == 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(format.SampleRate * Math.Abs(EnvelopeSweep) / 64.0)));
        _sweepStepFrames = SweepRateHz <= 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(format.SampleRate / SweepRateHz)));
    }

    public override void Reset()
    {
        _phase = 0;
        _elapsedFrames = 0;
        _currentVolume = QuantizeVolume(Amplitude);
        _envelopeAccumulator = 0;
        _sweepAccumulator = 0;
        _currentPeriod = Math.Clamp(Period, 0, 2_047);
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete)
        {
            return;
        }

        int channels = Format.Channels;
        int sampleIndex = 0;
        byte[] pattern = DutyPatterns[Math.Clamp((int)Duty, 0, DutyPatterns.Length - 1)];

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            UpdateEnvelope();
            UpdatePeriodSweep();

            double frequencyHz = PeriodToFrequency(_currentPeriod);
            double phaseIncrement = Math.Min(frequencyHz, Format.SampleRate * 0.49) / Format.SampleRate;
            int sequenceIndex = Math.Clamp((int)(_phase * 8.0), 0, 7);
            float value = pattern[sequenceIndex] == 0 ? 0f : _currentVolume / 15f;

            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] = value;
            }

            _phase += phaseIncrement;
            _phase -= Math.Floor(_phase);
            _elapsedFrames++;
        }
    }

    private void UpdateEnvelope()
    {
        if (_envelopeStepFrames <= 0 || EnvelopeSweep == 0)
        {
            return;
        }

        _envelopeAccumulator++;
        while (_envelopeAccumulator >= _envelopeStepFrames)
        {
            _envelopeAccumulator -= _envelopeStepFrames;
            _currentVolume = Math.Clamp(_currentVolume + Math.Sign(EnvelopeSweep), 0, 15);
        }
    }

    private void UpdatePeriodSweep()
    {
        if (_sweepStepFrames <= 0 || PeriodSlide == 0 || _durationFrames <= 1)
        {
            return;
        }

        _sweepAccumulator++;
        if (_sweepAccumulator < _sweepStepFrames)
        {
            return;
        }

        _sweepAccumulator %= _sweepStepFrames;
        double position = Math.Clamp(_elapsedFrames / (double)(_durationFrames - 1), 0, 1);
        _currentPeriod = Math.Clamp(Period + (int)Math.Round(PeriodSlide * position), 0, 2_047);
    }

    private static int QuantizeVolume(float amplitude) =>
        Math.Clamp((int)Math.Round(Math.Clamp(amplitude, 0f, 1f) * 15), 0, 15);

    private static double PeriodToFrequency(int period) =>
        131_072.0 / Math.Max(1, 2_048 - Math.Clamp(period, 0, 2_047));
}

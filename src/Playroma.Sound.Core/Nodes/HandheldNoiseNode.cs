using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Finite Game Boy-style noise source using the APU's 15-bit/7-bit LFSR,
/// NR43-compatible polynomial byte, four-bit volume, and 64 Hz envelope clock.
/// </summary>
public sealed class HandheldNoiseNode : AudioNodeBase
{
    private ushort _lfsr;
    private double _clockAccumulator;
    private long _elapsedFrames;
    private long _durationFrames;
    private int _currentVolume;
    private float _amplitude;
    private long _envelopeAccumulator;
    private long _envelopeStepFrames;

    public HandheldNoiseNode(
        byte polynomial = 0x35,
        float amplitude = 0.5f,
        double durationSeconds = 0.2,
        int envelopeSweep = -2)
    {
        Polynomial = polynomial;
        Amplitude = amplitude;
        DurationSeconds = durationSeconds;
        EnvelopeSweep = envelopeSweep;
    }

    public byte Polynomial { get; set; }
    public float Amplitude
    {
        get => _amplitude;
        set
        {
            _amplitude = value;
            if (IsPrepared)
            {
                _currentVolume = Math.Clamp((int)Math.Round(Math.Clamp(value, 0f, 1f) * 15), 0, 15);
            }
        }
    }
    public double DurationSeconds { get; set; }
    public int EnvelopeSweep { get; set; }

    public override bool IsComplete => _durationFrames > 0 && _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _durationFrames = DurationSeconds <= 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(DurationSeconds * format.SampleRate)));
        _envelopeStepFrames = EnvelopeSweep == 0
            ? 0
            : Math.Max(1, checked((long)Math.Round(format.SampleRate * Math.Abs(EnvelopeSweep) / 64.0)));
    }

    public override void Reset()
    {
        _lfsr = 0x7FFF;
        _clockAccumulator = 0;
        _elapsedFrames = 0;
        _currentVolume = Math.Clamp((int)Math.Round(Math.Clamp(Amplitude, 0f, 1f) * 15), 0, 15);
        _envelopeAccumulator = 0;
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
        int shift = (Polynomial >> 4) & 0x0F;
        bool shortMode = (Polynomial & 0x08) != 0;
        int dividerCode = Polynomial & 0x07;
        double clockHz = NoiseClockFrequency(shift, dividerCode);
        double clocksPerSample = clockHz / Format.SampleRate;

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            UpdateEnvelope();
            _clockAccumulator += clocksPerSample;
            int shifts = Math.Min(128, (int)_clockAccumulator);
            _clockAccumulator -= shifts;
            for (int index = 0; index < shifts; index++)
            {
                ClockLfsr(shortMode);
            }

            float value = (_lfsr & 1) == 0 ? 0f : _currentVolume / 15f;
            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] = value;
            }

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

    private void ClockLfsr(bool shortMode)
    {
        int feedback = (_lfsr ^ (_lfsr >> 1)) & 1;
        _lfsr = (ushort)((_lfsr >> 1) | (feedback << 14));
        if (shortMode)
        {
            _lfsr = (ushort)((_lfsr & ~(1 << 6)) | (feedback << 6));
        }
    }

    private static double NoiseClockFrequency(int shift, int dividerCode)
    {
        if (shift >= 14)
        {
            return 0;
        }

        double divisor = dividerCode == 0 ? 0.5 : dividerCode;
        return 262_144.0 / divisor / Math.Pow(2, shift + 1);
    }
}

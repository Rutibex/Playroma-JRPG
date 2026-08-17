using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Dsp.Modulation;

public sealed class SampleAndHold : IControlSource
{
    private readonly ulong _seed;
    private Pcg32 _random;
    private int _sampleRate;
    private int _remainingSamples;
    private float _value;

    public SampleAndHold(double rateHz = 2.0, ulong seed = 1)
    {
        RateHz = rateHz;
        _seed = seed;
        _random = new Pcg32(seed);
    }

    public double RateHz { get; set; }

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
        _random = new Pcg32(_seed);
        _remainingSamples = 0;
        _value = 0f;
    }

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("The control source must be prepared before use.");
        }

        if (_remainingSamples <= 0)
        {
            _value = (_random.NextFloat() * 2f) - 1f;
            double rate = Math.Clamp(RateHz, 0.001, _sampleRate * 0.49);
            _remainingSamples = Math.Max(1, (int)Math.Round(_sampleRate / rate));
        }

        _remainingSamples--;
        return _value;
    }
}

using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Dsp.Modulation;

public sealed class RandomWalk : IControlSource
{
    private readonly ulong _seed;
    private Pcg32 _random;
    private int _sampleRate;
    private float _value;

    public RandomWalk(ulong seed = 1)
    {
        _seed = seed;
        _random = new Pcg32(seed);
    }

    public float StepPerSecond { get; set; } = 0.5f;
    public float PullToCenter { get; set; } = 0.05f;

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
        _value = 0f;
    }

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("The control source must be prepared before use.");
        }

        float randomStep = ((_random.NextFloat() * 2f) - 1f) * StepPerSecond / _sampleRate;
        float centering = -_value * Math.Clamp(PullToCenter, 0f, 10f) / _sampleRate;
        _value = Math.Clamp(_value + randomStep + centering, -1f, 1f);
        return _value;
    }
}

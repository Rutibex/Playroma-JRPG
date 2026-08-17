namespace Playroma.Sound.Dsp.Dynamics;

public sealed class EnvelopeFollower
{
    private int _sampleRate;
    private float _attackCoefficient;
    private float _releaseCoefficient;
    private float _value;

    public float Value => _value;

    public void Prepare(int sampleRate, double attackSeconds, double releaseSeconds)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        SetTimes(attackSeconds, releaseSeconds);
        Reset();
    }

    public void SetTimes(double attackSeconds, double releaseSeconds)
    {
        if (!double.IsFinite(attackSeconds) || attackSeconds < 0 ||
            !double.IsFinite(releaseSeconds) || releaseSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackSeconds));
        }

        _attackCoefficient = TimeToCoefficient(attackSeconds);
        _releaseCoefficient = TimeToCoefficient(releaseSeconds);
    }

    public void Reset() => _value = 0f;

    public float Process(float detectorValue)
    {
        detectorValue = MathF.Abs(detectorValue);
        float coefficient = detectorValue > _value ? _attackCoefficient : _releaseCoefficient;
        _value = detectorValue + (coefficient * (_value - detectorValue));
        return _value;
    }

    private float TimeToCoefficient(double seconds)
    {
        if (_sampleRate <= 0 || seconds <= 0)
        {
            return 0f;
        }

        return (float)Math.Exp(-1.0 / (seconds * _sampleRate));
    }
}

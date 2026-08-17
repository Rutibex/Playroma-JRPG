namespace Playroma.Sound.Dsp.Parameters;

/// <summary>
/// Thread-safe target value with audio-thread-owned smoothing state.
/// </summary>
public sealed class AudioParameter
{
    private int _targetBits;
    private int _sampleRate;
    private float _current;
    private float _lastTarget;
    private float _linearIncrement;
    private int _linearSamplesRemaining;
    private float _stageOne;
    private float _smoothingCoefficient;
    private double _smoothingTimeSeconds = 0.01;

    public AudioParameter(
        ParameterMetadata metadata,
        SmoothingMode smoothingMode = SmoothingMode.Exponential,
        double smoothingTimeSeconds = 0.01)
    {
        Metadata = (metadata ?? throw new ArgumentNullException(nameof(metadata))).Validate();
        SmoothingMode = smoothingMode;
        SmoothingTimeSeconds = smoothingTimeSeconds;
        _targetBits = BitConverter.SingleToInt32Bits(Metadata.DefaultValue);
        _current = Metadata.DefaultValue;
        _lastTarget = Metadata.DefaultValue;
        _stageOne = Metadata.DefaultValue;
    }

    public ParameterMetadata Metadata { get; }
    public SmoothingMode SmoothingMode { get; set; }

    public double SmoothingTimeSeconds
    {
        get => _smoothingTimeSeconds;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _smoothingTimeSeconds = value;
            RecalculateCoefficient();
        }
    }

    public float TargetValue => BitConverter.Int32BitsToSingle(Volatile.Read(ref _targetBits));
    public float CurrentValue => _current;

    public void Prepare(int sampleRate)
    {
        if (sampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        RecalculateCoefficient();
        Reset();
    }

    public void Reset()
    {
        float target = TargetValue;
        _current = target;
        _lastTarget = target;
        _stageOne = target;
        _linearIncrement = 0f;
        _linearSamplesRemaining = 0;
    }

    public void SetValue(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        value = Math.Clamp(value, Metadata.Minimum, Metadata.Maximum);
        Interlocked.Exchange(ref _targetBits, BitConverter.SingleToInt32Bits(value));
    }

    public void SetNormalizedValue(float normalized) =>
        SetValue(ParameterMapping.Denormalize(normalized, Metadata));

    public float GetNormalizedValue() => ParameterMapping.Normalize(TargetValue, Metadata);

    public float NextValue()
    {
        float target = TargetValue;
        if (target != _lastTarget)
        {
            OnTargetChanged(target);
        }

        switch (SmoothingMode)
        {
            case SmoothingMode.None:
                _current = target;
                break;

            case SmoothingMode.Linear:
                if (_linearSamplesRemaining > 0)
                {
                    _current += _linearIncrement;
                    _linearSamplesRemaining--;
                }
                else
                {
                    _current = target;
                }
                break;

            case SmoothingMode.CriticallyDamped:
                _stageOne += (target - _stageOne) * _smoothingCoefficient;
                _current += (_stageOne - _current) * _smoothingCoefficient;
                break;

            default:
                _current += (target - _current) * _smoothingCoefficient;
                break;
        }

        if (!float.IsFinite(_current))
        {
            _current = target;
        }

        return _current;
    }

    public float Advance(int sampleCount)
    {
        if (sampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        for (int i = 0; i < sampleCount; i++)
        {
            NextValue();
        }

        return _current;
    }

    private void OnTargetChanged(float target)
    {
        _lastTarget = target;
        if (SmoothingMode == SmoothingMode.Linear)
        {
            int samples = Math.Max(1, (int)Math.Round(SmoothingTimeSeconds * Math.Max(1, _sampleRate)));
            _linearSamplesRemaining = samples;
            _linearIncrement = (target - _current) / samples;
        }
    }

    private void RecalculateCoefficient()
    {
        if (_sampleRate <= 0 || SmoothingTimeSeconds <= 0)
        {
            _smoothingCoefficient = 1f;
            return;
        }

        double coefficient = 1.0 - Math.Exp(-1.0 / (SmoothingTimeSeconds * _sampleRate));
        _smoothingCoefficient = (float)Math.Clamp(coefficient, 0.000001, 1.0);
    }
}

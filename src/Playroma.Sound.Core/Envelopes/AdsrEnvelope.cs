namespace Playroma.Sound.Core.Envelopes;

public sealed class AdsrEnvelope : IEnvelope
{
    private enum Stage
    {
        Idle,
        Attack,
        Decay,
        Sustain,
        Release,
        Complete
    }

    private int _sampleRate;
    private Stage _stage;
    private int _stageSample;
    private int _attackSamples;
    private int _decaySamples;
    private int _sustainSamples;
    private int _releaseSamples;
    private float _releaseStart;

    public AdsrEnvelope(
        double attackSeconds,
        double decaySeconds,
        float sustainLevel,
        double sustainSeconds,
        double releaseSeconds)
    {
        ValidateTime(attackSeconds, nameof(attackSeconds));
        ValidateTime(decaySeconds, nameof(decaySeconds));
        ValidateTime(sustainSeconds, nameof(sustainSeconds));
        ValidateTime(releaseSeconds, nameof(releaseSeconds));

        if (sustainLevel is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(sustainLevel));
        }

        AttackSeconds = attackSeconds;
        DecaySeconds = decaySeconds;
        SustainLevel = sustainLevel;
        SustainSeconds = sustainSeconds;
        ReleaseSeconds = releaseSeconds;
    }

    public double AttackSeconds { get; }
    public double DecaySeconds { get; }
    public float SustainLevel { get; }
    public double SustainSeconds { get; }
    public double ReleaseSeconds { get; }

    public bool IsComplete => _stage == Stage.Complete;

    public void Prepare(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        _attackSamples = SecondsToSamples(AttackSeconds);
        _decaySamples = SecondsToSamples(DecaySeconds);
        _sustainSamples = SecondsToSamples(SustainSeconds);
        _releaseSamples = SecondsToSamples(ReleaseSeconds);
        Reset();
    }

    public void Reset()
    {
        _stage = Stage.Attack;
        _stageSample = 0;
        _releaseStart = SustainLevel;
        SkipEmptyStages();
    }

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("Envelope must be prepared before use.");
        }

        float value = _stage switch
        {
            Stage.Idle => 0f,
            Stage.Attack => _stageSample / (float)Math.Max(1, _attackSamples),
            Stage.Decay => 1f + ((SustainLevel - 1f) * (_stageSample / (float)Math.Max(1, _decaySamples))),
            Stage.Sustain => SustainLevel,
            Stage.Release => _releaseStart * (1f - (_stageSample / (float)Math.Max(1, _releaseSamples))),
            Stage.Complete => 0f,
            _ => 0f
        };

        Advance();
        return Math.Clamp(value, 0f, 1f);
    }

    public void ReleaseNow(float currentValue)
    {
        _releaseStart = Math.Clamp(currentValue, 0f, 1f);
        _stage = Stage.Release;
        _stageSample = 0;
        SkipEmptyStages();
    }

    private void Advance()
    {
        _stageSample++;

        switch (_stage)
        {
            case Stage.Attack when _stageSample >= _attackSamples:
                MoveTo(Stage.Decay);
                break;
            case Stage.Decay when _stageSample >= _decaySamples:
                MoveTo(Stage.Sustain);
                break;
            case Stage.Sustain when _stageSample >= _sustainSamples:
                _releaseStart = SustainLevel;
                MoveTo(Stage.Release);
                break;
            case Stage.Release when _stageSample >= _releaseSamples:
                MoveTo(Stage.Complete);
                break;
        }
    }

    private void MoveTo(Stage stage)
    {
        _stage = stage;
        _stageSample = 0;
        SkipEmptyStages();
    }

    private void SkipEmptyStages()
    {
        bool advanced;

        do
        {
            advanced = false;

            if (_stage == Stage.Attack && _attackSamples == 0)
            {
                _stage = Stage.Decay;
                advanced = true;
            }
            else if (_stage == Stage.Decay && _decaySamples == 0)
            {
                _stage = Stage.Sustain;
                advanced = true;
            }
            else if (_stage == Stage.Sustain && _sustainSamples == 0)
            {
                _releaseStart = SustainLevel;
                _stage = Stage.Release;
                advanced = true;
            }
            else if (_stage == Stage.Release && _releaseSamples == 0)
            {
                _stage = Stage.Complete;
                advanced = true;
            }

            if (advanced)
            {
                _stageSample = 0;
            }
        }
        while (advanced);
    }

    private int SecondsToSamples(double seconds) =>
        checked((int)Math.Round(seconds * _sampleRate));

    private static void ValidateTime(double seconds, string parameterName)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

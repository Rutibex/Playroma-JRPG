namespace Playroma.Sound.Core.Envelopes;

/// <summary>
/// Finite one-shot envelope made from ordered time/value points.
/// </summary>
public sealed class SegmentEnvelope : IEnvelope
{
    private readonly EnvelopePoint[] _points;
    private int _sampleRate;
    private long _samplePosition;
    private long _finalSample;

    public SegmentEnvelope(IEnumerable<EnvelopePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points.OrderBy(point => point.TimeSeconds).ToArray();

        if (_points.Length < 2)
        {
            throw new ArgumentException("A segment envelope requires at least two points.", nameof(points));
        }

        if (_points[0].TimeSeconds != 0.0)
        {
            throw new ArgumentException("The first envelope point must begin at zero seconds.", nameof(points));
        }

        foreach (EnvelopePoint point in _points)
        {
            if (!double.IsFinite(point.TimeSeconds) || point.TimeSeconds < 0)
            {
                throw new ArgumentException("Envelope times must be finite and non-negative.", nameof(points));
            }

            if (!float.IsFinite(point.Value))
            {
                throw new ArgumentException("Envelope values must be finite.", nameof(points));
            }
        }
    }

    public bool IsComplete => _samplePosition > _finalSample;

    public void Prepare(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        _finalSample = checked((long)Math.Round(_points[^1].TimeSeconds * sampleRate));
        Reset();
    }

    public void Reset() => _samplePosition = 0;

    public float NextValue()
    {
        if (_sampleRate <= 0)
        {
            throw new InvalidOperationException("Envelope must be prepared before use.");
        }

        if (_samplePosition > _finalSample)
        {
            return _points[^1].Value;
        }

        double time = _samplePosition / (double)_sampleRate;
        _samplePosition++;

        int segmentIndex = FindSegment(time);
        EnvelopePoint start = _points[segmentIndex];
        EnvelopePoint end = _points[Math.Min(segmentIndex + 1, _points.Length - 1)];

        if (start.TimeSeconds == end.TimeSeconds)
        {
            return end.Value;
        }

        float amount = (float)((time - start.TimeSeconds) / (end.TimeSeconds - start.TimeSeconds));
        amount = Math.Clamp(amount, 0f, 1f);
        amount = Shape(amount, start.CurveToNext, start.Value, end.Value);
        return start.Value + ((end.Value - start.Value) * amount);
    }

    private int FindSegment(double time)
    {
        for (int i = 0; i < _points.Length - 1; i++)
        {
            if (time <= _points[i + 1].TimeSeconds)
            {
                return i;
            }
        }

        return _points.Length - 1;
    }

    private static float Shape(float amount, EnvelopeCurve curve, float start, float end)
    {
        return curve switch
        {
            EnvelopeCurve.Linear => amount,
            EnvelopeCurve.SmoothStep => amount * amount * (3f - (2f * amount)),
            EnvelopeCurve.Exponential => Exponential(amount, start, end),
            _ => amount
        };
    }

    private static float Exponential(float amount, float start, float end)
    {
        // This curve remains well-defined through zero and for bipolar values.
        const float curvature = 5f;
        float shaped = (MathF.Exp(curvature * amount) - 1f) / (MathF.Exp(curvature) - 1f);
        return end >= start ? shaped : 1f - ((MathF.Exp(curvature * (1f - amount)) - 1f) / (MathF.Exp(curvature) - 1f));
    }
}

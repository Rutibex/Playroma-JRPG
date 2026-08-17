using System.Globalization;

namespace Playroma.Sound.Core.Generation;

/// <summary>Interpolation used between two expressive gesture points.</summary>
public enum GestureCurveType
{
    Linear,
    SmoothStep,
    EaseIn,
    EaseOut,
    Step
}

/// <summary>Optional pitch quantization applied to a gesture oscillator.</summary>
public enum PitchQuantizationMode
{
    None,
    QuarterTone,
    Chromatic,
    WholeTone
}

/// <summary>A normalized position/value pair. Curve controls the segment leading to the next point.</summary>
public readonly record struct GesturePoint(double Position, double Value, GestureCurveType Curve = GestureCurveType.SmoothStep);

/// <summary>
/// Compact serializable automation curve used for pitch and amplitude gestures.
/// Text format: position:value:curve;position:value:curve.
/// </summary>
public sealed class GestureCurve
{
    private readonly GesturePoint[] _points;

    public GestureCurve(IEnumerable<GesturePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = Normalize(points).ToArray();
        if (_points.Length == 0)
        {
            _points = [new GesturePoint(0, 0), new GesturePoint(1, 0)];
        }
    }

    public IReadOnlyList<GesturePoint> Points => _points;

    public double Evaluate(double position)
    {
        if (!double.IsFinite(position)) position = 0;
        position = Math.Clamp(position, 0, 1);
        if (_points.Length == 1) return _points[0].Value;
        if (position <= _points[0].Position) return _points[0].Value;
        if (position >= _points[^1].Position) return _points[^1].Value;

        int upper = 1;
        while (upper < _points.Length && position > _points[upper].Position) upper++;
        GesturePoint left = _points[upper - 1];
        GesturePoint right = _points[upper];
        double span = Math.Max(0.0000001, right.Position - left.Position);
        double amount = Math.Clamp((position - left.Position) / span, 0, 1);
        amount = left.Curve switch
        {
            GestureCurveType.SmoothStep => amount * amount * (3 - (2 * amount)),
            GestureCurveType.EaseIn => amount * amount,
            GestureCurveType.EaseOut => 1 - ((1 - amount) * (1 - amount)),
            GestureCurveType.Step => 0,
            _ => amount
        };
        return left.Value + ((right.Value - left.Value) * amount);
    }

    public override string ToString() => string.Join(';', _points.Select(point => string.Create(
        CultureInfo.InvariantCulture,
        $"{point.Position:0.####}:{point.Value:0.####}:{point.Curve}")));

    public static GestureCurve Flat(double value) => new([new GesturePoint(0, value), new GesturePoint(1, value)]);

    public static GestureCurve Parse(string? text, double fallbackValue = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return Flat(fallbackValue);
        var points = new List<GesturePoint>();
        foreach (string token in text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = token.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double position) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                continue;
            }

            GestureCurveType curve = GestureCurveType.SmoothStep;
            if (parts.Length >= 3) Enum.TryParse(parts[2], true, out curve);
            points.Add(new GesturePoint(position, value, curve));
        }

        return points.Count == 0 ? Flat(fallbackValue) : new GestureCurve(points);
    }

    private static IEnumerable<GesturePoint> Normalize(IEnumerable<GesturePoint> points)
    {
        GesturePoint[] ordered = points
            .Where(point => double.IsFinite(point.Position) && double.IsFinite(point.Value))
            .Select(point => point with { Position = Math.Clamp(point.Position, 0, 1) })
            .OrderBy(point => point.Position)
            .ToArray();

        if (ordered.Length == 0) yield break;
        var deduplicated = new List<GesturePoint>(ordered.Length);
        foreach (GesturePoint point in ordered)
        {
            if (deduplicated.Count > 0 && Math.Abs(deduplicated[^1].Position - point.Position) < 0.0000001)
                deduplicated[^1] = point;
            else
                deduplicated.Add(point);
        }

        if (deduplicated[0].Position > 0)
            deduplicated.Insert(0, deduplicated[0] with { Position = 0 });
        if (deduplicated[^1].Position < 1)
            deduplicated.Add(deduplicated[^1] with { Position = 1 });

        foreach (GesturePoint point in deduplicated) yield return point;
    }
}

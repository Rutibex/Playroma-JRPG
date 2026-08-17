namespace Playroma.Sound.Core.Envelopes;

public readonly record struct EnvelopePoint(double TimeSeconds, float Value, EnvelopeCurve CurveToNext = EnvelopeCurve.Linear);

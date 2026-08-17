using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;
using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Deterministic evolving texture source used for natural ambience, machinery,
/// crowds, electrical beds, radio noise, organic movement, and abstract glitches.
/// </summary>
public sealed class ProceduralTextureNode : AudioNodeBase
{
    private Pcg32 _random;
    private long _elapsedFrames;
    private long _durationFrames;
    private double _phaseA;
    private double _phaseB;
    private float _low;
    private float _mid;
    private float _impulse;
    private float _sampleHold;
    private int _holdFrames;

    public ProceduralTextureNode(ProceduralTextureKind kind, double durationSeconds, ulong seed = 1)
    {
        Kind = kind;
        DurationSeconds = durationSeconds;
        Seed = seed;
    }

    public ProceduralTextureKind Kind { get; set; }
    public double DurationSeconds { get; }
    public ulong Seed { get; set; }
    public float Amplitude { get; set; } = 0.5f;
    public float Intensity { get; set; } = 0.5f;
    public float Density { get; set; } = 0.5f;
    public float Brightness { get; set; } = 0.5f;
    public float Movement { get; set; } = 0.5f;
    public double BaseFrequencyHz { get; set; } = 120;
    public double AttackSeconds { get; set; } = 0.05;
    public double ReleaseSeconds { get; set; } = 0.2;

    public override bool IsComplete => _durationFrames > 0 && _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _durationFrames = DurationSeconds <= 0 ? 0 : Math.Max(1, (long)Math.Round(DurationSeconds * format.SampleRate));
    }

    public override void Reset()
    {
        _random = new Pcg32(Seed == 0 ? 1UL : Seed);
        _elapsedFrames = 0;
        _phaseA = _random.NextDouble();
        _phaseB = _random.NextDouble();
        _low = 0;
        _mid = 0;
        _impulse = 0;
        _sampleHold = 0;
        _holdFrames = 0;
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete) return;
        int channels = Format.Channels;
        int sampleIndex = 0;
        double sampleRate = Format.SampleRate;
        double attackFrames = Math.Max(0, AttackSeconds * sampleRate);
        double releaseFrames = Math.Max(0, ReleaseSeconds * sampleRate);

        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            float white = _random.NextSignedFloat();
            float smoothing = 0.002f + ((1 - Brightness) * 0.045f);
            _low += (white - _low) * smoothing;
            _mid += (white - _mid) * (0.02f + (Brightness * 0.16f));
            float high = white - _mid;
            float value = Render(white, high, sampleRate);

            double envelope = 1;
            if (attackFrames > 0 && _elapsedFrames < attackFrames) envelope *= _elapsedFrames / attackFrames;
            if (_durationFrames > 0 && releaseFrames > 0 && _elapsedFrames > _durationFrames - releaseFrames)
                envelope *= Math.Clamp((_durationFrames - _elapsedFrames) / releaseFrames, 0, 1);
            value *= Amplitude * (float)envelope;

            for (int channel = 0; channel < channels; channel++) output[sampleIndex++] = value;
            _elapsedFrames++;
        }
    }

    private float Render(float white, float high, double sampleRate)
    {
        double seconds = _elapsedFrames / sampleRate;
        double slow = 0.5 + (0.5 * Math.Sin(Math.Tau * (0.03 + (Movement * 0.7)) * seconds));
        double baseFrequency = Math.Clamp(BaseFrequencyHz, 5, sampleRate * 0.4);
        float value;
        switch (Kind)
        {
            case ProceduralTextureKind.Wind:
                value = _low * (float)(0.6 + (slow * 0.7)) + high * Brightness * 0.15f;
                break;
            case ProceduralTextureKind.Rain:
                TriggerImpulse(4 + (Density * 150), 0.55f + (Intensity * 0.9f), sampleRate);
                value = high * (0.1f + (Density * 0.22f)) + _impulse;
                break;
            case ProceduralTextureKind.Fire:
                TriggerImpulse(0.5f + (Density * 18), 0.4f + Intensity, sampleRate);
                value = (_low * 0.35f) + (high * 0.12f) + _impulse;
                break;
            case ProceduralTextureKind.Water:
                TriggerImpulse(0.15f + (Density * 7), 0.18f + (Intensity * 0.45f), sampleRate);
                value = _low * 0.45f + (float)Math.Sin(_phaseA * Math.Tau) * _impulse;
                Advance(ref _phaseA, (baseFrequency * (1 + (slow * 2))) / sampleRate);
                break;
            case ProceduralTextureKind.Machine:
                TriggerImpulse(0.2f + (Density * 6), 0.25f + (Intensity * 0.55f), sampleRate);
                value = (float)Math.Sin(_phaseA * Math.Tau) * 0.48f
                    + (float)Math.Sin(_phaseB * Math.Tau) * 0.2f + _impulse;
                Advance(ref _phaseA, baseFrequency / sampleRate);
                Advance(ref _phaseB, baseFrequency * (1.5 + Movement * 5) / sampleRate);
                break;
            case ProceduralTextureKind.Crowd:
                value = _low * 0.55f + _mid * 0.22f
                    + (float)Math.Sin(_phaseA * Math.Tau) * (0.05f + Density * 0.08f);
                Advance(ref _phaseA, (70 + slow * 160) / sampleRate);
                break;
            case ProceduralTextureKind.Insects:
                double gate = Math.Sin(Math.Tau * (3 + Density * 19) * seconds) > 0.35 ? 1 : 0;
                value = (float)Math.Sin(_phaseA * Math.Tau) * (float)gate * (0.15f + Intensity * 0.55f);
                Advance(ref _phaseA, (baseFrequency * (8 + Brightness * 24)) / sampleRate);
                break;
            case ProceduralTextureKind.Electrical:
                TriggerImpulse(0.15f + (Density * 15), 0.5f + Intensity, sampleRate);
                value = (float)Math.Sin(_phaseA * Math.Tau) * 0.35f + high * 0.12f + _impulse;
                Advance(ref _phaseA, baseFrequency / sampleRate);
                break;
            case ProceduralTextureKind.Organic:
                value = _low * 0.45f + (float)Math.Sin(_phaseA * Math.Tau) * (float)(0.22 + slow * 0.25);
                Advance(ref _phaseA, (baseFrequency * (0.25 + slow)) / sampleRate);
                break;
            case ProceduralTextureKind.Glitch:
                if (_holdFrames-- <= 0)
                {
                    _sampleHold = _random.NextSignedFloat();
                    _holdFrames = Math.Max(1, (int)(sampleRate / (20 + Density * 2_500)));
                }
                value = _sampleHold * 0.55f + high * 0.22f;
                break;
            case ProceduralTextureKind.Radio:
                value = high * 0.5f + _mid * 0.18f + (float)Math.Sin(_phaseA * Math.Tau) * 0.08f;
                Advance(ref _phaseA, 50 / sampleRate);
                break;
            case ProceduralTextureKind.Dust:
                TriggerImpulse(0.05f + (Density * 3), 0.12f + Intensity * 0.35f, sampleRate);
                value = high * 0.15f + _impulse;
                break;
            case ProceduralTextureKind.Steam:
                value = _mid * 0.45f + high * 0.25f;
                break;
            case ProceduralTextureKind.Underwater:
                value = _low * 0.65f + (float)Math.Sin(_phaseA * Math.Tau) * 0.12f;
                Advance(ref _phaseA, (baseFrequency * (0.2 + slow * 0.4)) / sampleRate);
                break;
            case ProceduralTextureKind.Forest:
                TriggerImpulse(0.02f + (Density * 2), 0.12f + Intensity * 0.2f, sampleRate);
                value = _low * 0.4f + high * 0.08f + _impulse
                    + (float)Math.Sin(_phaseA * Math.Tau) * (Density * 0.08f);
                Advance(ref _phaseA, (1_300 + Brightness * 4_000) / sampleRate);
                break;
            case ProceduralTextureKind.Space:
                value = (float)Math.Sin(_phaseA * Math.Tau) * 0.3f
                    + (float)Math.Sin(_phaseB * Math.Tau) * 0.18f + _low * 0.08f;
                Advance(ref _phaseA, baseFrequency * 0.23 / sampleRate);
                Advance(ref _phaseB, baseFrequency * (0.51 + Movement) / sampleRate);
                break;
            default:
                value = white;
                break;
        }

        _impulse *= 0.94f - (Brightness * 0.04f);
        return Math.Clamp(value, -1.5f, 1.5f);
    }

    private void TriggerImpulse(float ratePerSecond, float level, double sampleRate)
    {
        double probability = 1 - Math.Exp(-Math.Max(0, ratePerSecond) / sampleRate);
        if (_random.NextDouble() < probability) _impulse += _random.NextSignedFloat() * level;
    }

    private static void Advance(ref double phase, double increment)
    {
        phase += increment;
        phase -= Math.Floor(phase);
    }
}

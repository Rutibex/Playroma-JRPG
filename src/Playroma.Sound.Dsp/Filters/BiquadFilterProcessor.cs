using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Filters;

/// <summary>
/// Audio EQ Cookbook biquad with smoothed frequency, Q, and gain parameters.
/// </summary>
public sealed class BiquadFilterProcessor : AudioProcessorBase
{
    private float[] _z1 = [];
    private float[] _z2 = [];

    public BiquadFilterProcessor(BiquadFilterType filterType = BiquadFilterType.LowPass)
    {
        FilterType = filterType;
        Frequency = new AudioParameter(
            new ParameterMetadata("frequency", "Frequency", "Hz", 10f, 40_000f, 2_000f, ParameterScale.Logarithmic));
        Q = new AudioParameter(
            new ParameterMetadata("q", "Resonance", string.Empty, 0.05f, 50f, 0.7071068f, ParameterScale.Logarithmic));
        GainDecibels = new AudioParameter(
            new ParameterMetadata("gain", "Gain", "dB", -60f, 60f, 0f, ParameterScale.Bipolar));
    }

    public BiquadFilterType FilterType { get; set; }
    public AudioParameter Frequency { get; }
    public AudioParameter Q { get; }
    public AudioParameter GainDecibels { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        _z1 = new float[format.Channels];
        _z2 = new float[format.Channels];
        Frequency.Prepare(format.SampleRate);
        Q.Prepare(format.SampleRate);
        GainDecibels.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_z1);
        Array.Clear(_z2);
        Frequency.Reset();
        Q.Reset();
        GainDecibels.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        int sampleIndex = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float frequency = Math.Clamp(Frequency.NextValue(), 10f, Format.SampleRate * 0.49f);
            float q = Math.Clamp(Q.NextValue(), 0.05f, 50f);
            float gainDb = Math.Clamp(GainDecibels.NextValue(), -60f, 60f);
            CalculateCoefficients(FilterType, frequency, q, gainDb, Format.SampleRate,
                out float b0, out float b1, out float b2, out float a1, out float a2);

            for (int channel = 0; channel < channels; channel++)
            {
                float input = samples[sampleIndex];
                float output = (b0 * input) + _z1[channel];
                _z1[channel] = (b1 * input) - (a1 * output) + _z2[channel];
                _z2[channel] = (b2 * input) - (a2 * output);
                samples[sampleIndex++] = float.IsFinite(output) ? output : 0f;
            }
        }
    }

    private static void CalculateCoefficients(
        BiquadFilterType type,
        float frequency,
        float q,
        float gainDb,
        int sampleRate,
        out float b0,
        out float b1,
        out float b2,
        out float a1,
        out float a2)
    {
        double omega = Math.Tau * frequency / sampleRate;
        double sine = Math.Sin(omega);
        double cosine = Math.Cos(omega);
        double alpha = sine / (2.0 * q);
        double amplitude = Math.Pow(10.0, gainDb / 40.0);
        double sqrtAmplitude = Math.Sqrt(amplitude);

        double rb0;
        double rb1;
        double rb2;
        double ra0;
        double ra1;
        double ra2;

        switch (type)
        {
            case BiquadFilterType.HighPass:
                rb0 = (1.0 + cosine) / 2.0;
                rb1 = -(1.0 + cosine);
                rb2 = rb0;
                ra0 = 1.0 + alpha;
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - alpha;
                break;

            case BiquadFilterType.BandPass:
                rb0 = alpha;
                rb1 = 0.0;
                rb2 = -alpha;
                ra0 = 1.0 + alpha;
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - alpha;
                break;

            case BiquadFilterType.Notch:
                rb0 = 1.0;
                rb1 = -2.0 * cosine;
                rb2 = 1.0;
                ra0 = 1.0 + alpha;
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - alpha;
                break;

            case BiquadFilterType.AllPass:
                rb0 = 1.0 - alpha;
                rb1 = -2.0 * cosine;
                rb2 = 1.0 + alpha;
                ra0 = 1.0 + alpha;
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - alpha;
                break;

            case BiquadFilterType.Peaking:
                rb0 = 1.0 + (alpha * amplitude);
                rb1 = -2.0 * cosine;
                rb2 = 1.0 - (alpha * amplitude);
                ra0 = 1.0 + (alpha / amplitude);
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - (alpha / amplitude);
                break;

            case BiquadFilterType.LowShelf:
            {
                double shelfAlpha = sine / 2.0 * Math.Sqrt(2.0);
                double twoRootAlpha = 2.0 * sqrtAmplitude * shelfAlpha;
                rb0 = amplitude * ((amplitude + 1.0) - ((amplitude - 1.0) * cosine) + twoRootAlpha);
                rb1 = 2.0 * amplitude * ((amplitude - 1.0) - ((amplitude + 1.0) * cosine));
                rb2 = amplitude * ((amplitude + 1.0) - ((amplitude - 1.0) * cosine) - twoRootAlpha);
                ra0 = (amplitude + 1.0) + ((amplitude - 1.0) * cosine) + twoRootAlpha;
                ra1 = -2.0 * ((amplitude - 1.0) + ((amplitude + 1.0) * cosine));
                ra2 = (amplitude + 1.0) + ((amplitude - 1.0) * cosine) - twoRootAlpha;
                break;
            }

            case BiquadFilterType.HighShelf:
            {
                double shelfAlpha = sine / 2.0 * Math.Sqrt(2.0);
                double twoRootAlpha = 2.0 * sqrtAmplitude * shelfAlpha;
                rb0 = amplitude * ((amplitude + 1.0) + ((amplitude - 1.0) * cosine) + twoRootAlpha);
                rb1 = -2.0 * amplitude * ((amplitude - 1.0) + ((amplitude + 1.0) * cosine));
                rb2 = amplitude * ((amplitude + 1.0) + ((amplitude - 1.0) * cosine) - twoRootAlpha);
                ra0 = (amplitude + 1.0) - ((amplitude - 1.0) * cosine) + twoRootAlpha;
                ra1 = 2.0 * ((amplitude - 1.0) - ((amplitude + 1.0) * cosine));
                ra2 = (amplitude + 1.0) - ((amplitude - 1.0) * cosine) - twoRootAlpha;
                break;
            }

            default:
                rb0 = (1.0 - cosine) / 2.0;
                rb1 = 1.0 - cosine;
                rb2 = rb0;
                ra0 = 1.0 + alpha;
                ra1 = -2.0 * cosine;
                ra2 = 1.0 - alpha;
                break;
        }

        double inverseA0 = Math.Abs(ra0) > double.Epsilon ? 1.0 / ra0 : 1.0;
        b0 = (float)(rb0 * inverseA0);
        b1 = (float)(rb1 * inverseA0);
        b2 = (float)(rb2 * inverseA0);
        a1 = (float)(ra1 * inverseA0);
        a2 = (float)(ra2 * inverseA0);
    }
}

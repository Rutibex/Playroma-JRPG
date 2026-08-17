using System.Numerics;

namespace Playroma.Sound.Core.Audio;

public static class AudioMath
{
    public const float Silence = 0f;

    public static float DecibelsToLinear(float decibels) =>
        MathF.Pow(10f, decibels / 20f);

    public static float LinearToDecibels(float linear)
    {
        if (linear <= 0f)
        {
            return float.NegativeInfinity;
        }

        return 20f * MathF.Log10(linear);
    }

    public static float ClampSample(float sample) =>
        Math.Clamp(sample, -1f, 1f);

    public static float SoftClip(float sample) =>
        sample / (1f + MathF.Abs(sample));

    public static void ApplyGain(Span<float> samples, float gain)
    {
        int vectorWidth = Vector<float>.Count;
        int index = 0;

        if (Vector.IsHardwareAccelerated && samples.Length >= vectorWidth)
        {
            var gainVector = new Vector<float>(gain);
            int lastVectorStart = samples.Length - vectorWidth;

            while (index <= lastVectorStart)
            {
                var values = new Vector<float>(samples.Slice(index, vectorWidth));
                (values * gainVector).CopyTo(samples.Slice(index, vectorWidth));
                index += vectorWidth;
            }
        }

        for (; index < samples.Length; index++)
        {
            samples[index] *= gain;
        }
    }

    public static void AddInPlace(Span<float> destination, ReadOnlySpan<float> source, float gain = 1f)
    {
        if (destination.Length != source.Length)
        {
            throw new ArgumentException("Source and destination lengths must match.");
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] += source[i] * gain;
        }
    }

    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0f;

        foreach (float sample in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        return peak;
    }

    public static void NormalizePeak(Span<float> samples, float targetPeak = 0.98f)
    {
        if (targetPeak is <= 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPeak));
        }

        float peak = Peak(samples);
        if (peak <= float.Epsilon)
        {
            return;
        }

        ApplyGain(samples, targetPeak / peak);
    }
}

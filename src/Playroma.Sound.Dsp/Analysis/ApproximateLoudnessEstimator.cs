namespace Playroma.Sound.Dsp.Analysis;

/// <summary>
/// Lightweight ungated programme-loudness estimate derived from mean-square energy.
/// It is useful for relative preset matching, but is not a standards-compliant LUFS meter.
/// </summary>
public static class ApproximateLoudnessEstimator
{
    public static float EstimateDecibels(
        ReadOnlySpan<float> interleavedSamples,
        int frameCount,
        int channels)
    {
        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        if (channels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        int sampleCount = checked(frameCount * channels);
        if (interleavedSamples.Length < sampleCount)
        {
            throw new ArgumentException("The sample span is too small.", nameof(interleavedSamples));
        }

        if (sampleCount == 0)
        {
            return float.NegativeInfinity;
        }

        double energy = 0.0;
        for (int i = 0; i < sampleCount; i++)
        {
            double sample = interleavedSamples[i];
            energy += sample * sample;
        }

        double meanSquare = energy / sampleCount;
        return meanSquare > double.Epsilon
            ? (float)(-0.691 + (10.0 * Math.Log10(meanSquare)))
            : float.NegativeInfinity;
    }
}

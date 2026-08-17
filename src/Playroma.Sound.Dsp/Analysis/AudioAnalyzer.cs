namespace Playroma.Sound.Dsp.Analysis;

public static class AudioAnalyzer
{
    public static AnalysisSnapshot Analyze(
        ReadOnlySpan<float> interleavedSamples,
        int frameCount,
        int channels,
        float clippingThreshold = 1f)
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
            return default;
        }

        double sumSquares = 0.0;
        double sum = 0.0;
        float peak = 0f;
        int clipped = 0;
        int crossings = 0;
        float previousMono = 0f;
        bool hasPrevious = false;
        double stereoCross = 0.0;
        double stereoLeftSquares = 0.0;
        double stereoRightSquares = 0.0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float mono = 0f;
            int offset = frame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                float sample = interleavedSamples[offset + channel];
                float absolute = MathF.Abs(sample);
                peak = MathF.Max(peak, absolute);
                sumSquares += sample * sample;
                sum += sample;
                mono += sample;
                if (absolute >= clippingThreshold)
                {
                    clipped++;
                }
            }

            mono /= channels;
            if (hasPrevious && ((mono >= 0f && previousMono < 0f) || (mono < 0f && previousMono >= 0f)))
            {
                crossings++;
            }

            previousMono = mono;
            hasPrevious = true;

            if (channels >= 2)
            {
                float left = interleavedSamples[offset];
                float right = interleavedSamples[offset + 1];
                stereoCross += left * right;
                stereoLeftSquares += left * left;
                stereoRightSquares += right * right;
            }
        }

        float rms = (float)Math.Sqrt(sumSquares / sampleCount);
        float dc = (float)(sum / sampleCount);
        float crest = rms > float.Epsilon ? peak / rms : 0f;
        float zeroCrossingRate = frameCount > 1 ? crossings / (float)(frameCount - 1) : 0f;
        float correlation = 0f;

        if (channels >= 2)
        {
            double denominator = Math.Sqrt(stereoLeftSquares * stereoRightSquares);
            correlation = denominator > double.Epsilon
                ? (float)Math.Clamp(stereoCross / denominator, -1.0, 1.0)
                : 0f;
        }

        return new AnalysisSnapshot(peak, rms, dc, crest, zeroCrossingRate, correlation, clipped);
    }
}

using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Resampling;

/// <summary>
/// Offline Hann-windowed sinc resampler. It favors quality and determinism over speed.
/// </summary>
public static class WindowedSincResampler
{
    public static RenderedSound Resample(RenderedSound source, int targetSampleRate, int taps = 32)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetSampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        if (taps is < 8 or > 256 || (taps & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taps), "Tap count must be an even number from 8 to 256.");
        }

        if (source.Format.SampleRate == targetSampleRate)
        {
            return new RenderedSound(source.Format, (float[])source.Samples.Clone());
        }

        int channels = source.Format.Channels;
        int targetFrames = Math.Max(1, (int)Math.Round(
            source.FrameCount * targetSampleRate / (double)source.Format.SampleRate));
        var targetFormat = new AudioFormat(targetSampleRate, channels, source.Format.BlockFrames);
        float[] output = new float[checked(targetFrames * channels)];
        double sourceStep = source.Format.SampleRate / (double)targetSampleRate;
        double cutoff = Math.Min(1.0, targetSampleRate / (double)source.Format.SampleRate);
        int half = taps / 2;

        for (int frame = 0; frame < targetFrames; frame++)
        {
            double sourcePosition = frame * sourceStep;
            int center = (int)Math.Floor(sourcePosition);

            for (int channel = 0; channel < channels; channel++)
            {
                double sum = 0.0;
                double weightSum = 0.0;

                for (int tap = -half + 1; tap <= half; tap++)
                {
                    int sourceFrame = center + tap;
                    if ((uint)sourceFrame >= (uint)source.FrameCount)
                    {
                        continue;
                    }

                    double distance = sourcePosition - sourceFrame;
                    double normalizedTap = (tap + half - 1) / (double)(taps - 1);
                    double window = 0.5 - (0.5 * Math.Cos(Math.Tau * normalizedTap));
                    double sincArgument = Math.PI * distance * cutoff;
                    double sinc = Math.Abs(sincArgument) < 1e-12
                        ? 1.0
                        : Math.Sin(sincArgument) / sincArgument;
                    double weight = sinc * window * cutoff;
                    sum += source.Samples[(sourceFrame * channels) + channel] * weight;
                    weightSum += weight;
                }

                output[(frame * channels) + channel] = weightSum == 0.0
                    ? 0f
                    : (float)(sum / weightSum);
            }
        }

        return new RenderedSound(targetFormat, output);
    }
}

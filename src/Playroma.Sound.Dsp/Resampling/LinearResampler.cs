using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Resampling;

public static class LinearResampler
{
    public static RenderedSound Resample(RenderedSound source, int targetSampleRate)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetSampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
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

        for (int frame = 0; frame < targetFrames; frame++)
        {
            double position = frame * sourceStep;
            int firstFrame = Math.Min((int)Math.Floor(position), source.FrameCount - 1);
            int secondFrame = Math.Min(firstFrame + 1, source.FrameCount - 1);
            float fraction = (float)(position - Math.Floor(position));

            for (int channel = 0; channel < channels; channel++)
            {
                float first = source.Samples[(firstFrame * channels) + channel];
                float second = source.Samples[(secondFrame * channels) + channel];
                output[(frame * channels) + channel] = first + ((second - first) * fraction);
            }
        }

        return new RenderedSound(targetFormat, output);
    }
}

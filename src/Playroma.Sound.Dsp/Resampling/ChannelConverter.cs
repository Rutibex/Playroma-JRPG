using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Resampling;

public static class ChannelConverter
{
    public static RenderedSound Convert(RenderedSound source, int targetChannels)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetChannels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(targetChannels));
        }

        int sourceChannels = source.Format.Channels;
        if (sourceChannels == targetChannels)
        {
            return new RenderedSound(source.Format, (float[])source.Samples.Clone());
        }

        var targetFormat = new AudioFormat(
            source.Format.SampleRate,
            targetChannels,
            source.Format.BlockFrames);
        float[] output = new float[checked(source.FrameCount * targetChannels)];

        for (int frame = 0; frame < source.FrameCount; frame++)
        {
            int sourceOffset = frame * sourceChannels;
            int targetOffset = frame * targetChannels;

            if (targetChannels == 1)
            {
                float sum = 0f;
                for (int channel = 0; channel < sourceChannels; channel++)
                {
                    sum += source.Samples[sourceOffset + channel];
                }

                output[targetOffset] = sum / sourceChannels;
            }
            else if (sourceChannels == 1)
            {
                float mono = source.Samples[sourceOffset];
                for (int channel = 0; channel < targetChannels; channel++)
                {
                    output[targetOffset + channel] = mono;
                }
            }
            else
            {
                for (int channel = 0; channel < targetChannels; channel++)
                {
                    int sourceChannel = channel < sourceChannels ? channel : sourceChannels - 1;
                    output[targetOffset + channel] = source.Samples[sourceOffset + sourceChannel];
                }
            }
        }

        return new RenderedSound(targetFormat, output);
    }

    public static RenderedSound ApplyMatrix(RenderedSound source, float[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(matrix);
        int targetChannels = matrix.GetLength(0);
        int sourceChannels = matrix.GetLength(1);

        if (sourceChannels != source.Format.Channels || targetChannels is < 1 or > 32)
        {
            throw new ArgumentException("Matrix dimensions do not match the source and target channel counts.", nameof(matrix));
        }

        var targetFormat = new AudioFormat(source.Format.SampleRate, targetChannels, source.Format.BlockFrames);
        float[] output = new float[checked(source.FrameCount * targetChannels)];

        for (int frame = 0; frame < source.FrameCount; frame++)
        {
            for (int target = 0; target < targetChannels; target++)
            {
                float sum = 0f;
                for (int sourceChannel = 0; sourceChannel < sourceChannels; sourceChannel++)
                {
                    sum += source.Samples[(frame * sourceChannels) + sourceChannel] * matrix[target, sourceChannel];
                }

                output[(frame * targetChannels) + target] = sum;
            }
        }

        return new RenderedSound(targetFormat, output);
    }
}

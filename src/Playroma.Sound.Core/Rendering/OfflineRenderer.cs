using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;

namespace Playroma.Sound.Core.Rendering;

public static class OfflineRenderer
{
    public static RenderedSound Render(IAudioNode root, AudioFormat format, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(root);

        int totalFrames = format.FramesFromSeconds(durationSeconds);
        float[] samples = new float[checked(totalFrames * format.Channels)];

        root.Prepare(format);

        int framePosition = 0;
        while (framePosition < totalFrames)
        {
            int framesThisBlock = Math.Min(format.BlockFrames, totalFrames - framePosition);
            int sampleOffset = framePosition * format.Channels;
            int sampleCount = framesThisBlock * format.Channels;
            root.Process(samples.AsSpan(sampleOffset, sampleCount), framesThisBlock);
            framePosition += framesThisBlock;
        }

        return new RenderedSound(format, samples);
    }

    public static RenderedSound RenderUntilComplete(
        IAudioNode root,
        AudioFormat format,
        double maximumDurationSeconds = 60.0)
    {
        ArgumentNullException.ThrowIfNull(root);

        int maximumFrames = format.FramesFromSeconds(maximumDurationSeconds);
        float[] samples = new float[checked(maximumFrames * format.Channels)];

        root.Prepare(format);

        int framePosition = 0;
        while (framePosition < maximumFrames && !root.IsComplete)
        {
            int framesThisBlock = Math.Min(format.BlockFrames, maximumFrames - framePosition);
            int sampleOffset = framePosition * format.Channels;
            int sampleCount = framesThisBlock * format.Channels;
            root.Process(samples.AsSpan(sampleOffset, sampleCount), framesThisBlock);
            framePosition += framesThisBlock;
        }

        if (!root.IsComplete)
        {
            throw new InvalidOperationException("The node did not complete before the maximum render duration.");
        }

        Array.Resize(ref samples, checked(framePosition * format.Channels));
        return new RenderedSound(format, samples);
    }
}

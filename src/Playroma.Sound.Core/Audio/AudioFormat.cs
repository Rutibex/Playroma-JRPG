namespace Playroma.Sound.Core.Audio;

/// <summary>
/// Immutable description of an interleaved floating-point audio stream.
/// </summary>
public readonly record struct AudioFormat
{
    public AudioFormat(int sampleRate, int channels, int blockFrames = 512)
    {
        if (sampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be between 8,000 and 384,000 Hz.");
        }

        if (channels is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "Channel count must be between 1 and 32.");
        }

        if (blockFrames is < 1 or > 65_536)
        {
            throw new ArgumentOutOfRangeException(nameof(blockFrames), "Block size must be between 1 and 65,536 frames.");
        }

        SampleRate = sampleRate;
        Channels = channels;
        BlockFrames = blockFrames;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public int BlockFrames { get; }

    public int SamplesPerBlock => checked(BlockFrames * Channels);

    public int FramesFromSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        return checked((int)Math.Ceiling(seconds * SampleRate));
    }

    public TimeSpan DurationFromFrames(long frames)
    {
        if (frames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames));
        }

        return TimeSpan.FromSeconds(frames / (double)SampleRate);
    }
}

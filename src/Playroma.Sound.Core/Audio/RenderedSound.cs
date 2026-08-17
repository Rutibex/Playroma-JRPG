namespace Playroma.Sound.Core.Audio;

/// <summary>
/// Fully rendered interleaved floating-point audio.
/// </summary>
public sealed class RenderedSound
{
    public RenderedSound(AudioFormat format, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length % format.Channels != 0)
        {
            throw new ArgumentException("Interleaved sample length must be divisible by the channel count.", nameof(samples));
        }

        Format = format;
        Samples = samples;
    }

    public AudioFormat Format { get; }
    public float[] Samples { get; }
    public int FrameCount => Samples.Length / Format.Channels;
    public TimeSpan Duration => Format.DurationFromFrames(FrameCount);

    public Span<float> AsSpan() => Samples.AsSpan();
    public ReadOnlySpan<float> AsReadOnlySpan() => Samples.AsSpan();
}

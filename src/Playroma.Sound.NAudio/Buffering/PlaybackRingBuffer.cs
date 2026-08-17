namespace Playroma.Sound.NAudio.Buffering;

/// <summary>
/// Fixed-capacity, single-producer/single-consumer ring buffer for interleaved float frames.
/// The producer and consumer may run on different threads. No method allocates after construction.
/// </summary>
public sealed class PlaybackRingBuffer
{
    private readonly float[] _samples;
    private readonly int _frameMask;
    private long _readFrame;
    private long _writeFrame;

    public PlaybackRingBuffer(int channels, int requestedCapacityFrames)
    {
        if (channels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (requestedCapacityFrames < 2) throw new ArgumentOutOfRangeException(nameof(requestedCapacityFrames));

        Channels = channels;
        CapacityFrames = RoundUpPowerOfTwo(requestedCapacityFrames);
        _frameMask = CapacityFrames - 1;
        _samples = new float[checked(CapacityFrames * channels)];
    }

    public int Channels { get; }
    public int CapacityFrames { get; }
    public int AvailableReadFrames
    {
        get
        {
            long write = Volatile.Read(ref _writeFrame);
            long read = Volatile.Read(ref _readFrame);
            return checked((int)Math.Clamp(write - read, 0, CapacityFrames));
        }
    }

    public int AvailableWriteFrames => CapacityFrames - AvailableReadFrames;
    public double FillRatio => AvailableReadFrames / (double)CapacityFrames;

    public int Write(ReadOnlySpan<float> source, int requestedFrames)
    {
        if (requestedFrames < 0) throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        int requestedSamples = checked(requestedFrames * Channels);
        if (source.Length < requestedSamples) throw new ArgumentException("Source span is too small.", nameof(source));

        long read = Volatile.Read(ref _readFrame);
        long write = Volatile.Read(ref _writeFrame);
        int available = checked((int)Math.Clamp(CapacityFrames - (write - read), 0, CapacityFrames));
        int frames = Math.Min(requestedFrames, available);
        CopyIntoRing(source, frames, write);
        Volatile.Write(ref _writeFrame, write + frames);
        return frames;
    }

    public int Read(Span<float> destination, int requestedFrames)
    {
        if (requestedFrames < 0) throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        int requestedSamples = checked(requestedFrames * Channels);
        if (destination.Length < requestedSamples) throw new ArgumentException("Destination span is too small.", nameof(destination));

        long write = Volatile.Read(ref _writeFrame);
        long read = Volatile.Read(ref _readFrame);
        int available = checked((int)Math.Clamp(write - read, 0, CapacityFrames));
        int frames = Math.Min(requestedFrames, available);
        CopyFromRing(destination, frames, read);
        Volatile.Write(ref _readFrame, read + frames);
        return frames;
    }

    public bool TryReadFrame(Span<float> destination)
    {
        if (destination.Length < Channels) throw new ArgumentException("Destination span is too small.", nameof(destination));

        long write = Volatile.Read(ref _writeFrame);
        long read = Volatile.Read(ref _readFrame);
        if (write == read) return false;

        int frameIndex = checked((int)(read & _frameMask));
        _samples.AsSpan(frameIndex * Channels, Channels).CopyTo(destination);
        Volatile.Write(ref _readFrame, read + 1);
        return true;
    }

    /// <summary>
    /// Clears buffered audio. Call only while the producer and consumer are stopped.
    /// </summary>
    public void Clear()
    {
        _samples.AsSpan().Clear();
        Volatile.Write(ref _readFrame, 0);
        Volatile.Write(ref _writeFrame, 0);
    }

    private void CopyIntoRing(ReadOnlySpan<float> source, int frames, long writeFrame)
    {
        if (frames == 0) return;
        int startFrame = checked((int)(writeFrame & _frameMask));
        int firstFrames = Math.Min(frames, CapacityFrames - startFrame);
        int firstSamples = checked(firstFrames * Channels);
        source[..firstSamples].CopyTo(_samples.AsSpan(startFrame * Channels, firstSamples));

        int remainingFrames = frames - firstFrames;
        if (remainingFrames > 0)
        {
            int remainingSamples = checked(remainingFrames * Channels);
            source.Slice(firstSamples, remainingSamples).CopyTo(_samples.AsSpan(0, remainingSamples));
        }
    }

    private void CopyFromRing(Span<float> destination, int frames, long readFrame)
    {
        if (frames == 0) return;
        int startFrame = checked((int)(readFrame & _frameMask));
        int firstFrames = Math.Min(frames, CapacityFrames - startFrame);
        int firstSamples = checked(firstFrames * Channels);
        _samples.AsSpan(startFrame * Channels, firstSamples).CopyTo(destination[..firstSamples]);

        int remainingFrames = frames - firstFrames;
        if (remainingFrames > 0)
        {
            int remainingSamples = checked(remainingFrames * Channels);
            _samples.AsSpan(0, remainingSamples).CopyTo(destination.Slice(firstSamples, remainingSamples));
        }
    }

    private static int RoundUpPowerOfTwo(int value)
    {
        if (value > 1 << 29) throw new ArgumentOutOfRangeException(nameof(value));
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}

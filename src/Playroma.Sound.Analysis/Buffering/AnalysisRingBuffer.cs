namespace Playroma.Sound.Analysis.Buffering;

/// <summary>Fixed-capacity SPSC interleaved-float ring used by real-time analysis taps.</summary>
internal sealed class AnalysisRingBuffer
{
    private readonly float[] _samples;
    private readonly int _channels;
    private readonly int _capacityFrames;
    private long _writeFrame;
    private long _readFrame;

    public AnalysisRingBuffer(int channels, int capacityFrames)
    {
        if (channels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (capacityFrames < 2) throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        _channels = channels;
        _capacityFrames = capacityFrames;
        _samples = new float[checked(channels * capacityFrames)];
    }

    public int Channels => _channels;
    public int CapacityFrames => _capacityFrames;
    public int AvailableReadFrames => (int)Math.Min(_capacityFrames, Math.Max(0, Volatile.Read(ref _writeFrame) - Volatile.Read(ref _readFrame)));
    public int AvailableWriteFrames => _capacityFrames - AvailableReadFrames;

    public int Write(ReadOnlySpan<float> source, int frameCount)
    {
        if (frameCount < 0 || source.Length < checked(frameCount * _channels)) throw new ArgumentOutOfRangeException(nameof(frameCount));
        long write = Volatile.Read(ref _writeFrame);
        long read = Volatile.Read(ref _readFrame);
        int writable = Math.Min(frameCount, _capacityFrames - (int)Math.Min(_capacityFrames, Math.Max(0, write - read)));
        for (int frame = 0; frame < writable; frame++)
        {
            int targetFrame = (int)((write + frame) % _capacityFrames);
            source.Slice(frame * _channels, _channels).CopyTo(_samples.AsSpan(targetFrame * _channels, _channels));
        }
        if (writable > 0) Volatile.Write(ref _writeFrame, write + writable);
        return writable;
    }

    public int Read(Span<float> destination, int frameCount)
    {
        if (frameCount < 0 || destination.Length < checked(frameCount * _channels)) throw new ArgumentOutOfRangeException(nameof(frameCount));
        long read = Volatile.Read(ref _readFrame);
        long write = Volatile.Read(ref _writeFrame);
        int readable = Math.Min(frameCount, (int)Math.Min(_capacityFrames, Math.Max(0, write - read)));
        for (int frame = 0; frame < readable; frame++)
        {
            int sourceFrame = (int)((read + frame) % _capacityFrames);
            _samples.AsSpan(sourceFrame * _channels, _channels).CopyTo(destination.Slice(frame * _channels, _channels));
        }
        if (readable > 0) Volatile.Write(ref _readFrame, read + readable);
        return readable;
    }

    public void Clear() => Volatile.Write(ref _readFrame, Volatile.Read(ref _writeFrame));
}

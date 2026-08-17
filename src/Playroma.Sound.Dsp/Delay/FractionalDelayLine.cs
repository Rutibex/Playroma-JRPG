namespace Playroma.Sound.Dsp.Delay;

/// <summary>
/// Interleaved multichannel circular delay with linear fractional interpolation.
/// </summary>
public sealed class FractionalDelayLine
{
    private float[] _buffer = [];
    private int _channels;
    private int _bufferFrames;
    private int _writeFrame;

    public int MaximumDelayFrames => Math.Max(0, _bufferFrames - 2);

    public void Prepare(int channels, int maximumDelayFrames)
    {
        if (channels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (maximumDelayFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelayFrames));
        }

        _channels = channels;
        _bufferFrames = checked(maximumDelayFrames + 2);
        _buffer = new float[checked(_bufferFrames * channels)];
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _writeFrame = 0;
    }

    public float Read(int channel, float delayFrames)
    {
        EnsurePrepared();
        if ((uint)channel >= (uint)_channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        delayFrames = Math.Clamp(delayFrames, 0f, MaximumDelayFrames);
        double readPosition = _writeFrame - delayFrames;
        while (readPosition < 0)
        {
            readPosition += _bufferFrames;
        }

        int firstFrame = (int)Math.Floor(readPosition) % _bufferFrames;
        int secondFrame = firstFrame + 1;
        if (secondFrame >= _bufferFrames)
        {
            secondFrame = 0;
        }

        float fraction = (float)(readPosition - Math.Floor(readPosition));
        float first = _buffer[(firstFrame * _channels) + channel];
        float second = _buffer[(secondFrame * _channels) + channel];
        return first + ((second - first) * fraction);
    }

    public void WriteFrame(ReadOnlySpan<float> frame)
    {
        EnsurePrepared();
        if (frame.Length < _channels)
        {
            throw new ArgumentException("The frame span does not contain every channel.", nameof(frame));
        }

        int destination = _writeFrame * _channels;
        frame[.._channels].CopyTo(_buffer.AsSpan(destination, _channels));
        _writeFrame++;
        if (_writeFrame >= _bufferFrames)
        {
            _writeFrame = 0;
        }
    }

    private void EnsurePrepared()
    {
        if (_buffer.Length == 0)
        {
            throw new InvalidOperationException("The delay line must be prepared before use.");
        }
    }
}

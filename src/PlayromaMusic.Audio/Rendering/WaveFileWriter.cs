using System.Text;

namespace PlayromaMusic.Audio.Rendering;

public sealed class WaveFileWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private readonly long _riffSizePosition;
    private readonly long _dataSizePosition;
    private long _dataBytes;
    private bool _disposed;

    public WaveFileWriter(string path, int sampleRate, short channels = 2, short bitsPerSample = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        _writer = new BinaryWriter(File.Create(fullPath), Encoding.ASCII, leaveOpen: false);

        WriteFourCc("RIFF");
        _riffSizePosition = _writer.BaseStream.Position;
        _writer.Write(0u);
        WriteFourCc("WAVE");
        WriteFourCc("fmt ");
        _writer.Write(16u);
        _writer.Write((short)1);
        _writer.Write(channels);
        _writer.Write(sampleRate);
        int byteRate = checked(sampleRate * channels * bitsPerSample / 8);
        short blockAlign = checked((short)(channels * bitsPerSample / 8));
        _writer.Write(byteRate);
        _writer.Write(blockAlign);
        _writer.Write(bitsPerSample);
        WriteFourCc("data");
        _dataSizePosition = _writer.BaseStream.Position;
        _writer.Write(0u);
    }

    public long DataBytesWritten => _dataBytes;

    public void Write(ReadOnlySpan<byte> pcmData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.Write(pcmData);
        _dataBytes += pcmData.Length;
        if (_dataBytes > uint.MaxValue)
        {
            throw new InvalidOperationException("The WAV writer supports standard RIFF files up to 4 GB.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        long end = _writer.BaseStream.Position;
        _writer.BaseStream.Position = _dataSizePosition;
        _writer.Write((uint)_dataBytes);
        _writer.BaseStream.Position = _riffSizePosition;
        _writer.Write(checked((uint)(end - 8)));
        _writer.BaseStream.Position = end;
        _writer.Dispose();
    }

    private void WriteFourCc(string value)
    {
        if (value.Length != 4)
        {
            throw new ArgumentException("A RIFF FourCC must contain exactly four characters.", nameof(value));
        }

        _writer.Write(Encoding.ASCII.GetBytes(value));
    }
}

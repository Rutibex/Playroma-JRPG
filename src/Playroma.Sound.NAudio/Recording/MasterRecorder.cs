using System.Buffers.Binary;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.IO;
using Playroma.Sound.NAudio.Buffering;
using Playroma.Sound.NAudio.Playback;

namespace Playroma.Sound.NAudio.Recording;

/// <summary>
/// Records the post-conversion master output without writing to disk on the WASAPI callback thread.
/// Audio is copied into a bounded SPSC ring and encoded by a dedicated worker.
/// </summary>
public sealed class MasterRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly AudioFormat _format;
    private readonly PlaybackRingBuffer _ring;
    private readonly PlaybackTelemetry _telemetry;
    private readonly float[] _workerBuffer;
    private Thread? _worker;
    private StreamingWavFileWriter? _writer;
    private int _stopRequested;
    private int _recording;
    private Exception? _lastError;

    public MasterRecorder(AudioFormat format, int bufferMilliseconds, PlaybackTelemetry telemetry)
    {
        if (bufferMilliseconds is < 100 or > 60_000) throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        _format = format;
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        int capacityFrames = Math.Max(format.BlockFrames * 2, checked((int)Math.Ceiling(format.SampleRate * (bufferMilliseconds / 1_000.0))));
        _ring = new PlaybackRingBuffer(format.Channels, capacityFrames);
        _workerBuffer = new float[checked(format.BlockFrames * format.Channels)];
    }

    public AudioFormat Format => _format;
    public bool IsRecording => Volatile.Read(ref _recording) != 0;
    public Exception? LastError => Volatile.Read(ref _lastError);

    public void Start(string path, WavEncoding encoding = WavEncoding.Pcm16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (_gate)
        {
            if (IsRecording) throw new InvalidOperationException("Master recording is already active.");
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            _ring.Clear();
            Volatile.Write(ref _lastError, null);
            Volatile.Write(ref _stopRequested, 0);
            _writer = new StreamingWavFileWriter(fullPath, _format, encoding, _workerBuffer.Length);
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Playroma Sound master recorder"
            };
            try
            {
                Volatile.Write(ref _recording, 1);
                _telemetry.SetRecording(true);
                _worker.Start();
            }
            catch
            {
                Volatile.Write(ref _recording, 0);
                _telemetry.SetRecording(false);
                _worker = null;
                _writer.Dispose();
                _writer = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        Thread? worker;
        lock (_gate)
        {
            if (!IsRecording) return;
            Volatile.Write(ref _stopRequested, 1);
            worker = _worker;
        }

        worker?.Join();
        lock (_gate)
        {
            _worker = null;
            try
            {
                _writer?.Dispose();
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception);
                _telemetry.SetError(exception);
                throw;
            }
            finally
            {
                _writer = null;
                Volatile.Write(ref _recording, 0);
                _telemetry.SetRecording(false);
            }
        }
    }

    internal int TryWrite(ReadOnlySpan<float> samples, int requestedFrames)
    {
        if (!IsRecording) return 0;
        int written = _ring.Write(samples, requestedFrames);
        if (written < requestedFrames) _telemetry.AddRecordingDroppedFrames(requestedFrames - written);
        return written;
    }

    public void Dispose() => Stop();

    private void WorkerLoop()
    {
        try
        {
            while (Volatile.Read(ref _stopRequested) == 0 || _ring.AvailableReadFrames > 0)
            {
                int frames = _ring.Read(_workerBuffer, _format.BlockFrames);
                if (frames == 0)
                {
                    Thread.Sleep(2);
                    continue;
                }

                _writer!.Write(_workerBuffer.AsSpan(0, frames * _format.Channels), frames);
                _telemetry.AddRecordedFrames(frames);
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception);
            _telemetry.SetError(exception);
        }
    }

    private sealed class StreamingWavFileWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly AudioFormat _format;
        private readonly WavEncoding _encoding;
        private readonly byte[] _conversionBuffer;
        private long _dataBytes;
        private bool _disposed;

        public StreamingWavFileWriter(string path, AudioFormat format, WavEncoding encoding, int maximumSamplesPerWrite)
        {
            _format = format;
            _encoding = encoding;
            int bytesPerSample = GetBytesPerSample(encoding);
            _conversionBuffer = new byte[checked(maximumSamplesPerWrite * bytesPerSample)];
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            _stream.Write(new byte[44]);
        }

        public void Write(ReadOnlySpan<float> samples, int frames)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int sampleCount = checked(frames * _format.Channels);
            if (samples.Length < sampleCount) throw new ArgumentException("Sample span is too small.", nameof(samples));
            int byteCount = Encode(samples[..sampleCount], _conversionBuffer, _encoding);
            _stream.Write(_conversionBuffer, 0, byteCount);
            _dataBytes += byteCount;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                WriteHeader();
            }
            finally
            {
                _stream.Dispose();
            }
        }

        private void WriteHeader()
        {
            if (_dataBytes > uint.MaxValue - 36) throw new InvalidOperationException("The WAV recording exceeded the RIFF size limit.");
            int bits = GetBytesPerSample(_encoding) * 8;
            short formatCode = _encoding == WavEncoding.Float32 ? (short)3 : (short)1;
            int blockAlign = checked(_format.Channels * (bits / 8));
            int byteRate = checked(_format.SampleRate * blockAlign);
            Span<byte> header = stackalloc byte[44];
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)(36 + _dataBytes)));
            "WAVE"u8.CopyTo(header[8..]);
            "fmt "u8.CopyTo(header[12..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16);
            BinaryPrimitives.WriteInt16LittleEndian(header[20..], formatCode);
            BinaryPrimitives.WriteInt16LittleEndian(header[22..], checked((short)_format.Channels));
            BinaryPrimitives.WriteInt32LittleEndian(header[24..], _format.SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(header[28..], byteRate);
            BinaryPrimitives.WriteInt16LittleEndian(header[32..], checked((short)blockAlign));
            BinaryPrimitives.WriteInt16LittleEndian(header[34..], checked((short)bits));
            "data"u8.CopyTo(header[36..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[40..], checked((uint)_dataBytes));
            _stream.Position = 0;
            _stream.Write(header);
            _stream.Flush(true);
        }

        private static int Encode(ReadOnlySpan<float> source, Span<byte> destination, WavEncoding encoding)
        {
            int bytesPerSample = GetBytesPerSample(encoding);
            int byteCount = checked(source.Length * bytesPerSample);
            if (destination.Length < byteCount) throw new ArgumentException("Destination is too small.", nameof(destination));

            int offset = 0;
            foreach (float raw in source)
            {
                float sample = float.IsFinite(raw) ? Math.Clamp(raw, -1f, 1f) : 0f;
                switch (encoding)
                {
                    case WavEncoding.Pcm16:
                        BinaryPrimitives.WriteInt16LittleEndian(destination[offset..], (short)Math.Round(sample * short.MaxValue));
                        offset += 2;
                        break;
                    case WavEncoding.Pcm24:
                        int value24 = (int)Math.Round(sample * 8_388_607f);
                        destination[offset++] = (byte)(value24 & 0xFF);
                        destination[offset++] = (byte)((value24 >> 8) & 0xFF);
                        destination[offset++] = (byte)((value24 >> 16) & 0xFF);
                        break;
                    case WavEncoding.Pcm32:
                        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], (int)Math.Round(sample * int.MaxValue));
                        offset += 4;
                        break;
                    case WavEncoding.Float32:
                        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], BitConverter.SingleToInt32Bits(sample));
                        offset += 4;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(encoding));
                }
            }
            return byteCount;
        }

        private static int GetBytesPerSample(WavEncoding encoding) => encoding switch
        {
            WavEncoding.Pcm16 => 2,
            WavEncoding.Pcm24 => 3,
            WavEncoding.Pcm32 or WavEncoding.Float32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
    }
}

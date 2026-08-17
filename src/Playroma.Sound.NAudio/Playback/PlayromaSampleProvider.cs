using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using Playroma.Sound.Analysis.Realtime;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.NAudio.Buffering;
using Playroma.Sound.NAudio.Recording;

namespace Playroma.Sound.NAudio.Playback;

/// <summary>
/// NAudio pull provider backed by a Playroma float ring buffer. It performs linear sample-rate
/// conversion, deterministic channel mapping, non-finite sanitization, underrun silence, metering,
/// and an optional non-blocking master-recording tap.
/// </summary>
public sealed class PlayromaSampleProvider : IWaveProvider
{
    private readonly PlaybackRingBuffer _ring;
    private readonly AudioFormat _sourceFormat;
    private readonly PlaybackTelemetry _telemetry;
    private readonly float[] _currentSourceFrame;
    private readonly float[] _nextSourceFrame;
    private readonly float[] _interpolatedSourceFrame;
    private readonly double _sourceFramesPerOutputFrame;
    private MasterRecorder? _recorder;
    private AudioAnalysisTap? _analysisTap;
    private double _sourcePosition;
    private bool _hasCurrentSourceFrame;
    private bool _hasNextSourceFrame;
    private float _masterGain = 1f;

    public PlayromaSampleProvider(
        PlaybackRingBuffer ring,
        AudioFormat sourceFormat,
        int outputSampleRate,
        int outputChannels,
        PlaybackTelemetry telemetry)
    {
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
        if (ring.Channels != sourceFormat.Channels) throw new ArgumentException("Ring channel count must match the source format.", nameof(ring));
        if (outputSampleRate is < 8_000 or > 384_000) throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
        if (outputChannels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(outputChannels));
        _sourceFormat = sourceFormat;
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _sourceFramesPerOutputFrame = sourceFormat.SampleRate / (double)outputSampleRate;
        _currentSourceFrame = new float[sourceFormat.Channels];
        _nextSourceFrame = new float[sourceFormat.Channels];
        _interpolatedSourceFrame = new float[sourceFormat.Channels];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, outputChannels);
    }

    public WaveFormat WaveFormat { get; }

    public float MasterGain
    {
        get => Volatile.Read(ref _masterGain);
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 8f) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _masterGain, value);
        }
    }

    internal void SetRecorder(MasterRecorder? recorder) => Volatile.Write(ref _recorder, recorder);
    internal void SetAnalysisTap(AudioAnalysisTap? analysisTap) => Volatile.Write(ref _analysisTap, analysisTap);

    /// <summary>Resets interpolation state. Call only while the output device is stopped.</summary>
    public void Reset()
    {
        _sourcePosition = 0;
        _hasCurrentSourceFrame = false;
        _hasNextSourceFrame = false;
        _currentSourceFrame.AsSpan().Clear();
        _nextSourceFrame.AsSpan().Clear();
        _interpolatedSourceFrame.AsSpan().Clear();
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
        long started = Stopwatch.GetTimestamp();
        _telemetry.AddProviderCallback();

        int blockAlign = WaveFormat.BlockAlign;
        int requestedFrames = count / blockAlign;
        int alignedBytes = requestedFrames * blockAlign;
        Span<byte> destinationBytes = buffer.AsSpan(offset, alignedBytes);
        Span<float> destination = MemoryMarshal.Cast<byte, float>(destinationBytes);
        int outputChannels = WaveFormat.Channels;
        int renderedFrames = 0;
        long nonFinite = 0;
        double sumSquares = 0;
        float peak = 0;
        float gain = Volatile.Read(ref _masterGain);

        for (; renderedFrames < requestedFrames; renderedFrames++)
        {
            Span<float> outputFrame = destination.Slice(renderedFrames * outputChannels, outputChannels);
            if (!TryRenderOutputFrame(outputFrame)) break;
            for (int channel = 0; channel < outputChannels; channel++)
            {
                float sample = outputFrame[channel] * gain;
                if (!float.IsFinite(sample))
                {
                    sample = 0f;
                    nonFinite++;
                }
                sample = Math.Clamp(sample, -1f, 1f);
                outputFrame[channel] = sample;
                float absolute = MathF.Abs(sample);
                if (absolute > peak) peak = absolute;
                sumSquares += sample * sample;
            }
        }

        int missingFrames = requestedFrames - renderedFrames;
        if (missingFrames > 0)
        {
            destination[(renderedFrames * outputChannels)..].Clear();
            _telemetry.AddUnderrun(missingFrames);
        }
        if (count > alignedBytes) buffer.AsSpan(offset + alignedBytes, count - alignedBytes).Clear();
        if (nonFinite > 0) _telemetry.AddNonFiniteSamples(nonFinite);
        int renderedSamples = requestedFrames * outputChannels;
        float rms = renderedSamples == 0 ? 0f : (float)Math.Sqrt(sumSquares / renderedSamples);
        _telemetry.RecordMasterLevels(peak, rms);

        AudioAnalysisTap? analysisTap = Volatile.Read(ref _analysisTap);
        analysisTap?.TryWrite(destination, requestedFrames);
        MasterRecorder? recorder = Volatile.Read(ref _recorder);
        recorder?.TryWrite(destination, requestedFrames);
        _telemetry.AddOutputFrames(requestedFrames);
        _telemetry.RecordCallbackDuration(Stopwatch.GetTimestamp() - started);
        return count;
    }

    private bool TryRenderOutputFrame(Span<float> outputFrame)
    {
        if (_sourceFramesPerOutputFrame == 1.0)
        {
            if (!_ring.TryReadFrame(_interpolatedSourceFrame)) return false;
            MapChannels(_interpolatedSourceFrame, outputFrame);
            return true;
        }

        if (!_hasCurrentSourceFrame)
        {
            if (!_ring.TryReadFrame(_currentSourceFrame)) return false;
            _hasCurrentSourceFrame = true;
        }
        if (!_hasNextSourceFrame)
        {
            if (!_ring.TryReadFrame(_nextSourceFrame)) return false;
            _hasNextSourceFrame = true;
        }

        float alpha = (float)_sourcePosition;
        for (int channel = 0; channel < _sourceFormat.Channels; channel++)
            _interpolatedSourceFrame[channel] = _currentSourceFrame[channel] + ((_nextSourceFrame[channel] - _currentSourceFrame[channel]) * alpha);
        MapChannels(_interpolatedSourceFrame, outputFrame);

        _sourcePosition += _sourceFramesPerOutputFrame;
        while (_sourcePosition >= 1.0)
        {
            _nextSourceFrame.AsSpan().CopyTo(_currentSourceFrame);
            _sourcePosition -= 1.0;
            if (!_ring.TryReadFrame(_nextSourceFrame))
            {
                _hasCurrentSourceFrame = false;
                _hasNextSourceFrame = false;
                _sourcePosition = 0;
                break;
            }
        }
        return true;
    }

    private static void MapChannels(ReadOnlySpan<float> source, Span<float> destination)
    {
        destination.Clear();
        if (source.Length == destination.Length)
        {
            source.CopyTo(destination);
            return;
        }

        if (source.Length == 1)
        {
            destination.Fill(source[0]);
            return;
        }

        if (destination.Length == 1)
        {
            float sum = 0;
            foreach (float sample in source) sum += sample;
            destination[0] = sum / source.Length;
            return;
        }

        if (destination.Length == 2 && source.Length > 2)
        {
            float left = 0;
            float right = 0;
            int leftCount = 0;
            int rightCount = 0;
            for (int channel = 0; channel < source.Length; channel++)
            {
                if ((channel & 1) == 0)
                {
                    left += source[channel];
                    leftCount++;
                }
                else
                {
                    right += source[channel];
                    rightCount++;
                }
            }
            destination[0] = leftCount == 0 ? 0 : left / leftCount;
            destination[1] = rightCount == 0 ? destination[0] : right / rightCount;
            return;
        }

        source[..Math.Min(source.Length, destination.Length)].CopyTo(destination);
    }
}

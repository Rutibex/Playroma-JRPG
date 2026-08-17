using System.Diagnostics;
using System.Numerics;
using Playroma.Sound.Analysis.Buffering;
using Playroma.Sound.Analysis.Spectral;
using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Analysis.Realtime;

/// <summary>
/// Non-blocking real-time audio analysis tap. The producer only copies into a bounded SPSC ring;
/// all FFT, waveform, loudness, phase, and spectrogram work occurs on a dedicated worker.
/// </summary>
public sealed class AudioAnalysisTap : IDisposable
{
    private readonly AudioFormat _format;
    private readonly AudioAnalysisOptions _options;
    private readonly AnalysisRingBuffer _ring;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _worker;
    private readonly float[] _readBuffer;
    private readonly float[] _monoWindow;
    private readonly float[] _leftWindow;
    private readonly float[] _rightWindow;
    private readonly Complex[] _fft;
    private readonly Queue<float[]> _spectrogram;
    private AudioAnalysisSnapshot _snapshot;
    private long _inputFrames;
    private long _droppedFrames;
    private long _nonFiniteSamples;
    private long _clippedSamples;
    private long _sequence;
    private int _windowFrames;
    private float _maximumPeak;
    private int _disposed;
    private int _resetRequested;

    public AudioAnalysisTap(AudioFormat format, AudioAnalysisOptions? options = null)
    {
        _format = format;
        _options = options ?? new AudioAnalysisOptions();
        _options.Validate();
        int capacityFrames = Math.Max(format.BlockFrames * 2, checked((int)Math.Ceiling(format.SampleRate * (_options.BufferMilliseconds / 1_000.0))));
        _ring = new AnalysisRingBuffer(format.Channels, capacityFrames);
        _readBuffer = new float[format.SamplesPerBlock];
        _monoWindow = new float[_options.FftSize];
        _leftWindow = new float[_options.FftSize];
        _rightWindow = new float[_options.FftSize];
        _fft = new Complex[_options.FftSize];
        _spectrogram = new Queue<float[]>(_options.SpectrogramRows);
        _snapshot = AudioAnalysisSnapshot.Empty(format);
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Playroma audio analysis", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    public AudioFormat Format => _format;
    public AudioAnalysisSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public int QueuedFrames => _ring.AvailableReadFrames;

    public bool TryWrite(ReadOnlySpan<float> interleavedSamples, int frameCount)
    {
        // Analysis taps can be detached while a device callback still holds a stale local reference.
        // A disposed tap therefore declines the block rather than throwing on the audio thread.
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (frameCount < 0 || interleavedSamples.Length < checked(frameCount * _format.Channels)) throw new ArgumentOutOfRangeException(nameof(frameCount));
        int sampleCount = checked(frameCount * _format.Channels);
        long nonFinite = 0;
        long clipped = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            float value = interleavedSamples[i];
            if (!float.IsFinite(value)) nonFinite++;
            else if (MathF.Abs(value) >= _options.ClippingThreshold) clipped++;
        }
        if (nonFinite > 0) Interlocked.Add(ref _nonFiniteSamples, nonFinite);
        if (clipped > 0) Interlocked.Add(ref _clippedSamples, clipped);
        int written = _ring.Write(interleavedSamples, frameCount);
        Interlocked.Add(ref _inputFrames, written);
        if (written < frameCount) Interlocked.Add(ref _droppedFrames, frameCount - written);
        return written == frameCount;
    }

    public void Reset()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _resetRequested, 1);
        Volatile.Write(ref _snapshot, AudioAnalysisSnapshot.Empty(_format));
    }

    private void WorkerLoop()
    {
        int minimumFramesPerUpdate = Math.Max(1, _format.SampleRate / _options.UpdateRateHz);
        int accumulated = 0;
        while (!_cancellation.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _resetRequested, 0) != 0) ResetWorkerState();
            int read = _ring.Read(_readBuffer, _format.BlockFrames);
            if (read == 0) { Thread.Sleep(2); continue; }
            AppendWindow(_readBuffer.AsSpan(0, read * _format.Channels), read);
            accumulated += read;
            if (accumulated < minimumFramesPerUpdate || _windowFrames < _options.FftSize) continue;
            accumulated %= minimumFramesPerUpdate;
            PublishSnapshot();
        }
    }

    private void ResetWorkerState()
    {
        _ring.Clear();
        Array.Clear(_monoWindow);
        Array.Clear(_leftWindow);
        Array.Clear(_rightWindow);
        _windowFrames = 0;
        _spectrogram.Clear();
        _maximumPeak = 0;
    }

    private void AppendWindow(ReadOnlySpan<float> samples, int frames)
    {
        int copyFrames = Math.Min(frames, _options.FftSize);
        int existing = Math.Min(_windowFrames, _options.FftSize - copyFrames);
        if (existing > 0)
        {
            _monoWindow.AsSpan(_windowFrames - existing, existing).CopyTo(_monoWindow.AsSpan(0, existing));
            _leftWindow.AsSpan(_windowFrames - existing, existing).CopyTo(_leftWindow.AsSpan(0, existing));
            _rightWindow.AsSpan(_windowFrames - existing, existing).CopyTo(_rightWindow.AsSpan(0, existing));
        }
        for (int frame = 0; frame < copyFrames; frame++)
        {
            double sum = 0;
            int sourceFrame = frames - copyFrames + frame;
            float left = 0;
            float right = 0;
            for (int channel = 0; channel < _format.Channels; channel++)
            {
                float raw = samples[(sourceFrame * _format.Channels) + channel];
                float value = float.IsFinite(raw) ? raw : 0f;
                if (channel == 0) left = value;
                if (channel == 1) right = value;
                sum += value;
            }
            if (_format.Channels == 1) right = left;
            _leftWindow[existing + frame] = left;
            _rightWindow[existing + frame] = right;
            _monoWindow[existing + frame] = (float)(sum / _format.Channels);
        }
        _windowFrames = Math.Min(_options.FftSize, existing + copyFrames);
    }

    private void PublishSnapshot()
    {
        long started = Stopwatch.GetTimestamp();
        int frames = _options.FftSize;
        float peak = 0;
        double sumSquares = 0;
        double dc = 0;
        for (int i = 0; i < frames; i++)
        {
            float value = _monoWindow[i];
            peak = Math.Max(peak, MathF.Abs(value));
            sumSquares += value * value;
            dc += value;
            double window = 0.5 - (0.5 * Math.Cos((2 * Math.PI * i) / (frames - 1)));
            _fft[i] = new Complex(value * window, 0);
        }
        _maximumPeak = Math.Max(_maximumPeak, peak);
        float rms = (float)Math.Sqrt(sumSquares / frames);
        float dcOffset = (float)(dc / frames);
        Radix2Fft.Transform(_fft);
        float[] spectrum = BuildSpectrum();
        float[] frequencies = new float[spectrum.Length];
        for (int i = 0; i < frequencies.Length; i++) frequencies[i] = i * (_format.SampleRate * 0.5f) / Math.Max(1, frequencies.Length - 1);
        float[] waveformMin = new float[_options.WaveformPoints];
        float[] waveformMax = new float[_options.WaveformPoints];
        BuildWaveform(waveformMin, waveformMax);
        _spectrogram.Enqueue((float[])spectrum.Clone());
        while (_spectrogram.Count > _options.SpectrogramRows) _spectrogram.Dequeue();
        float correlation = CalculateCorrelation();
        float lufs = rms <= 0.000001f ? -120f : Math.Max(-120f, -0.691f + (10f * MathF.Log10(rms * rms)));
        double elapsed = (Stopwatch.GetTimestamp() - started) * 1_000.0 / Stopwatch.Frequency;
        var spectrogram = _spectrogram.Select(row => (IReadOnlyList<float>)row).ToArray();
        var snapshot = new AudioAnalysisSnapshot(
            Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, _format, peak, rms, _maximumPeak, dcOffset, correlation, lufs,
            Interlocked.Read(ref _clippedSamples), Interlocked.Read(ref _nonFiniteSamples), Interlocked.Read(ref _inputFrames), Interlocked.Read(ref _droppedFrames),
            _ring.AvailableReadFrames, elapsed, waveformMin, waveformMax, spectrum, frequencies, spectrogram);
        Volatile.Write(ref _snapshot, snapshot);
    }

    private float[] BuildSpectrum()
    {
        int sourceBins = _options.FftSize / 2;
        float[] result = new float[_options.SpectrumBins];
        for (int output = 0; output < result.Length; output++)
        {
            int start = output * sourceBins / result.Length;
            int end = Math.Max(start + 1, (output + 1) * sourceBins / result.Length);
            double maximum = 0;
            for (int source = start; source < end; source++) maximum = Math.Max(maximum, _fft[source].Magnitude);
            double normalized = maximum / Math.Max(1, _options.FftSize / 2.0);
            result[output] = normalized <= 0.0000001 ? -140f : (float)Math.Max(-140, 20 * Math.Log10(normalized));
        }
        return result;
    }

    private void BuildWaveform(Span<float> minimum, Span<float> maximum)
    {
        for (int point = 0; point < minimum.Length; point++)
        {
            int start = point * _options.FftSize / minimum.Length;
            int end = Math.Max(start + 1, (point + 1) * _options.FftSize / minimum.Length);
            float low = float.PositiveInfinity;
            float high = float.NegativeInfinity;
            for (int index = start; index < end; index++) { low = Math.Min(low, _monoWindow[index]); high = Math.Max(high, _monoWindow[index]); }
            minimum[point] = float.IsFinite(low) ? low : 0;
            maximum[point] = float.IsFinite(high) ? high : 0;
        }
    }

    private float CalculateCorrelation()
    {
        if (_format.Channels < 2) return 1f;
        double product = 0;
        double leftSquares = 0;
        double rightSquares = 0;
        for (int i = 0; i < _options.FftSize; i++)
        {
            double left = _leftWindow[i];
            double right = _rightWindow[i];
            product += left * right;
            leftSquares += left * left;
            rightSquares += right * right;
        }
        double denominator = Math.Sqrt(leftSquares * rightSquares);
        if (denominator <= double.Epsilon) return 0f;
        return Math.Clamp((float)(product / denominator), -1f, 1f);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        _worker.Join();
        _cancellation.Dispose();
    }
}

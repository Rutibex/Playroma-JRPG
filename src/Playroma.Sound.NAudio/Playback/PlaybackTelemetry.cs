namespace Playroma.Sound.NAudio.Playback;

public enum WasapiPlaybackHostState
{
    Stopped,
    Starting,
    Playing,
    Recovering,
    Faulted,
    Disposed
}

public sealed record PlaybackTelemetrySnapshot(
    WasapiPlaybackHostState State,
    string DeviceId,
    string DeviceName,
    int SourceSampleRate,
    int SourceChannels,
    int OutputSampleRate,
    int OutputChannels,
    int RingCapacityFrames,
    int RingBufferedFrames,
    double RingFillRatio,
    long SourceFramesRendered,
    long OutputFramesDelivered,
    long ProviderCallbacks,
    long UnderrunCallbacks,
    long UnderrunFrames,
    long ProducerOverrunFrames,
    long NonFiniteSamplesReplaced,
    long DeviceFailures,
    long DeviceRecoveries,
    int CurrentRecoveryAttempt,
    double LastProducerRenderMilliseconds,
    double PeakProducerRenderMilliseconds,
    double LastProviderCallbackMilliseconds,
    double PeakProviderCallbackMilliseconds,
    float MasterPeak,
    float MasterRms,
    float MaximumMasterPeak,
    bool IsRecording,
    long RecordedFrames,
    long RecordingDroppedFrames,
    string LastError)
{
    public static PlaybackTelemetrySnapshot Empty { get; } = new(
        WasapiPlaybackHostState.Stopped, string.Empty, string.Empty,
        0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, false, 0, 0, string.Empty);
}

/// <summary>Thread-safe metrics published by the playback host and its callback provider.</summary>
public sealed class PlaybackTelemetry
{
    private int _state;
    private string _deviceId = string.Empty;
    private string _deviceName = string.Empty;
    private string _lastError = string.Empty;
    private int _sourceSampleRate;
    private int _sourceChannels;
    private int _outputSampleRate;
    private int _outputChannels;
    private int _ringCapacityFrames;
    private long _sourceFramesRendered;
    private long _outputFramesDelivered;
    private long _providerCallbacks;
    private long _underrunCallbacks;
    private long _underrunFrames;
    private long _producerOverrunFrames;
    private long _nonFiniteSamplesReplaced;
    private long _deviceFailures;
    private long _deviceRecoveries;
    private int _currentRecoveryAttempt;
    private long _lastProducerTicks;
    private long _peakProducerTicks;
    private long _lastCallbackTicks;
    private long _peakCallbackTicks;
    private int _masterPeakBits;
    private int _masterRmsBits;
    private int _maximumMasterPeakBits;
    private int _isRecording;
    private long _recordedFrames;
    private long _recordingDroppedFrames;
    private Func<int>? _bufferedFramesReader;
    private Func<double>? _fillRatioReader;

    public PlaybackTelemetrySnapshot Capture()
    {
        int buffered = _bufferedFramesReader?.Invoke() ?? 0;
        double fill = _fillRatioReader?.Invoke() ?? 0;
        return new PlaybackTelemetrySnapshot(
            (WasapiPlaybackHostState)Volatile.Read(ref _state),
            Volatile.Read(ref _deviceId),
            Volatile.Read(ref _deviceName),
            Volatile.Read(ref _sourceSampleRate),
            Volatile.Read(ref _sourceChannels),
            Volatile.Read(ref _outputSampleRate),
            Volatile.Read(ref _outputChannels),
            Volatile.Read(ref _ringCapacityFrames),
            buffered,
            fill,
            Interlocked.Read(ref _sourceFramesRendered),
            Interlocked.Read(ref _outputFramesDelivered),
            Interlocked.Read(ref _providerCallbacks),
            Interlocked.Read(ref _underrunCallbacks),
            Interlocked.Read(ref _underrunFrames),
            Interlocked.Read(ref _producerOverrunFrames),
            Interlocked.Read(ref _nonFiniteSamplesReplaced),
            Interlocked.Read(ref _deviceFailures),
            Interlocked.Read(ref _deviceRecoveries),
            Volatile.Read(ref _currentRecoveryAttempt),
            TicksToMilliseconds(Interlocked.Read(ref _lastProducerTicks)),
            TicksToMilliseconds(Interlocked.Read(ref _peakProducerTicks)),
            TicksToMilliseconds(Interlocked.Read(ref _lastCallbackTicks)),
            TicksToMilliseconds(Interlocked.Read(ref _peakCallbackTicks)),
            BitConverter.Int32BitsToSingle(Volatile.Read(ref _masterPeakBits)),
            BitConverter.Int32BitsToSingle(Volatile.Read(ref _masterRmsBits)),
            BitConverter.Int32BitsToSingle(Volatile.Read(ref _maximumMasterPeakBits)),
            Volatile.Read(ref _isRecording) != 0,
            Interlocked.Read(ref _recordedFrames),
            Interlocked.Read(ref _recordingDroppedFrames),
            Volatile.Read(ref _lastError));
    }

    internal void ConfigureSource(int sampleRate, int channels, int ringCapacityFrames, Func<int> bufferedFramesReader, Func<double> fillRatioReader)
    {
        Volatile.Write(ref _sourceSampleRate, sampleRate);
        Volatile.Write(ref _sourceChannels, channels);
        Volatile.Write(ref _ringCapacityFrames, ringCapacityFrames);
        _bufferedFramesReader = bufferedFramesReader;
        _fillRatioReader = fillRatioReader;
    }

    internal void ConfigureOutput(string deviceId, string deviceName, int sampleRate, int channels)
    {
        Volatile.Write(ref _deviceId, deviceId);
        Volatile.Write(ref _deviceName, deviceName);
        Volatile.Write(ref _outputSampleRate, sampleRate);
        Volatile.Write(ref _outputChannels, channels);
    }

    internal void SetState(WasapiPlaybackHostState state) => Volatile.Write(ref _state, (int)state);
    internal void SetError(Exception exception) => Volatile.Write(ref _lastError, exception.ToString());
    internal void ClearError() => Volatile.Write(ref _lastError, string.Empty);
    internal void AddSourceFrames(long frames) => Interlocked.Add(ref _sourceFramesRendered, frames);
    internal void AddOutputFrames(long frames) => Interlocked.Add(ref _outputFramesDelivered, frames);
    internal void AddProviderCallback() => Interlocked.Increment(ref _providerCallbacks);
    internal void AddUnderrun(long frames)
    {
        Interlocked.Increment(ref _underrunCallbacks);
        Interlocked.Add(ref _underrunFrames, frames);
    }
    internal void AddProducerOverrun(long frames) => Interlocked.Add(ref _producerOverrunFrames, frames);
    internal void AddNonFiniteSamples(long samples) => Interlocked.Add(ref _nonFiniteSamplesReplaced, samples);
    internal void AddDeviceFailure() => Interlocked.Increment(ref _deviceFailures);
    internal void AddDeviceRecovery() => Interlocked.Increment(ref _deviceRecoveries);
    internal void SetRecoveryAttempt(int attempt) => Volatile.Write(ref _currentRecoveryAttempt, attempt);
    internal void RecordMasterLevels(float peak, float rms)
    {
        if (!float.IsFinite(peak) || peak < 0f) peak = 0f;
        if (!float.IsFinite(rms) || rms < 0f) rms = 0f;
        Volatile.Write(ref _masterPeakBits, BitConverter.SingleToInt32Bits(peak));
        Volatile.Write(ref _masterRmsBits, BitConverter.SingleToInt32Bits(rms));
        SetMaximumFloat(ref _maximumMasterPeakBits, peak);
    }
    internal void SetRecording(bool value) => Volatile.Write(ref _isRecording, value ? 1 : 0);
    internal void AddRecordedFrames(long frames) => Interlocked.Add(ref _recordedFrames, frames);
    internal void AddRecordingDroppedFrames(long frames) => Interlocked.Add(ref _recordingDroppedFrames, frames);
    internal void RecordProducerDuration(long stopwatchTicks)
    {
        Interlocked.Exchange(ref _lastProducerTicks, stopwatchTicks);
        SetMaximum(ref _peakProducerTicks, stopwatchTicks);
    }
    internal void RecordCallbackDuration(long stopwatchTicks)
    {
        Interlocked.Exchange(ref _lastCallbackTicks, stopwatchTicks);
        SetMaximum(ref _peakCallbackTicks, stopwatchTicks);
    }

    private static void SetMaximum(ref long target, long value)
    {
        long current = Interlocked.Read(ref target);
        while (value > current)
        {
            long observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void SetMaximumFloat(ref int targetBits, float value)
    {
        int currentBits = Volatile.Read(ref targetBits);
        while (value > BitConverter.Int32BitsToSingle(currentBits))
        {
            int valueBits = BitConverter.SingleToInt32Bits(value);
            int observed = Interlocked.CompareExchange(ref targetBits, valueBits, currentBits);
            if (observed == currentBits) return;
            currentBits = observed;
        }
    }

    private static double TicksToMilliseconds(long ticks) => ticks * 1_000.0 / System.Diagnostics.Stopwatch.Frequency;
}

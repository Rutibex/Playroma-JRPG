using Playroma.Sound.NAudio.Devices;

namespace Playroma.Sound.NAudio.Playback;

public enum WasapiPlaybackShareMode
{
    Shared,
    Exclusive
}

/// <summary>Configuration for the Windows playback host.</summary>
public sealed class WasapiPlaybackOptions
{
    public AudioDeviceSelection Device { get; init; } = AudioDeviceSelection.Default();
    public WasapiPlaybackShareMode ShareMode { get; init; } = WasapiPlaybackShareMode.Shared;
    public bool UseEventSynchronization { get; init; } = true;
    public int LatencyMilliseconds { get; init; } = 80;
    public int BufferMilliseconds { get; init; } = 400;
    public int PrebufferMilliseconds { get; init; } = 100;
    public int? OutputSampleRate { get; init; }
    public int? OutputChannels { get; init; }
    public bool StartSourceAutomatically { get; init; } = true;
    public bool StopSourceAutomatically { get; init; } = true;
    public int SourceStopDrainMilliseconds { get; init; } = 5_000;
    public bool AutoRecoverDevice { get; init; } = true;
    public bool FallBackToDefaultDevice { get; init; } = true;
    public int DeviceRecoveryDelayMilliseconds { get; init; } = 750;
    public int MaximumRecoveryAttempts { get; init; } = 8;
    public int ProducerIdleSleepMilliseconds { get; init; } = 1;
    public int RecordingBufferMilliseconds { get; init; } = 2_000;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Device);
        if (LatencyMilliseconds is < 10 or > 5_000) throw new ArgumentOutOfRangeException(nameof(LatencyMilliseconds));
        if (BufferMilliseconds is < 20 or > 30_000) throw new ArgumentOutOfRangeException(nameof(BufferMilliseconds));
        if (PrebufferMilliseconds < 0 || PrebufferMilliseconds > BufferMilliseconds) throw new ArgumentOutOfRangeException(nameof(PrebufferMilliseconds));
        if (OutputSampleRate is < 8_000 or > 384_000) throw new ArgumentOutOfRangeException(nameof(OutputSampleRate));
        if (OutputChannels is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(OutputChannels));
        if (SourceStopDrainMilliseconds is < 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(SourceStopDrainMilliseconds));
        if (DeviceRecoveryDelayMilliseconds is < 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(DeviceRecoveryDelayMilliseconds));
        if (MaximumRecoveryAttempts is < 0 or > 1_000) throw new ArgumentOutOfRangeException(nameof(MaximumRecoveryAttempts));
        if (ProducerIdleSleepMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(ProducerIdleSleepMilliseconds));
        if (RecordingBufferMilliseconds is < 100 or > 60_000) throw new ArgumentOutOfRangeException(nameof(RecordingBufferMilliseconds));
    }
}

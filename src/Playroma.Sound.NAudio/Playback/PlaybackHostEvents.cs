namespace Playroma.Sound.NAudio.Playback;

public sealed class PlaybackHostFaultEventArgs : EventArgs
{
    public PlaybackHostFaultEventArgs(Exception exception, bool recoveryWillBeAttempted)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        RecoveryWillBeAttempted = recoveryWillBeAttempted;
    }

    public Exception Exception { get; }
    public bool RecoveryWillBeAttempted { get; }
}

public sealed class PlaybackDeviceEventArgs : EventArgs
{
    public PlaybackDeviceEventArgs(string deviceId, string deviceName)
    {
        DeviceId = deviceId ?? string.Empty;
        DeviceName = deviceName ?? string.Empty;
    }

    public string DeviceId { get; }
    public string DeviceName { get; }
}

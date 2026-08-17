namespace Playroma.Sound.NAudio.Devices;

public enum AudioDeviceRole
{
    Console,
    Multimedia,
    Communications
}

/// <summary>Selects either a specific WASAPI endpoint or the current default endpoint for a role.</summary>
public sealed record AudioDeviceSelection
{
    private AudioDeviceSelection(string? deviceId, AudioDeviceRole defaultRole)
    {
        DeviceId = deviceId;
        DefaultRole = defaultRole;
    }

    public string? DeviceId { get; }
    public AudioDeviceRole DefaultRole { get; }
    public bool UsesDefaultDevice => string.IsNullOrEmpty(DeviceId);

    public static AudioDeviceSelection Default(AudioDeviceRole role = AudioDeviceRole.Multimedia) => new(null, role);

    public static AudioDeviceSelection Specific(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return new AudioDeviceSelection(deviceId, AudioDeviceRole.Multimedia);
    }
}

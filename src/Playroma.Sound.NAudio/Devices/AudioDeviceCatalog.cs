using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Playroma.Sound.NAudio.Devices;

/// <summary>Enumerates and resolves active Windows Core Audio render endpoints.</summary>
public sealed class AudioDeviceCatalog
{
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        EnsureWindows();
        using var enumerator = new MMDeviceEnumerator();
        string? consoleId = TryGetDefaultId(enumerator, Role.Console);
        string? multimediaId = TryGetDefaultId(enumerator, Role.Multimedia);
        string? communicationsId = TryGetDefaultId(enumerator, Role.Communications);
        var result = new List<AudioDeviceInfo>();

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                using AudioClient audioClient = device.AudioClient;
                WaveFormat mix = audioClient.MixFormat;
                result.Add(new AudioDeviceInfo(
                    device.ID,
                    device.FriendlyName,
                    StringComparer.Ordinal.Equals(device.ID, consoleId),
                    StringComparer.Ordinal.Equals(device.ID, multimediaId),
                    StringComparer.Ordinal.Equals(device.ID, communicationsId),
                    mix.SampleRate,
                    mix.Channels,
                    mix.BitsPerSample,
                    mix.Encoding.ToString()));
            }
            finally
            {
                device.Dispose();
            }
        }

        return result.OrderByDescending(item => item.IsDefaultMultimedia)
            .ThenBy(item => item.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public AudioDeviceInfo GetSelectedDevice(AudioDeviceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        using MMDevice device = OpenDevice(selection);
        using AudioClient audioClient = device.AudioClient;
        WaveFormat mix = audioClient.MixFormat;
        return new AudioDeviceInfo(
            device.ID,
            device.FriendlyName,
            IsDefault(device.ID, AudioDeviceRole.Console),
            IsDefault(device.ID, AudioDeviceRole.Multimedia),
            IsDefault(device.ID, AudioDeviceRole.Communications),
            mix.SampleRate,
            mix.Channels,
            mix.BitsPerSample,
            mix.Encoding.ToString());
    }

    internal MMDevice OpenDevice(AudioDeviceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        EnsureWindows();
        var enumerator = new MMDeviceEnumerator();
        try
        {
            MMDevice device = selection.UsesDefaultDevice
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, ToRole(selection.DefaultRole))
                : enumerator.GetDevice(selection.DeviceId!);
            enumerator.Dispose();
            return device;
        }
        catch
        {
            enumerator.Dispose();
            throw;
        }
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDefault(string deviceId, AudioDeviceRole role)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, ToRole(role));
            return StringComparer.Ordinal.Equals(deviceId, device.ID);
        }
        catch
        {
            return false;
        }
    }

    internal static Role ToRole(AudioDeviceRole role) => role switch
    {
        AudioDeviceRole.Console => Role.Console,
        AudioDeviceRole.Multimedia => Role.Multimedia,
        AudioDeviceRole.Communications => Role.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WASAPI playback is available only on Windows.");
    }
}

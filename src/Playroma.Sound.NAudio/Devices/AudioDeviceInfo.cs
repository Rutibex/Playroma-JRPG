namespace Playroma.Sound.NAudio.Devices;

/// <summary>Serializable, NAudio-independent description of an active render endpoint.</summary>
public sealed record AudioDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefaultConsole,
    bool IsDefaultMultimedia,
    bool IsDefaultCommunications,
    int MixSampleRate,
    int MixChannels,
    int MixBitsPerSample,
    string MixEncoding);

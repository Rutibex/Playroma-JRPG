using Playroma.Sound.Core.Audio;
using Playroma.Sound.Streaming.Spatial;

namespace Playroma.Sound.Streaming.Runtime;

public interface IStreamingAudioSource
{
    AudioFormat Format { get; }
    long CurrentSampleFrame { get; }
    bool IsRunning { get; }
    void Start();
    void Stop();
    void Reset();
    int Render(Span<float> destination, int requestedFrames);
}

public enum VoiceStopReason
{
    Completed,
    Stolen,
    Stopped,
    Faulted
}

public readonly record struct SoundVoiceHandle(int Value)
{
    public bool IsValid => Value >= 0;
    public static SoundVoiceHandle Invalid { get; } = new(-1);
}

public readonly record struct VoiceStartOptions(
    string BusId,
    float Gain = 1f,
    float Pan = 0f,
    int Priority = 50,
    bool Loop = false,
    bool Protected = false,
    SoundEmitterState Spatial = default);

/// <summary>
/// Optional bounded real-time bus tap. Implementations must not allocate, block, load files, or
/// perform expensive analysis from <see cref="TryWriteBus"/>; copy into preallocated storage and return.
/// </summary>
public interface IStreamingBusTap
{
    bool TryWriteBus(string busId, ReadOnlySpan<float> interleavedSamples, int frameCount, AudioFormat format);
}

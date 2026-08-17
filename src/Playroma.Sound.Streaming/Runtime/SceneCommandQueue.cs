using Playroma.Sound.Streaming.Spatial;

namespace Playroma.Sound.Streaming.Runtime;

internal enum SceneCommandKind
{
    Start,
    Stop,
    Reset,
    CaptureSnapshot,
    RestoreSnapshot,
    SetParameter,
    Trigger,
    SetListener,
    SetListenerZone,
    SetLayerSpatial,
    SetLayerPreviewGain,
    SetEmitterPreviewGain,
    SetState,
    PlaySound,
    StopVoice,
    SetVoiceSpatial
}

internal readonly record struct SceneCommand(
    SceneCommandKind Kind,
    int TargetIndex = -1,
    float Value = 0f,
    SoundListenerState Listener = default,
    SoundEmitterState Emitter = default,
    int SecondaryIndex = -1,
    float SecondaryValue = 0f,
    int IntegerValue = 0,
    int Flags = 0,
    int HandleValue = -1,
    long RequestId = 0,
    object? Payload = null);

/// <summary>
/// Bounded single-producer/single-consumer queue. A StreamingSceneController can be
/// used when more than one control thread needs to submit commands.
/// </summary>
internal sealed class SceneCommandQueue
{
    private readonly SceneCommand[] _items;
    private int _read;
    private int _write;

    public SceneCommandQueue(int capacity)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new SceneCommand[capacity + 1];
    }

    public int Capacity => _items.Length - 1;

    public int Count
    {
        get
        {
            int read = Volatile.Read(ref _read);
            int write = Volatile.Read(ref _write);
            int count = write - read;
            return count >= 0 ? count : count + _items.Length;
        }
    }

    public bool TryEnqueue(SceneCommand item)
    {
        int write = Volatile.Read(ref _write);
        int next = write + 1;
        if (next == _items.Length) next = 0;
        if (next == Volatile.Read(ref _read)) return false;
        _items[write] = item;
        Volatile.Write(ref _write, next);
        return true;
    }

    public bool TryDequeue(out SceneCommand item)
    {
        int read = Volatile.Read(ref _read);
        if (read == Volatile.Read(ref _write))
        {
            item = default;
            return false;
        }
        item = _items[read];
        _items[read] = default;
        int next = read + 1;
        if (next == _items.Length) next = 0;
        Volatile.Write(ref _read, next);
        return true;
    }

    /// <summary>Consumer-side initialization helper. Do not call while producers are active.</summary>
    public void Clear()
    {
        Array.Clear(_items);
        Volatile.Write(ref _read, 0);
        Volatile.Write(ref _write, 0);
    }
}

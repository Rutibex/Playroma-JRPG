using Playroma.Sound.Core.Audio;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Streaming.Telemetry;

namespace Playroma.Sound.Streaming.Runtime;

/// <summary>
/// Real-time scene owner that supports prepared scene replacement with an allocation-free crossfade.
/// New scenes are compiled and instantiated on the control thread. Retired instances are returned to
/// the control thread for disposal so the audio callback never destroys a large graph object.
/// </summary>
public sealed class StreamingSceneHost : IStreamingAudioSource, IDisposable
{
    private readonly object _controlGate = new();
    private readonly HostCommandQueue _commands;
    private readonly ReferenceSpscQueue<StreamingSceneInstance> _retired;
    private readonly float[] _currentBuffer;
    private readonly float[] _replacementBuffer;
    private StreamingSceneInstance _current;
    private StreamingSceneInstance? _replacement;
    private StreamingSceneInstance? _blockedRetired;
    private int _crossfadeFrames;
    private int _crossfadePosition;
    private int _replacementPending;
    private int _desiredRunning;
    private int _lifecycleState;

    public StreamingSceneHost(PreparedSoundScene initialScene, int commandQueueCapacity = 32, int retiredQueueCapacity = 32)
    {
        ArgumentNullException.ThrowIfNull(initialScene);
        _current = initialScene.CreateInstance();
        Format = initialScene.Format;
        _commands = new HostCommandQueue(commandQueueCapacity);
        _retired = new ReferenceSpscQueue<StreamingSceneInstance>(retiredQueueCapacity);
        _currentBuffer = new float[Format.SamplesPerBlock];
        _replacementBuffer = new float[Format.SamplesPerBlock];
    }

    public AudioFormat Format { get; }
    public SoundSceneMetadata CurrentMetadata => Volatile.Read(ref _current).Metadata;
    public long CurrentSampleFrame => Volatile.Read(ref _current).CurrentSampleFrame;
    public bool IsRunning => Volatile.Read(ref _desiredRunning) != 0;
    public bool IsReplacementPending => Volatile.Read(ref _replacementPending) != 0;
    public StreamingTelemetrySnapshot Telemetry => Volatile.Read(ref _current).Telemetry;

    public bool TryStart() => TryEnqueueControl(new HostCommand(HostCommandKind.Start));
    public bool TryStop() => TryEnqueueControl(new HostCommand(HostCommandKind.Stop));
    public bool TryReset() => TryEnqueueControl(new HostCommand(HostCommandKind.Reset));
    public void Start() { if (!TryStart()) throw new InvalidOperationException("The scene host command queue is full."); }
    public void Stop() { if (!TryStop()) throw new InvalidOperationException("The scene host command queue is full."); }
    public void Reset() { if (!TryReset()) throw new InvalidOperationException("The scene host command queue is full."); }

    /// <summary>
    /// Queues a control for the current scene and, while crossfading, the replacement scene.
    /// A true result means the host accepted the command; individual scenes may ignore IDs
    /// they do not define.
    /// </summary>
    public bool TrySetParameter(string parameterId, float value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterId);
        return TryEnqueueControl(new HostCommand(HostCommandKind.SetParameter, Text: parameterId, Value: value));
    }

    public bool TryTrigger(string trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        return TryEnqueueControl(new HostCommand(HostCommandKind.Trigger, Text: trigger));
    }

    public bool TrySetListener(SoundListenerState listener) =>
        TryEnqueueControl(new HostCommand(HostCommandKind.SetListener, Listener: listener));

    public bool TrySetListenerZone(string zoneId) =>
        TryEnqueueControl(new HostCommand(HostCommandKind.SetListenerZone, Text: zoneId ?? string.Empty));

    public bool TrySetLayerSpatialState(string layerId, SoundEmitterState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        return TryEnqueueControl(new HostCommand(HostCommandKind.SetLayerSpatial, Text: layerId, Emitter: state));
    }

    public bool TryReplaceScene(PreparedSoundScene preparedScene, int crossfadeFrames = 2048)
    {
        ArgumentNullException.ThrowIfNull(preparedScene);
        ThrowIfDisposed();
        if (preparedScene.Format != Format)
            throw new InvalidOperationException("A replacement scene must use the same prepared audio format as the host.");
        if (crossfadeFrames < 0) throw new ArgumentOutOfRangeException(nameof(crossfadeFrames));

        lock (_controlGate)
        {
            if (Interlocked.CompareExchange(ref _replacementPending, 1, 0) != 0) return false;
            StreamingSceneInstance replacement = preparedScene.CreateInstance();
            if (_commands.TryEnqueue(new HostCommand(HostCommandKind.Replace, replacement, crossfadeFrames))) return true;
            replacement.Dispose();
            Volatile.Write(ref _replacementPending, 0);
            return false;
        }
    }

    public int Render(Span<float> destination, int requestedFrames)
    {
        EnterRender();
        try
        {
            if (requestedFrames < 0 || requestedFrames > Format.BlockFrames) throw new ArgumentOutOfRangeException(nameof(requestedFrames));
            int sampleCount = checked(requestedFrames * Format.Channels);
            if (destination.Length < sampleCount) throw new ArgumentException("Destination is too small.", nameof(destination));
            destination = destination[..sampleCount];
            destination.Clear();
            DrainCommands();

            StreamingSceneInstance? replacement = _replacement;
            if (replacement is null)
            {
                return _current.Render(destination, requestedFrames);
            }

            Span<float> current = _currentBuffer.AsSpan(0, sampleCount);
            Span<float> next = _replacementBuffer.AsSpan(0, sampleCount);
            current.Clear();
            next.Clear();
            _current.Render(current, requestedFrames);
            replacement.Render(next, requestedFrames);

            int channels = Format.Channels;
            for (int frame = 0; frame < requestedFrames; frame++)
            {
                float alpha = _crossfadeFrames <= 0
                    ? 1f
                    : Math.Clamp((_crossfadePosition + frame) / (float)_crossfadeFrames, 0f, 1f);
                float oldGain = 1f - alpha;
                int offset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                    destination[offset + channel] = (current[offset + channel] * oldGain) + (next[offset + channel] * alpha);
            }

            _crossfadePosition += requestedFrames;
            if (_crossfadeFrames <= 0 || _crossfadePosition >= _crossfadeFrames) CompleteReplacement();
            return requestedFrames;
        }
        finally
        {
            ExitRender();
        }
    }

    public bool TryDequeueRetired(out StreamingSceneInstance? retired)
    {
        if (_retired.TryDequeue(out retired)) return true;
        retired = Interlocked.Exchange(ref _blockedRetired, null);
        if (retired is null) return false;
        Volatile.Write(ref _replacementPending, 0);
        return true;
    }

    public int DrainRetiredInstances()
    {
        int count = 0;
        while (TryDequeueRetired(out StreamingSceneInstance? retired))
        {
            retired!.Dispose();
            count++;
        }
        return count;
    }

    public void Dispose()
    {
        int previous = Interlocked.CompareExchange(ref _lifecycleState, 2, 0);
        if (previous == 2) return;
        if (previous == 1)
            throw new InvalidOperationException("Cannot dispose the scene host while Render is active. Stop the device callback first.");

        lock (_controlGate)
        {
            _current.Dispose();
            _replacement?.Dispose();
            _replacement = null;
            while (_commands.TryDequeue(out HostCommand pending)) pending.Replacement?.Dispose();
            while (_retired.TryDequeue(out StreamingSceneInstance? retired)) retired!.Dispose();
            Interlocked.Exchange(ref _blockedRetired, null)?.Dispose();
        }
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out HostCommand command))
        {
            switch (command.Kind)
            {
                case HostCommandKind.Start:
                    Volatile.Write(ref _desiredRunning, 1);
                    _current.TryStart();
                    _replacement?.TryStart();
                    break;
                case HostCommandKind.Stop:
                    Volatile.Write(ref _desiredRunning, 0);
                    _current.TryStop();
                    _replacement?.TryStop();
                    break;
                case HostCommandKind.Reset:
                    _current.TryRequestReset();
                    _replacement?.TryRequestReset();
                    _crossfadePosition = 0;
                    break;
                case HostCommandKind.Replace:
                    BeginReplacement(command.Replacement!, command.CrossfadeFrames);
                    break;
                case HostCommandKind.SetParameter:
                    _current.TrySetParameter(command.Text!, command.Value);
                    _replacement?.TrySetParameter(command.Text!, command.Value);
                    break;
                case HostCommandKind.Trigger:
                    _current.TryTrigger(command.Text!);
                    _replacement?.TryTrigger(command.Text!);
                    break;
                case HostCommandKind.SetListener:
                    _current.TrySetListener(command.Listener);
                    _replacement?.TrySetListener(command.Listener);
                    break;
                case HostCommandKind.SetListenerZone:
                    _current.TrySetListenerZone(command.Text ?? string.Empty);
                    _replacement?.TrySetListenerZone(command.Text ?? string.Empty);
                    break;
                case HostCommandKind.SetLayerSpatial:
                    _current.TrySetLayerSpatialState(command.Text!, command.Emitter);
                    _replacement?.TrySetLayerSpatialState(command.Text!, command.Emitter);
                    break;
            }
        }
    }

    private void BeginReplacement(StreamingSceneInstance replacement, int crossfadeFrames)
    {
        if (_replacement is not null)
        {
            // This should only occur if a caller bypassed IsReplacementPending. Keep disposal off-thread.
            Retire(_replacement);
        }
        _replacement = replacement;
        _crossfadeFrames = crossfadeFrames;
        _crossfadePosition = 0;
        if (Volatile.Read(ref _desiredRunning) != 0) replacement.TryStart();
        if (crossfadeFrames == 0) CompleteReplacement();
    }

    private void CompleteReplacement()
    {
        StreamingSceneInstance? replacement = _replacement;
        if (replacement is null) return;
        StreamingSceneInstance retired = _current;
        Volatile.Write(ref _current, replacement);
        _replacement = null;
        _crossfadeFrames = 0;
        _crossfadePosition = 0;
        if (Retire(retired)) Volatile.Write(ref _replacementPending, 0);
    }

    private bool Retire(StreamingSceneInstance instance)
    {
        if (_retired.TryEnqueue(instance)) return true;
        // IsReplacementPending prevents another replacement until this slot is drained,
        // so a single bounded overflow slot is sufficient and keeps disposal off the audio thread.
        Volatile.Write(ref _blockedRetired, instance);
        return false;
    }

    private bool TryEnqueueControl(HostCommand command)
    {
        ThrowIfDisposed();
        lock (_controlGate) return _commands.TryEnqueue(command);
    }

    private void EnterRender()
    {
        ThrowIfDisposed();
        int previous = Interlocked.CompareExchange(ref _lifecycleState, 1, 0);
        if (previous == 2) throw new ObjectDisposedException(nameof(StreamingSceneHost));
        if (previous != 0) throw new InvalidOperationException("StreamingSceneHost supports exactly one render thread.");
    }

    private void ExitRender() => Volatile.Write(ref _lifecycleState, 0);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _lifecycleState) == 2, this);

    private enum HostCommandKind
    {
        Start,
        Stop,
        Reset,
        Replace,
        SetParameter,
        Trigger,
        SetListener,
        SetListenerZone,
        SetLayerSpatial
    }

    private readonly record struct HostCommand(
        HostCommandKind Kind,
        StreamingSceneInstance? Replacement = null,
        int CrossfadeFrames = 0,
        string? Text = null,
        float Value = 0f,
        SoundListenerState Listener = default,
        SoundEmitterState Emitter = default);

    private sealed class HostCommandQueue
    {
        private readonly HostCommand[] _items;
        private int _read;
        private int _write;

        public HostCommandQueue(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new HostCommand[capacity + 1];
        }

        public bool TryEnqueue(HostCommand item)
        {
            int write = Volatile.Read(ref _write);
            int next = write + 1;
            if (next == _items.Length) next = 0;
            if (next == Volatile.Read(ref _read)) return false;
            _items[write] = item;
            Volatile.Write(ref _write, next);
            return true;
        }

        public bool TryDequeue(out HostCommand item)
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
    }

    private sealed class ReferenceSpscQueue<T> where T : class
    {
        private readonly T?[] _items;
        private int _read;
        private int _write;

        public ReferenceSpscQueue(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T?[capacity + 1];
        }

        public bool TryEnqueue(T item)
        {
            int write = Volatile.Read(ref _write);
            int next = write + 1;
            if (next == _items.Length) next = 0;
            if (next == Volatile.Read(ref _read)) return false;
            _items[write] = item;
            Volatile.Write(ref _write, next);
            return true;
        }

        public bool TryDequeue(out T? item)
        {
            int read = Volatile.Read(ref _read);
            if (read == Volatile.Read(ref _write))
            {
                item = null;
                return false;
            }
            item = _items[read];
            _items[read] = null;
            int next = read + 1;
            if (next == _items.Length) next = 0;
            Volatile.Write(ref _read, next);
            return item is not null;
        }
    }
}

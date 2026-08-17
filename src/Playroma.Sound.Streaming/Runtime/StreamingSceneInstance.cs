using System.Diagnostics;
using System.Numerics;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;
using Playroma.Sound.Core.Random;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Snapshots;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Streaming.Telemetry;

namespace Playroma.Sound.Streaming.Runtime;

/// <summary>
/// Prepared, allocation-free block renderer for one long-duration sound scene.
/// Public control methods enqueue bounded commands; graph compilation and DSP allocation occur in the constructor.
/// </summary>
public sealed partial class StreamingSceneInstance : IStreamingAudioSource, IAudioNode, IDisposable
{
    private readonly PreparedSoundScene _prepared;
    private readonly SoundSceneDefinition _definition;
    private readonly SceneCommandQueue _commands;
    private readonly Dictionary<string, int> _parameterIndices;
    private readonly Dictionary<string, int> _layerIndices;
    private readonly Dictionary<string, int> _emitterIndices;
    private readonly Dictionary<string, int> _stateMachineIndices;
    private readonly Dictionary<string, int> _triggerIndices;
    private readonly string[] _triggers;
    private readonly Dictionary<string, int> _busIndices;
    private readonly Dictionary<string, int> _soundIndices;
    private readonly Dictionary<string, int> _zoneIndices;
    private readonly float[,] _zoneTransmission;
    private readonly int[] _zoneEnvironmentBusIndices;
    private readonly float[] _zoneEnvironmentGains;
    private readonly SceneParameterRuntime[] _parameters;
    private readonly CompiledBinding[] _bindings;
    private readonly BusRuntime[] _buses;
    private readonly int[] _busProcessingOrder;
    private readonly int _masterBusIndex;
    private readonly LayerRuntime[] _layers;
    private readonly EventEmitterRuntime[] _emitters;
    private readonly EventClusterRuntime[] _clusters;
    private readonly StateMachineRuntime[] _stateMachines;
    private readonly VoiceRuntime[] _voices;
    private readonly SoundPool[] _soundPools;
    private readonly float[] _voiceScratch;
    private readonly float[] _catchUpScratch;
    private readonly float[] _retirementTailValues;
    private readonly int[] _retirementTailRemaining;
    private readonly int[] _retirementTailBus;
    private readonly int _maximumVoices;
    private readonly int _voiceFadeFrames;
    private readonly bool _virtualizationEnabled;
    private readonly int _virtualCatchUpBudgetFrames;
    private readonly int _stopTailFrames;
    private readonly int _telemetryIntervalBlocks;
    private SoundListenerState _listener = SoundListenerState.Default;
    private int _listenerZoneIndex = -1;
    private long _sampleFrame;
    private long _voiceSequence;
    private int _nextVoiceHandle;
    private long _startedVoices;
    private long _completedVoices;
    private long _stolenVoices;
    private long _rejectedVoices;
    private long _faultedVoices;
    private long _queueOverflows;
    private double _peakRenderMilliseconds;
    private int _telemetryCountdown;
    private double _lastRenderMilliseconds;
    private int _activeLimitVoices;
    private bool _isRunning;
    private bool _stopping;
    private int _stopTailFramesRemaining;
    private bool _disposed;
    private IStreamingBusTap? _busTap;
    private readonly IReadOnlyList<string> _busIds;

    internal StreamingSceneInstance(PreparedSoundScene prepared)
    {
        _prepared = prepared;
        _definition = prepared.Definition;
        Format = prepared.Format;
        _commands = new SceneCommandQueue(prepared.Options.CommandQueueCapacity);
        _maximumVoices = _definition.MaximumVoices;
        _voiceFadeFrames = Math.Max(1, prepared.Options.VoiceFadeFrames);
        _virtualizationEnabled = prepared.Options.EnableVirtualization;
        _virtualCatchUpBudgetFrames = Math.Max(Format.BlockFrames, prepared.Options.VirtualCatchUpBudgetFrames);
        _stopTailFrames = Format.FramesFromSeconds(Math.Max(0, prepared.Options.StopTailSeconds));
        _telemetryIntervalBlocks = Math.Max(1, prepared.Options.TelemetryIntervalBlocks);
        _telemetryCountdown = _telemetryIntervalBlocks;
        _voiceScratch = new float[Format.SamplesPerBlock];
        _catchUpScratch = new float[Format.SamplesPerBlock];
        _retirementTailValues = new float[_maximumVoices * Format.Channels];
        _retirementTailRemaining = new int[_maximumVoices];
        _retirementTailBus = new int[_maximumVoices];

        _busIndices = _definition.Buses.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _soundIndices = _definition.Sounds.Select((item, index) => (item.SoundId, index))
            .ToDictionary(item => item.SoundId, item => item.index, StringComparer.Ordinal);
        _zoneIndices = _definition.AcousticZones.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _zoneTransmission = BuildZoneTransmission(_definition, _zoneIndices);
        _zoneEnvironmentBusIndices = new int[_definition.AcousticZones.Count];
        _zoneEnvironmentGains = new float[_definition.AcousticZones.Count];
        for (int i = 0; i < _definition.AcousticZones.Count; i++)
        {
            AcousticZoneDefinition zone = _definition.AcousticZones[i];
            _zoneEnvironmentBusIndices[i] = string.IsNullOrEmpty(zone.EnvironmentBusId) ? -1 : _busIndices[zone.EnvironmentBusId];
            _zoneEnvironmentGains[i] = Math.Max(0f, zone.AmbientGain);
        }
        _parameterIndices = _definition.Parameters.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _layerIndices = _definition.Layers.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _emitterIndices = _definition.Emitters.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        _stateMachineIndices = _definition.StateMachines.Select((item, index) => (item.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);

        _triggers = _definition.StateMachines.SelectMany(machine => machine.Transitions).Select(transition => transition.Trigger)
            .Concat(_definition.EventClusters.Select(cluster => cluster.Trigger))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _triggerIndices = _triggers.Select((item, index) => (item, index))
            .ToDictionary(item => item.item, item => item.index, StringComparer.Ordinal);

        _parameters = _definition.Parameters
            .Select(item => new SceneParameterRuntime(item, Format.SampleRate, Format.BlockFrames))
            .ToArray();

        _stateMachines = _definition.StateMachines.Select(item => new StateMachineRuntime(item)).ToArray();

        _buses = new BusRuntime[_definition.Buses.Count];
        for (int i = 0; i < _buses.Length; i++)
        {
            SoundBusDefinition bus = _definition.Buses[i];
            int parent = string.IsNullOrEmpty(bus.ParentId) ? -1 : _busIndices[bus.ParentId];
            (int Target, float Gain)[] sends = bus.Sends.Select(send => (_busIndices[send.TargetBusId], send.Gain)).ToArray();
            _buses[i] = new BusRuntime(bus, Format, parent, sends);
        }
        _busProcessingOrder = prepared.BusProcessingOrder;
        _masterBusIndex = _busIndices["master"];
        _busIds = Array.AsReadOnly(_buses.Select(bus => bus.Id).ToArray());

        _layers = new LayerRuntime[_definition.Layers.Count];
        for (int i = 0; i < _layers.Length; i++)
        {
            SoundLayerDefinition layer = _definition.Layers[i];
            int machine = string.IsNullOrEmpty(layer.StateMachineId) ? -1 : _stateMachineIndices[layer.StateMachineId];
            int[] states = machine < 0 ? [] : layer.ActiveStates.Select(id => _stateMachines[machine].StateIndices[id]).ToArray();
            int zone = string.IsNullOrEmpty(layer.Spatial.ZoneId) ? -1 : _zoneIndices[layer.Spatial.ZoneId];
            _layers[i] = new LayerRuntime(layer, _soundIndices[layer.SoundId], _busIndices[layer.BusId], zone, machine, states, Format.SampleRate);
        }

        _emitters = new EventEmitterRuntime[_definition.Emitters.Count];
        for (int i = 0; i < _emitters.Length; i++)
        {
            SoundEventEmitterDefinition emitter = _definition.Emitters[i];
            int machine = string.IsNullOrEmpty(emitter.StateMachineId) ? -1 : _stateMachineIndices[emitter.StateMachineId];
            int[] states = machine < 0 ? [] : emitter.ActiveStates.Select(id => _stateMachines[machine].StateIndices[id]).ToArray();
            ulong seed = StableHash(_definition.RootSeed, emitter.Id, i);
            _emitters[i] = new EventEmitterRuntime(
                emitter,
                emitter.SoundIds.Select(id => _soundIndices[id]).ToArray(),
                _busIndices[emitter.BusId],
                string.IsNullOrEmpty(emitter.SpatialDistribution.ZoneId) ? -1 : _zoneIndices[emitter.SpatialDistribution.ZoneId],
                machine,
                states,
                new Pcg32(seed, unchecked(seed ^ 0xA5A5A5A55A5A5A5AUL)),
                Format.SampleRate);
        }

        _clusters = new EventClusterRuntime[_definition.EventClusters.Count];
        for (int i = 0; i < _clusters.Length; i++)
        {
            SoundEventClusterDefinition cluster = _definition.EventClusters[i];
            ClusterEventRuntime[] events = cluster.Events.Select(item => new ClusterEventRuntime(
                _soundIndices[item.SoundId],
                _busIndices[item.BusId],
                Format.FramesFromSeconds(item.OffsetSeconds),
                item.Probability,
                item.Gain,
                item.Pan,
                item.Priority,
                string.IsNullOrEmpty(item.Spatial.ZoneId) ? -1 : _zoneIndices[item.Spatial.ZoneId],
                new SoundEmitterState(item.Spatial.Enabled, item.Spatial.Position, item.Spatial.Velocity, item.Spatial.MinimumDistance, item.Spatial.MaximumDistance, item.Spatial.Occlusion, item.Spatial.Obstruction))).ToArray();
            ulong seed = StableHash(_definition.RootSeed, cluster.Id, i);
            _clusters[i] = new EventClusterRuntime(cluster, events, new Pcg32(seed, seed ^ 0xDB4F0B9175AE2165UL));
        }

        _bindings = CompileBindings(_definition.ParameterBindings);

        var graphCompiler = new SoundGraphCompiler(prepared.Registry);
        int totalSlots = _definition.Sounds.Sum(sound => sound.PoolSize);
        _voices = new VoiceRuntime[totalSlots];
        _soundPools = new SoundPool[_definition.Sounds.Count];
        int slot = 0;
        for (int soundIndex = 0; soundIndex < _definition.Sounds.Count; soundIndex++)
        {
            SoundReferenceDefinition reference = _definition.Sounds[soundIndex];
            int start = slot;
            SoundGraphDefinition graphDefinition = prepared.Assets[reference.SoundId];
            for (int local = 0; local < reference.PoolSize; local++)
            {
                CompiledSoundGraph graph = graphCompiler.Compile(graphDefinition, Format, new GraphCompileOptions
                {
                    VariationIndex = local,
                    EventQueueCapacity = 256,
                    EnableBufferReuse = true,
                    ApplyMacroDefaults = true
                });
                _voices[slot] = new VoiceRuntime(slot, soundIndex, reference.SoundId, graph, Format.Channels);
                slot++;
            }
            _soundPools[soundIndex] = new SoundPool(start, reference.PoolSize);
        }

        InitializeControlState();
        ResetRuntime();
    }

    public AudioFormat Format { get; }
    public SoundSceneMetadata Metadata => _prepared.Metadata;
    public long CurrentSampleFrame => Volatile.Read(ref _sampleFrame);
    public bool IsRunning => Volatile.Read(ref _isRunning);
    public bool IsComplete => false;
    public StreamingTelemetrySnapshot Telemetry => CaptureTelemetry();
    public IReadOnlyList<string> BusIds => _busIds;

    public void Prepare(AudioFormat format)
    {
        ThrowIfDisposed();
        if (format != Format) throw new InvalidOperationException("A prepared scene must be recompiled for a different audio format.");
        Reset();
    }

    public bool TryStart()
    {
        ThrowIfDisposed();
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.Start));
    }

    public void Start()
    {
        if (!TryStart()) throw new InvalidOperationException("The scene command queue is full.");
    }

    public bool TryStop()
    {
        ThrowIfDisposed();
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.Stop));
    }

    public void Stop()
    {
        if (!TryStop()) throw new InvalidOperationException("The scene command queue is full.");
    }

    public bool TrySetParameter(string parameterId, float value)
    {
        ThrowIfDisposed();
        return _parameterIndices.TryGetValue(parameterId, out int index)
            && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetParameter, index, value));
    }

    public bool TryTrigger(string trigger)
    {
        ThrowIfDisposed();
        return _triggerIndices.TryGetValue(trigger, out int index)
            && EnqueueOrCount(new SceneCommand(SceneCommandKind.Trigger, index));
    }

    public bool TrySetLayerSpatialState(string layerId, SoundEmitterState state)
    {
        ThrowIfDisposed();
        return _layerIndices.TryGetValue(layerId, out int index)
            && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetLayerSpatial, index, Emitter: state));
    }

    public bool TrySetLayerPreviewGain(string layerId, float gain)
    {
        ThrowIfDisposed();
        return float.IsFinite(gain)
            && _layerIndices.TryGetValue(layerId, out int index)
            && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetLayerPreviewGain, index, Math.Max(0f, gain)));
    }

    public bool TrySetEmitterPreviewGain(string emitterId, float gain)
    {
        ThrowIfDisposed();
        return float.IsFinite(gain)
            && _emitterIndices.TryGetValue(emitterId, out int index)
            && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetEmitterPreviewGain, index, Math.Max(0f, gain)));
    }

    public bool TrySetState(string stateMachineId, string stateId)
    {
        ThrowIfDisposed();
        if (!_stateMachineIndices.TryGetValue(stateMachineId, out int machineIndex)) return false;
        if (!_stateMachines[machineIndex].StateIndices.TryGetValue(stateId, out int stateIndex)) return false;
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.SetState, machineIndex, SecondaryIndex: stateIndex));
    }

    public bool TryPlaySound(string soundId, VoiceStartOptions options, out SoundVoiceHandle handle)
    {
        ThrowIfDisposed();
        handle = SoundVoiceHandle.Invalid;
        if (!_soundIndices.TryGetValue(soundId, out int soundIndex)) return false;
        string busId = string.IsNullOrWhiteSpace(options.BusId) ? "master" : options.BusId;
        if (!_busIndices.TryGetValue(busId, out int busIndex)) return false;
        int handleValue = Interlocked.Increment(ref _nextVoiceHandle);
        int flags = (options.Loop ? 1 : 0) | (options.Protected ? 2 : 0);
        bool queued = EnqueueOrCount(new SceneCommand(
            SceneCommandKind.PlaySound,
            soundIndex,
            options.Gain,
            Emitter: options.Spatial,
            SecondaryIndex: busIndex,
            SecondaryValue: options.Pan,
            IntegerValue: options.Priority,
            Flags: flags,
            HandleValue: handleValue));
        if (!queued) return false;
        handle = new SoundVoiceHandle(handleValue);
        return true;
    }

    public bool TryStopVoice(SoundVoiceHandle handle)
    {
        ThrowIfDisposed();
        return handle.IsValid && EnqueueOrCount(new SceneCommand(SceneCommandKind.StopVoice, handle.Value));
    }

    public bool TrySetVoiceSpatialState(SoundVoiceHandle handle, SoundEmitterState state)
    {
        ThrowIfDisposed();
        return handle.IsValid && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetVoiceSpatial, handle.Value, Emitter: state));
    }

    public bool TrySetListener(SoundListenerState listener)
    {
        ThrowIfDisposed();
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.SetListener, Listener: listener));
    }

    public bool TrySetListenerZone(string zoneId)
    {
        ThrowIfDisposed();
        int index = string.IsNullOrEmpty(zoneId) ? -1 : (_zoneIndices.TryGetValue(zoneId, out int resolvedZoneIndex) ? resolvedZoneIndex : int.MinValue);
        return index != int.MinValue && EnqueueOrCount(new SceneCommand(SceneCommandKind.SetListenerZone, index));
    }

    public bool TryRequestReset()
    {
        ThrowIfDisposed();
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.Reset));
    }

    public void Reset()
    {
        if (!TryRequestReset()) throw new InvalidOperationException("The scene command queue is full.");
    }

    private void ResetRuntime()
    {
        _isRunning = false;
        _stopping = false;
        _stopTailFramesRemaining = 0;
        _sampleFrame = 0;
        _voiceSequence = 0;
        _nextVoiceHandle = 0;
        _activeLimitVoices = 0;
        _listener = SoundListenerState.Default;
        _listenerZoneIndex = -1;
        foreach (BusRuntime bus in _buses) bus.Reset();
        foreach (VoiceRuntime voice in _voices) voice.ResetSlot();
        Array.Clear(_retirementTailValues);
        Array.Clear(_retirementTailRemaining);
        Array.Clear(_retirementTailBus);
        foreach (LayerRuntime layer in _layers)
        {
            layer.VoiceIndex = -1;
            layer.SemanticGain = 1f;
            layer.PreviewGain = 1f;
            layer.Spatial = new SoundEmitterState(
                layer.Definition.Spatial.Enabled,
                layer.Definition.Spatial.Position,
                layer.Definition.Spatial.Velocity,
                layer.Definition.Spatial.MinimumDistance,
                layer.Definition.Spatial.MaximumDistance,
                layer.Definition.Spatial.Occlusion,
                layer.Definition.Spatial.Obstruction);
        }
        for (int i = 0; i < _emitters.Length; i++)
        {
            EventEmitterRuntime emitter = _emitters[i];
            ulong seed = StableHash(_definition.RootSeed, emitter.Definition.Id, i);
            emitter.Random = new Pcg32(seed, unchecked(seed ^ 0xA5A5A5A55A5A5A5AUL));
            emitter.RateScale = 1f;
            emitter.GainScale = 1f;
            emitter.PreviewGain = 1f;
            emitter.ActiveVoices = 0;
            emitter.ScheduleNext(0);
        }
        for (int i = 0; i < _clusters.Length; i++)
        {
            ulong seed = StableHash(_definition.RootSeed, _clusters[i].Definition.Id, i);
            _clusters[i].Random = new Pcg32(seed, seed ^ 0xDB4F0B9175AE2165UL);
        }
        for (int i = 0; i < _stateMachines.Length; i++)
        {
            _stateMachines[i].CurrentStateIndex = _stateMachines[i].StateIndices[_stateMachines[i].Definition.InitialState];
        }
        for (int i = 0; i < _parameters.Length; i++)
        {
            SceneParameterDefinition definition = _definition.Parameters[i];
            _parameters[i].Restore(definition.DefaultValue, definition.DefaultValue);
        }
        for (int i = 0; i < _bindings.Length; i++) _bindings[i].LastValue = float.NaN;
        _startedVoices = _completedVoices = _stolenVoices = _rejectedVoices = _faultedVoices = _queueOverflows = 0;
        _peakRenderMilliseconds = 0;
        _lastRenderMilliseconds = 0;
        _telemetryCountdown = _telemetryIntervalBlocks;
        PublishTelemetry();
    }

    /// <summary>Attaches or removes a bounded real-time bus tap. The reference swap is atomic.</summary>
    public void SetBusTap(IStreamingBusTap? busTap) => Volatile.Write(ref _busTap, busTap);

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
            long startTicks = Stopwatch.GetTimestamp();

            DrainCommands();
            if (requestedFrames == 0)
            {
                PublishTelemetry();
                return 0;
            }
            if (!_isRunning)
            {
                UpdateRenderTiming(startTicks);
                return requestedFrames;
            }

            AdvanceParametersAndApplyBindings();
            if (!_stopping)
            {
                UpdateLayers();
                ScheduleEmitters(requestedFrames);
            }
            foreach (BusRuntime bus in _buses) bus.Buffer.AsSpan(0, sampleCount).Clear();
            RenderVoices(requestedFrames);
            RenderRetirementTails(requestedFrames);
            ProcessBuses(requestedFrames);
            _buses[_masterBusIndex].Buffer.AsSpan(0, sampleCount).CopyTo(destination);
            Sanitize(destination);
            if (_stopping && !HasActiveVoices() && !HasRetirementTails())
            {
                _stopTailFramesRemaining = Math.Max(0, _stopTailFramesRemaining - requestedFrames);
                if (_stopTailFramesRemaining == 0)
                {
                    _stopping = false;
                    _isRunning = false;
                }
            }
            Volatile.Write(ref _sampleFrame, _sampleFrame + requestedFrames);
            UpdateRenderTiming(startTicks);
            return requestedFrames;
        }
        finally
        {
            ExitRender();
        }
    }

    void IAudioNode.Process(Span<float> output, int frameCount) => Render(output, frameCount);

    /// <summary>
    /// Synchronous convenience for offline, single-threaded use. Real-time hosts should use
    /// TryRequestSnapshot and TryGetSnapshot so capture occurs at an audio block boundary.
    /// </summary>
    public SoundSceneSnapshot CaptureSnapshot() => CaptureSnapshotOffline();

    public void RestoreSnapshot(SoundSceneSnapshot snapshot)
    {
        if (!TryRestoreSnapshot(snapshot)) throw new InvalidOperationException("The scene command queue is full.");
    }

    public void Dispose()
    {
        int state = Interlocked.CompareExchange(ref _lifecycleState, 2, 0);
        if (state == 2) return;
        if (state == 1)
            throw new InvalidOperationException("Cannot dispose a scene while Render is active. Stop the audio host, then dispose it from the control thread.");
        _disposed = true;
        _isRunning = false;
        _stopping = false;
        _stopTailFramesRemaining = 0;
        _commands.Clear();
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out SceneCommand command))
        {
            switch (command.Kind)
            {
                case SceneCommandKind.Start:
                    _isRunning = true;
                    _stopping = false;
                    _stopTailFramesRemaining = 0;
                    break;
                case SceneCommandKind.Stop:
                    if (_isRunning)
                    {
                        _stopping = true;
                        _stopTailFramesRemaining = _stopTailFrames;
                        FadeAllVoices();
                    }
                    break;
                case SceneCommandKind.Reset:
                    ResetRuntime();
                    break;
                case SceneCommandKind.CaptureSnapshot:
                    CaptureRuntimeSnapshot(command.RequestId);
                    break;
                case SceneCommandKind.RestoreSnapshot:
                    ApplyPreparedSnapshot((PreparedSoundSceneSnapshot)command.Payload!);
                    break;
                case SceneCommandKind.SetParameter:
                    _parameters[command.TargetIndex].SetTarget(command.Value);
                    break;
                case SceneCommandKind.Trigger:
                    ApplyTrigger(_triggers[command.TargetIndex]);
                    break;
                case SceneCommandKind.SetListener:
                    _listener = command.Listener;
                    break;
                case SceneCommandKind.SetListenerZone:
                    _listenerZoneIndex = command.TargetIndex;
                    break;
                case SceneCommandKind.SetLayerSpatial:
                    _layers[command.TargetIndex].Spatial = command.Emitter;
                    if (_layers[command.TargetIndex].VoiceIndex >= 0)
                        _voices[_layers[command.TargetIndex].VoiceIndex].Spatial = command.Emitter;
                    break;
                case SceneCommandKind.SetLayerPreviewGain:
                    _layers[command.TargetIndex].PreviewGain = Math.Max(0f, command.Value);
                    break;
                case SceneCommandKind.SetEmitterPreviewGain:
                {
                    EventEmitterRuntime emitter = _emitters[command.TargetIndex];
                    emitter.PreviewGain = Math.Max(0f, command.Value);
                    float targetGain = emitter.Definition.Gain * emitter.GainScale * emitter.PreviewGain;
                    foreach (VoiceRuntime voice in _voices)
                    {
                        if (!voice.Active || voice.EmitterIndex != command.TargetIndex) continue;
                        voice.TargetGain = targetGain;
                        voice.FadeFramesRemaining = _voiceFadeFrames;
                    }
                    break;
                }
                case SceneCommandKind.SetState:
                    _stateMachines[command.TargetIndex].CurrentStateIndex = command.SecondaryIndex;
                    break;
                case SceneCommandKind.PlaySound:
                    StartVoice(command.TargetIndex, new PendingVoiceStart(
                        command.SecondaryIndex,
                        Math.Max(0f, command.Value),
                        Math.Clamp(command.SecondaryValue, -1f, 1f),
                        command.IntegerValue,
                        (command.Flags & 1) != 0,
                        (command.Flags & 2) != 0,
                        0,
                        -1,
                        -1,
                        -1,
                        command.HandleValue,
                        command.Emitter));
                    break;
                case SceneCommandKind.StopVoice:
                {
                    int voiceIndex = FindVoiceByHandle(command.TargetIndex);
                    if (voiceIndex >= 0)
                    {
                        VoiceRuntime voice = _voices[voiceIndex];
                        voice.TargetGain = 0f;
                        voice.FadeFramesRemaining = _voiceFadeFrames;
                    }
                    break;
                }
                case SceneCommandKind.SetVoiceSpatial:
                {
                    int voiceIndex = FindVoiceByHandle(command.TargetIndex);
                    if (voiceIndex >= 0) _voices[voiceIndex].Spatial = command.Emitter;
                    break;
                }
            }
        }
    }

    private void AdvanceParametersAndApplyBindings()
    {
        for (int i = 0; i < _parameters.Length; i++) _parameters[i].Advance();
        for (int i = 0; i < _bindings.Length; i++)
        {
            ref CompiledBinding binding = ref _bindings[i];
            SceneParameterRuntime parameter = _parameters[binding.ParameterIndex];
            float normalized = (parameter.Current - parameter.Minimum) / (parameter.Maximum - parameter.Minimum);
            normalized = Math.Clamp(normalized, 0f, 1f);
            normalized = binding.Curve switch
            {
                SceneMappingCurve.SmoothStep => normalized * normalized * (3f - (2f * normalized)),
                SceneMappingCurve.Exponential => normalized * normalized,
                SceneMappingCurve.Inverted => 1f - normalized,
                _ => normalized
            };
            float value = binding.OutputMinimum + ((binding.OutputMaximum - binding.OutputMinimum) * normalized);
            switch (binding.Kind)
            {
                case SceneParameterTargetKind.LayerGain:
                    _layers[binding.TargetIndex].SemanticGain = value;
                    break;
                case SceneParameterTargetKind.EmitterRate:
                    _emitters[binding.TargetIndex].RateScale = Math.Max(0.01f, value);
                    break;
                case SceneParameterTargetKind.EmitterGain:
                    _emitters[binding.TargetIndex].GainScale = Math.Max(0f, value);
                    break;
                case SceneParameterTargetKind.BusGain:
                    _buses[binding.TargetIndex].TargetGain = _buses[binding.TargetIndex].BaseGain * Math.Max(0f, value);
                    break;
                case SceneParameterTargetKind.LayerMacro:
                {
                    int voiceIndex = _layers[binding.TargetIndex].VoiceIndex;
                    if (voiceIndex >= 0 && (float.IsNaN(binding.LastValue) || Math.Abs(value - binding.LastValue) > 0.0001f))
                    {
                        _voices[voiceIndex].Graph.TrySetMacro(binding.TargetParameter, value);
                        binding.LastValue = value;
                    }
                    break;
                }
            }
        }
    }

    private void UpdateLayers()
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            LayerRuntime layer = _layers[i];
            bool active = layer.IsActive(_stateMachines);
            if (active && layer.VoiceIndex < 0)
            {
                int delay = Format.FramesFromSeconds(Math.Max(0, layer.Definition.StartDelaySeconds));
                int voiceIndex = StartVoice(layer.SoundIndex, new PendingVoiceStart(
                    layer.BusIndex,
                    layer.Definition.Gain * layer.SemanticGain * layer.PreviewGain,
                    layer.Definition.Pan,
                    layer.Definition.Priority,
                    true,
                    layer.Definition.Protected,
                    delay,
                    i,
                    -1,
                    layer.ZoneIndex,
                    -1,
                    layer.Spatial));
                layer.VoiceIndex = voiceIndex;
                if (voiceIndex >= 0) InvalidateLayerMacroBindings(i);
            }
            else if (active && layer.VoiceIndex >= 0)
            {
                VoiceRuntime voice = _voices[layer.VoiceIndex];
                float targetGain = layer.Definition.Gain * layer.SemanticGain * layer.PreviewGain;
                if (Math.Abs(targetGain - voice.TargetGain) > 0.0001f)
                {
                    voice.TargetGain = targetGain;
                    voice.FadeFramesRemaining = layer.FadeFrames;
                }
                voice.Spatial = layer.Spatial;
            }
            else if (!active && layer.VoiceIndex >= 0)
            {
                VoiceRuntime voice = _voices[layer.VoiceIndex];
                if (voice.TargetGain != 0f)
                {
                    voice.TargetGain = 0f;
                    voice.FadeFramesRemaining = layer.FadeFrames;
                }
            }
        }
    }

    private void ScheduleEmitters(int frameCount)
    {
        long blockEnd = _sampleFrame + frameCount;
        for (int i = 0; i < _emitters.Length; i++)
        {
            EventEmitterRuntime emitter = _emitters[i];
            if (!emitter.IsActive(_stateMachines))
            {
                // Keep dormant emitters near the current timeline without replaying every missed event
                // when their controlling state becomes active again.
                if (emitter.NextEventFrame < blockEnd) emitter.ScheduleNext(_sampleFrame);
                continue;
            }
            while (emitter.NextEventFrame < blockEnd)
            {
                long eventFrame = emitter.NextEventFrame;
                if (emitter.Random.NextFloat() <= Math.Clamp(emitter.Definition.Probability, 0f, 1f))
                {
                    int burstRange = emitter.Definition.MaximumBurstCount - emitter.Definition.MinimumBurstCount + 1;
                    int count = emitter.Definition.MinimumBurstCount + emitter.Random.NextInt(Math.Max(1, burstRange));
                    for (int burst = 0; burst < count && emitter.ActiveVoices < emitter.Definition.MaximumConcurrentVoices; burst++)
                    {
                        int soundIndex = emitter.SoundIndices[emitter.Random.NextInt(emitter.SoundIndices.Length)];
                        int spread = Format.FramesFromSeconds(Math.Max(0, emitter.Definition.BurstSpreadSeconds));
                        int burstOffset = spread <= 0 ? 0 : emitter.Random.NextInt(spread + 1);
                        int startDelay = checked((int)Math.Max(0, eventFrame - _sampleFrame)) + burstOffset;
                        SoundEmitterState spatial = CreateEmitterSpatial(emitter);
                        int voice = StartVoice(soundIndex, new PendingVoiceStart(
                            emitter.BusIndex,
                            emitter.Definition.Gain * emitter.GainScale * emitter.PreviewGain,
                            0f,
                            emitter.Definition.Priority,
                            false,
                            false,
                            startDelay,
                            -1,
                            i,
                            emitter.ZoneIndex,
                            -1,
                            spatial));
                        if (voice >= 0) emitter.ActiveVoices++;
                    }
                }
                emitter.ScheduleNext(eventFrame);
                if (emitter.NextEventFrame <= eventFrame) emitter.NextEventFrame = eventFrame + 1;
            }
        }
    }

    private SoundEmitterState CreateEmitterSpatial(EventEmitterRuntime emitter)
    {
        SpatialDistributionDefinition distribution = emitter.Definition.SpatialDistribution;
        float radius = distribution.MinimumRadius + ((distribution.MaximumRadius - distribution.MinimumRadius) * emitter.Random.NextFloat());
        float angle = emitter.Random.NextFloat() * MathF.Tau;
        Vector3 position = distribution.Centre + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
        return new SoundEmitterState(
            distribution.MaximumRadius > 0 || distribution.MinimumRadius > 0,
            position,
            Vector3.Zero,
            distribution.MinimumDistance,
            distribution.MaximumDistance,
            distribution.Occlusion,
            distribution.Obstruction);
    }

    private void RenderVoices(int frameCount)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            VoiceRuntime voice = _voices[i];
            if (!voice.Active) continue;
            try
            {
                RenderVoice(voice, frameCount);
            }
            catch
            {
                _faultedVoices++;
                DeactivateVoice(voice, VoiceStopReason.Faulted);
            }
        }
    }

    private void RenderVoice(VoiceRuntime voice, int frameCount)
    {
        int startOffset = 0;
        if (voice.StartDelayFrames > 0)
        {
            if (voice.StartDelayFrames >= frameCount)
            {
                voice.StartDelayFrames -= frameCount;
                return;
            }
            startOffset = voice.StartDelayFrames;
            voice.StartDelayFrames = 0;
        }
        int processFrames = frameCount - startOffset;
        SimpleSpatializer.Calculate(_listener, voice.Spatial, out float spatialGain, out float spatialPan, out _);
        if (_listenerZoneIndex >= 0 && voice.ZoneIndex >= 0) spatialGain *= _zoneTransmission[_listenerZoneIndex, voice.ZoneIndex];
        float expectedGain = Math.Max(voice.CurrentGain, voice.TargetGain) * spatialGain;
        bool mayVirtualize = _virtualizationEnabled && voice.Loop && !voice.Protected;
        if (mayVirtualize && expectedGain < _definition.VirtualizationThreshold)
        {
            voice.Virtual = true;
            voice.VirtualFramesOwed += processFrames;
            voice.LogicalFrame += processFrames;
            AdvanceFade(voice, processFrames);
            if (voice.TargetGain <= 0f && voice.CurrentGain <= 0.000001f) DeactivateVoice(voice, VoiceStopReason.Stopped);
            return;
        }

        if (voice.VirtualFramesOwed > 0)
        {
            long catchUpFrames = Math.Min(voice.VirtualFramesOwed, _virtualCatchUpBudgetFrames);
            FastForward(voice, catchUpFrames);
            voice.VirtualFramesOwed -= catchUpFrames;
            if (!voice.Active) return;
            if (voice.VirtualFramesOwed > 0)
            {
                // Stay virtual while catching up over bounded work units. Include the current
                // host block in the logical debt so real-time deadlines remain bounded.
                voice.Virtual = true;
                voice.VirtualFramesOwed += processFrames;
                voice.LogicalFrame += processFrames;
                AdvanceFade(voice, processFrames);
                return;
            }
            voice.Virtual = false;
        }

        int sampleCount = processFrames * Format.Channels;
        Span<float> scratch = _voiceScratch.AsSpan(0, sampleCount);
        scratch.Clear();
        voice.Graph.Process(scratch, processFrames);
        voice.LogicalFrame += processFrames;
        float pan = Math.Clamp(voice.Pan + spatialPan, -1f, 1f);
        MixVoiceToBus(voice, scratch, startOffset, processFrames, spatialGain, pan);

        if (voice.Graph.IsComplete)
        {
            if (voice.Loop)
            {
                voice.Graph.Reset();
                voice.LogicalFrame = 0;
            }
            else
            {
                DeactivateVoice(voice, VoiceStopReason.Completed);
                return;
            }
        }
        if (voice.TargetGain <= 0f && voice.CurrentGain <= 0.000001f) DeactivateVoice(voice, VoiceStopReason.Stopped);
    }

    private void MixVoiceToBus(VoiceRuntime voice, ReadOnlySpan<float> source, int startOffsetFrames, int frameCount, float spatialGain, float pan)
    {
        Span<float> destination = _buses[voice.BusIndex].Buffer;
        int environmentBusIndex = voice.ZoneIndex >= 0 ? _zoneEnvironmentBusIndices[voice.ZoneIndex] : -1;
        Span<float> environmentDestination = environmentBusIndex >= 0 && environmentBusIndex != voice.BusIndex
            ? _buses[environmentBusIndex].Buffer
            : Span<float>.Empty;
        float environmentGain = voice.ZoneIndex >= 0 ? _zoneEnvironmentGains[voice.ZoneIndex] : 0f;
        int channels = Format.Channels;
        float leftPan = channels == 2 ? MathF.Sqrt((1f - pan) * 0.5f) : 1f;
        float rightPan = channels == 2 ? MathF.Sqrt((1f + pan) * 0.5f) : 1f;
        int sourceIndex = 0;
        int destinationIndex = startOffsetFrames * channels;
        float gain = voice.CurrentGain;
        float target = voice.TargetGain;
        int fadeFrames = Math.Max(1, voice.FadeFramesRemaining);
        float step = (target - gain) / fadeFrames;
        for (int frame = 0; frame < frameCount; frame++)
        {
            if (voice.FadeFramesRemaining > 0)
            {
                gain += step;
                voice.FadeFramesRemaining--;
                if (voice.FadeFramesRemaining == 0) gain = target;
            }
            if (channels == 2)
            {
                float left = source[sourceIndex] * gain * spatialGain * leftPan;
                float right = source[sourceIndex + 1] * gain * spatialGain * rightPan;
                destination[destinationIndex] += left;
                destination[destinationIndex + 1] += right;
                if (!environmentDestination.IsEmpty)
                {
                    environmentDestination[destinationIndex] += left * environmentGain;
                    environmentDestination[destinationIndex + 1] += right * environmentGain;
                }
                voice.LastFrame[0] = left;
                voice.LastFrame[1] = right;
                sourceIndex += 2;
                destinationIndex += 2;
            }
            else
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = source[sourceIndex++] * gain * spatialGain;
                    destination[destinationIndex] += value;
                    if (!environmentDestination.IsEmpty) environmentDestination[destinationIndex] += value * environmentGain;
                    voice.LastFrame[channel] = value;
                    destinationIndex++;
                }
            }
        }
        voice.CurrentGain = gain;
    }

    private void AdvanceFade(VoiceRuntime voice, int frames)
    {
        if (voice.FadeFramesRemaining <= 0)
        {
            voice.CurrentGain = voice.TargetGain;
            return;
        }
        int used = Math.Min(frames, voice.FadeFramesRemaining);
        float amount = used / (float)voice.FadeFramesRemaining;
        voice.CurrentGain += (voice.TargetGain - voice.CurrentGain) * amount;
        voice.FadeFramesRemaining -= used;
        if (voice.FadeFramesRemaining <= 0) voice.CurrentGain = voice.TargetGain;
    }

    private void BeginRetirementTail(VoiceRuntime voice)
    {
        int slot = -1;
        int shortest = int.MaxValue;
        for (int i = 0; i < _retirementTailRemaining.Length; i++)
        {
            if (_retirementTailRemaining[i] == 0) { slot = i; break; }
            if (_retirementTailRemaining[i] < shortest) { shortest = _retirementTailRemaining[i]; slot = i; }
        }
        if (slot < 0) return;
        int offset = slot * Format.Channels;
        voice.LastFrame.AsSpan().CopyTo(_retirementTailValues.AsSpan(offset, Format.Channels));
        _retirementTailRemaining[slot] = _voiceFadeFrames;
        _retirementTailBus[slot] = voice.BusIndex;
    }

    private void RenderRetirementTails(int frameCount)
    {
        int channels = Format.Channels;
        for (int tail = 0; tail < _retirementTailRemaining.Length; tail++)
        {
            int remaining = _retirementTailRemaining[tail];
            if (remaining <= 0) continue;
            Span<float> destination = _buses[_retirementTailBus[tail]].Buffer;
            int valueOffset = tail * channels;
            int frames = Math.Min(frameCount, remaining);
            for (int frame = 0; frame < frames; frame++)
            {
                float scale = remaining / (float)_voiceFadeFrames;
                int sampleOffset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                    destination[sampleOffset + channel] += _retirementTailValues[valueOffset + channel] * scale;
                remaining--;
            }
            _retirementTailRemaining[tail] = remaining;
            if (remaining == 0) _retirementTailValues.AsSpan(valueOffset, channels).Clear();
        }
    }

    private void ProcessBuses(int frameCount)
    {
        int sampleCount = frameCount * Format.Channels;
        for (int order = 0; order < _busProcessingOrder.Length; order++)
        {
            BusRuntime bus = _buses[_busProcessingOrder[order]];
            Span<float> samples = bus.Buffer.AsSpan(0, sampleCount);
            bus.CurrentGain += (bus.TargetGain - bus.CurrentGain) * 0.25f;
            if (bus.CurrentGain != 1f)
                for (int sample = 0; sample < sampleCount; sample++) samples[sample] *= bus.CurrentGain;
            bus.Processors.Process(samples, frameCount);
            MeasureBus(bus, samples);
            Volatile.Read(ref _busTap)?.TryWriteBus(bus.Id, samples, frameCount, Format);
            foreach ((int target, float gain) in bus.Sends)
                AddBuffer(samples, _buses[target].Buffer.AsSpan(0, sampleCount), gain);
            if (bus.ParentIndex >= 0) AddBuffer(samples, _buses[bus.ParentIndex].Buffer.AsSpan(0, sampleCount), 1f);
        }
    }

    private static void AddBuffer(ReadOnlySpan<float> source, Span<float> destination, float gain)
    {
        for (int i = 0; i < source.Length; i++) destination[i] += source[i] * gain;
    }

    private static void MeasureBus(BusRuntime bus, ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float value = float.IsFinite(samples[i]) ? samples[i] : 0f;
            peak = Math.Max(peak, Math.Abs(value));
            sum += value * value;
        }
        bus.Peak = peak;
        bus.Rms = samples.Length == 0 ? 0f : (float)Math.Sqrt(sum / samples.Length);
    }

    private int StartVoice(int soundIndex, PendingVoiceStart request)
    {
        int slot = FindInactiveSlot(soundIndex);
        if (slot < 0)
        {
            slot = FindStealCandidate(soundIndex, request.Priority);
            if (slot < 0)
            {
                _rejectedVoices++;
                return -1;
            }
            VoiceRuntime stolen = _voices[slot];
            _stolenVoices++;
            BeginRetirementTail(stolen);
            DeactivateVoice(stolen, VoiceStopReason.Stolen);
        }
        if (_activeLimitVoices >= _maximumVoices)
        {
            int victimIndex = FindGlobalStealCandidate(request.Priority);
            if (victimIndex < 0)
            {
                _rejectedVoices++;
                return -1;
            }
            _stolenVoices++;
            BeginRetirementTail(_voices[victimIndex]);
            DeactivateVoice(_voices[victimIndex], VoiceStopReason.Stolen);
        }
        VoiceRuntime voice = _voices[slot];
        BeginVoice(voice, request, fadeIn: true);
        return slot;
    }

    private void BeginVoice(VoiceRuntime voice, PendingVoiceStart request, bool fadeIn)
    {
        voice.Graph.Reset();
        voice.Active = true;
        voice.CountsAgainstLimit = true;
        voice.Loop = request.Loop;
        voice.Protected = request.Protected;
        voice.Virtual = false;
        voice.BusIndex = request.BusIndex;
        voice.Priority = request.Priority;
        voice.CurrentGain = fadeIn ? 0f : request.Gain;
        voice.TargetGain = request.Gain;
        voice.Pan = request.Pan;
        voice.FadeFramesRemaining = fadeIn ? _voiceFadeFrames : 0;
        voice.StartDelayFrames = request.StartDelayFrames;
        voice.LayerIndex = request.LayerIndex;
        voice.EmitterIndex = request.EmitterIndex;
        voice.ZoneIndex = request.ZoneIndex;
        voice.HandleId = request.HandleId;
        voice.LogicalFrame = 0;
        voice.VirtualFramesOwed = 0;
        voice.StartSequence = ++_voiceSequence;
        voice.Spatial = request.Spatial.Enabled ? request.Spatial : SoundEmitterState.Disabled;
        _activeLimitVoices++;
        _startedVoices++;
    }

    private void DeactivateVoice(VoiceRuntime voice, VoiceStopReason reason)
    {
        if (!voice.Active) return;
        if (voice.CountsAgainstLimit)
        {
            _activeLimitVoices = Math.Max(0, _activeLimitVoices - 1);
            voice.CountsAgainstLimit = false;
        }
        if (voice.LayerIndex >= 0 && voice.LayerIndex < _layers.Length && _layers[voice.LayerIndex].VoiceIndex == voice.SlotIndex)
            _layers[voice.LayerIndex].VoiceIndex = -1;
        if (voice.EmitterIndex >= 0 && voice.EmitterIndex < _emitters.Length)
            _emitters[voice.EmitterIndex].ActiveVoices = Math.Max(0, _emitters[voice.EmitterIndex].ActiveVoices - 1);
        if (reason == VoiceStopReason.Completed) _completedVoices++;
        voice.ResetSlot();
    }

    private int FindVoiceByHandle(int handleId)
    {
        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i].Active && _voices[i].HandleId == handleId) return i;
        return -1;
    }

    private int FindInactiveSlot(int soundIndex)
    {
        SoundPool pool = _soundPools[soundIndex];
        for (int i = 0; i < pool.Count; i++) if (!_voices[pool.Start + i].Active) return pool.Start + i;
        return -1;
    }

    private int FindStealCandidate(int soundIndex, int newPriority)
    {
        SoundPool pool = _soundPools[soundIndex];
        int best = -1;
        double score = double.MaxValue;
        for (int i = 0; i < pool.Count; i++)
        {
            VoiceRuntime voice = _voices[pool.Start + i];
            if (!voice.Active || voice.Protected || voice.Priority > newPriority) continue;
            double candidate = (voice.Priority * 1000.0) + (voice.CurrentGain * 100.0) - (voice.StartSequence * 0.000001);
            if (candidate < score) { score = candidate; best = voice.SlotIndex; }
        }
        return best;
    }

    private int FindGlobalStealCandidate(int newPriority)
    {
        int best = -1;
        double score = double.MaxValue;
        foreach (VoiceRuntime voice in _voices)
        {
            if (!voice.Active || voice.Protected || voice.Priority > newPriority) continue;
            double candidate = (voice.Priority * 1000.0) + (voice.CurrentGain * 100.0) - (voice.StartSequence * 0.000001);
            if (candidate < score) { score = candidate; best = voice.SlotIndex; }
        }
        return best;
    }

    private void FastForward(VoiceRuntime voice, long frames)
    {
        long remaining = frames;
        while (remaining > 0 && voice.Active)
        {
            int block = (int)Math.Min(remaining, Format.BlockFrames);
            Span<float> scratch = _catchUpScratch.AsSpan(0, block * Format.Channels);
            scratch.Clear();
            voice.Graph.Process(scratch, block);
            remaining -= block;
            if (voice.Graph.IsComplete)
            {
                if (voice.Loop) voice.Graph.Reset();
                else { DeactivateVoice(voice, VoiceStopReason.Completed); break; }
            }
        }
    }

    private void ApplyTrigger(string trigger)
    {
        for (int i = 0; i < _stateMachines.Length; i++) _stateMachines[i].ApplyTrigger(trigger);
        for (int i = 0; i < _clusters.Length; i++)
        {
            EventClusterRuntime cluster = _clusters[i];
            if (cluster.Definition.Trigger != trigger) continue;
            foreach (ClusterEventRuntime item in cluster.Events)
            {
                if (cluster.Random.NextFloat() > Math.Clamp(item.Probability, 0f, 1f)) continue;
                StartVoice(item.SoundIndex, new PendingVoiceStart(
                    item.BusIndex, item.Gain, item.Pan, item.Priority, false, false, item.OffsetFrames, -1, -1, item.ZoneIndex, -1, item.Spatial));
            }
        }
    }

    private void InvalidateLayerMacroBindings(int layerIndex)
    {
        for (int i = 0; i < _bindings.Length; i++)
            if (_bindings[i].Kind == SceneParameterTargetKind.LayerMacro && _bindings[i].TargetIndex == layerIndex)
                _bindings[i].LastValue = float.NaN;
    }

    private bool HasActiveVoices()
    {
        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i].Active) return true;
        return false;
    }

    private bool HasRetirementTails()
    {
        for (int i = 0; i < _retirementTailRemaining.Length; i++)
            if (_retirementTailRemaining[i] > 0) return true;
        return false;
    }

    private void FadeAllVoices()
    {
        foreach (VoiceRuntime voice in _voices)
        {
            if (!voice.Active) continue;
            voice.TargetGain = 0f;
            voice.FadeFramesRemaining = _voiceFadeFrames;
        }
    }

    private CompiledBinding[] CompileBindings(IReadOnlyList<SceneParameterBindingDefinition> definitions)
    {
        var result = new CompiledBinding[definitions.Count];
        for (int i = 0; i < definitions.Count; i++)
        {
            SceneParameterBindingDefinition binding = definitions[i];
            int target = binding.TargetKind switch
            {
                SceneParameterTargetKind.LayerGain or SceneParameterTargetKind.LayerMacro => _layerIndices[binding.TargetId],
                SceneParameterTargetKind.EmitterRate or SceneParameterTargetKind.EmitterGain => _definition.Emitters.FindIndex(item => item.Id == binding.TargetId),
                SceneParameterTargetKind.BusGain => _busIndices[binding.TargetId],
                _ => -1
            };
            result[i] = new CompiledBinding(
                _parameterIndices[binding.ParameterId],
                binding.TargetKind,
                target,
                binding.TargetParameter,
                binding.OutputMinimum,
                binding.OutputMaximum,
                binding.Curve,
                float.NaN);
        }
        return result;
    }

    private bool EnqueueOrCount(SceneCommand command)
    {
        bool success = _commands.TryEnqueue(command);
        if (!success) Interlocked.Increment(ref _queueOverflows);
        return success;
    }

    private void UpdateRenderTiming(long startTicks)
    {
        double elapsed = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        _peakRenderMilliseconds = Math.Max(_peakRenderMilliseconds, elapsed);
        _lastRenderMilliseconds = elapsed;
        if (--_telemetryCountdown <= 0)
        {
            _telemetryCountdown = _telemetryIntervalBlocks;
            PublishTelemetry();
        }
    }

    public StreamingTelemetrySnapshot CaptureTelemetry() => CapturePublishedTelemetry();

    private static void Sanitize(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i];
            samples[i] = float.IsFinite(value) ? Math.Clamp(value, -8f, 8f) : 0f;
        }
    }

    private static ulong StableHash(ulong rootSeed, string text, int index)
    {
        ulong hash = 14695981039346656037UL ^ rootSeed ^ unchecked((ulong)index * 0x9E3779B97F4A7C15UL);
        foreach (char character in text)
        {
            hash ^= character;
            hash = unchecked(hash * 1099511628211UL);
        }
        return hash;
    }

    private static float[,] BuildZoneTransmission(SoundSceneDefinition definition, IReadOnlyDictionary<string, int> indices)
    {
        int count = definition.AcousticZones.Count;
        var result = new float[count, count];
        for (int i = 0; i < count; i++) result[i, i] = 1f;
        foreach (AcousticPortalDefinition portal in definition.AcousticPortals)
        {
            int a = indices[portal.ZoneAId];
            int b = indices[portal.ZoneBId];
            float value = Math.Clamp(portal.Transmission, 0f, 1f);
            result[a, b] = Math.Max(result[a, b], value);
            result[b, a] = Math.Max(result[b, a], value);
        }
        for (int via = 0; via < count; via++)
            for (int from = 0; from < count; from++)
                for (int to = 0; to < count; to++)
                    result[from, to] = Math.Max(result[from, to], result[from, via] * result[via, to]);
        return result;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || Volatile.Read(ref _lifecycleState) == 2, this);
    }

    private readonly record struct SoundPool(int Start, int Count);

    private record struct CompiledBinding(
        int ParameterIndex,
        SceneParameterTargetKind Kind,
        int TargetIndex,
        string TargetParameter,
        float OutputMinimum,
        float OutputMaximum,
        SceneMappingCurve Curve,
        float LastValue);
}

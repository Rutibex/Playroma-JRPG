using Playroma.Sound.Core.Random;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Snapshots;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Streaming.Telemetry;

namespace Playroma.Sound.Streaming.Runtime;

public sealed partial class StreamingSceneInstance
{
    private RuntimeSnapshotBuffer[] _snapshotBuffers = null!;
    private StreamingTelemetryBuffer[] _telemetryBuffers = null!;
    private int _publishedSnapshotIndex = -1;
    private int _publishedTelemetryIndex = -1;
    private long _nextSnapshotRequestId;
    private double _renderDeadlineMilliseconds;
    private int _lifecycleState; // 0 idle, 1 rendering/exclusive offline operation, 2 disposed

    private void InitializeControlState()
    {
        _snapshotBuffers =
        [
            new RuntimeSnapshotBuffer(_parameters.Length, _stateMachines.Length, _layers.Length, _emitters.Length, _clusters.Length, _voices.Length),
            new RuntimeSnapshotBuffer(_parameters.Length, _stateMachines.Length, _layers.Length, _emitters.Length, _clusters.Length, _voices.Length)
        ];
        _telemetryBuffers =
        [
            new StreamingTelemetryBuffer(_buses.Length, _stateMachines.Length, _soundPools.Length),
            new StreamingTelemetryBuffer(_buses.Length, _stateMachines.Length, _soundPools.Length)
        ];
        _renderDeadlineMilliseconds = Format.BlockFrames * 1000.0 / Format.SampleRate;
    }

    private void EnterRender()
    {
        ThrowIfDisposed();
        int previous = Interlocked.CompareExchange(ref _lifecycleState, 1, 0);
        if (previous == 2) throw new ObjectDisposedException(nameof(StreamingSceneInstance));
        if (previous != 0) throw new InvalidOperationException("StreamingSceneInstance supports exactly one render thread.");
    }

    private void ExitRender() => Volatile.Write(ref _lifecycleState, 0);

    /// <summary>Queues an allocation-free snapshot capture at the next render block boundary.</summary>
    public bool TryRequestSnapshot(out long requestId)
    {
        ThrowIfDisposed();
        requestId = Interlocked.Increment(ref _nextSnapshotRequestId);
        if (EnqueueOrCount(new SceneCommand(SceneCommandKind.CaptureSnapshot, RequestId: requestId))) return true;
        requestId = 0;
        return false;
    }

    /// <summary>
    /// Copies a completed audio-thread snapshot into an ordinary serializable snapshot.
    /// Allocation occurs only on the calling control thread.
    /// </summary>
    public bool TryGetSnapshot(long requestId, out SoundSceneSnapshot? snapshot)
    {
        snapshot = null;
        if (requestId <= 0) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int published = Volatile.Read(ref _publishedSnapshotIndex);
            if (published < 0) return false;
            for (int slot = 0; slot < _snapshotBuffers.Length; slot++)
            {
                int index = slot == 0 ? published : 1 - published;
                RuntimeSnapshotBuffer buffer = _snapshotBuffers[index];
                int before = Volatile.Read(ref buffer.Version);
                if ((before & 1) != 0 || buffer.RequestId != requestId) continue;
                SoundSceneSnapshot candidate = CreatePublicSnapshot(buffer);
                int after = Volatile.Read(ref buffer.Version);
                if (before == after && (after & 1) == 0 && buffer.RequestId == requestId)
                {
                    snapshot = candidate;
                    return true;
                }
            }
        }
        return false;
    }

    public bool TryGetLatestSnapshot(out long requestId, out SoundSceneSnapshot? snapshot)
    {
        int index = Volatile.Read(ref _publishedSnapshotIndex);
        requestId = index < 0 ? 0 : _snapshotBuffers[index].RequestId;
        return TryGetSnapshot(requestId, out snapshot);
    }

    /// <summary>Prepares restore data on the control thread, then applies it on the audio thread.</summary>
    public bool TryRestoreSnapshot(SoundSceneSnapshot snapshot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        PreparedSoundSceneSnapshot prepared = PrepareSnapshotForRestore(snapshot);
        return EnqueueOrCount(new SceneCommand(SceneCommandKind.RestoreSnapshot, Payload: prepared));
    }

    private SoundSceneSnapshot CaptureSnapshotOffline()
    {
        ThrowIfDisposed();
        int previous = Interlocked.CompareExchange(ref _lifecycleState, 1, 0);
        if (previous != 0)
            throw new InvalidOperationException("A synchronous snapshot cannot be captured while Render is active. Use TryRequestSnapshot instead.");
        try
        {
            // Synchronous offline capture owns the consumer boundary, so first apply
            // every command that was accepted before the capture call.
            DrainCommands();
            long requestId = Interlocked.Increment(ref _nextSnapshotRequestId);
            CaptureRuntimeSnapshot(requestId);
            if (!TryGetSnapshot(requestId, out SoundSceneSnapshot? snapshot) || snapshot is null)
                throw new InvalidOperationException("The offline snapshot could not be published.");
            return snapshot;
        }
        finally
        {
            Volatile.Write(ref _lifecycleState, 0);
        }
    }

    private void CaptureRuntimeSnapshot(long requestId)
    {
        int current = Volatile.Read(ref _publishedSnapshotIndex);
        int index = current == 0 ? 1 : 0;
        RuntimeSnapshotBuffer buffer = _snapshotBuffers[index];
        BeginWrite(ref buffer.Version);
        buffer.RequestId = requestId;
        buffer.SampleFrame = _sampleFrame;
        buffer.IsRunning = _isRunning;
        buffer.IsStopping = _stopping;
        buffer.StopTailFramesRemaining = _stopTailFramesRemaining;
        buffer.NextVoiceHandle = Volatile.Read(ref _nextVoiceHandle);
        buffer.VoiceSequence = _voiceSequence;
        buffer.Listener = _listener;
        buffer.ListenerZoneIndex = _listenerZoneIndex;

        for (int i = 0; i < _parameters.Length; i++)
        {
            buffer.ParameterCurrent[i] = _parameters[i].Current;
            buffer.ParameterTarget[i] = _parameters[i].Target;
        }
        for (int i = 0; i < _stateMachines.Length; i++) buffer.StateMachineStates[i] = _stateMachines[i].CurrentStateIndex;
        for (int i = 0; i < _layers.Length; i++)
        {
            buffer.LayerSemanticGains[i] = _layers[i].SemanticGain;
            buffer.LayerSpatialStates[i] = _layers[i].Spatial;
        }
        for (int i = 0; i < _emitters.Length; i++)
        {
            buffer.EmitterNextFrames[i] = _emitters[i].NextEventFrame;
            buffer.EmitterRandomStates[i] = _emitters[i].Random.CaptureState();
        }
        for (int i = 0; i < _clusters.Length; i++) buffer.ClusterRandomStates[i] = _clusters[i].Random.CaptureState();

        int voiceCount = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            VoiceRuntime voice = _voices[i];
            if (!voice.Active) continue;
            buffer.Voices[voiceCount++] = new RuntimeVoiceSnapshot
            {
                SlotIndex = voice.SlotIndex,
                SoundIndex = voice.SoundIndex,
                GraphFrame = voice.LogicalFrame,
                BusIndex = voice.BusIndex,
                CurrentGain = voice.CurrentGain,
                TargetGain = voice.TargetGain,
                Pan = voice.Pan,
                Priority = voice.Priority,
                Loop = voice.Loop,
                Protected = voice.Protected,
                Virtual = voice.Virtual,
                FadeFramesRemaining = voice.FadeFramesRemaining,
                StartDelayFrames = voice.StartDelayFrames,
                VirtualFramesOwed = voice.VirtualFramesOwed,
                StartSequence = voice.StartSequence,
                LayerIndex = voice.LayerIndex,
                EmitterIndex = voice.EmitterIndex,
                ZoneIndex = voice.ZoneIndex,
                HandleId = voice.HandleId,
                Spatial = voice.Spatial
            };
        }
        buffer.VoiceCount = voiceCount;
        EndWrite(ref buffer.Version);
        Volatile.Write(ref _publishedSnapshotIndex, index);
    }

    private SoundSceneSnapshot CreatePublicSnapshot(RuntimeSnapshotBuffer buffer)
    {
        var snapshot = new SoundSceneSnapshot
        {
            SceneId = _definition.Id,
            SampleFrame = buffer.SampleFrame,
            IsRunning = buffer.IsRunning,
            IsStopping = buffer.IsStopping,
            StopTailFramesRemaining = buffer.StopTailFramesRemaining,
            NextVoiceHandle = buffer.NextVoiceHandle,
            VoiceSequence = buffer.VoiceSequence,
            Listener = buffer.Listener,
            ListenerZoneId = buffer.ListenerZoneIndex >= 0 ? _definition.AcousticZones[buffer.ListenerZoneIndex].Id : ""
        };
        for (int i = 0; i < _parameters.Length; i++)
            snapshot.Parameters.Add(new SceneParameterSnapshot(_parameters[i].Id, buffer.ParameterCurrent[i], buffer.ParameterTarget[i]));
        for (int i = 0; i < _stateMachines.Length; i++)
            snapshot.StateMachines.Add(new StateMachineSnapshot(_stateMachines[i].Definition.Id, _stateMachines[i].Definition.States[buffer.StateMachineStates[i]].Id));
        for (int i = 0; i < _layers.Length; i++)
            snapshot.Layers.Add(new LayerSnapshot(_layers[i].Definition.Id, buffer.LayerSemanticGains[i], buffer.LayerSpatialStates[i]));
        for (int i = 0; i < _emitters.Length; i++)
            snapshot.Emitters.Add(new EventEmitterSnapshot(_emitters[i].Definition.Id, buffer.EmitterNextFrames[i], buffer.EmitterRandomStates[i]));
        for (int i = 0; i < _clusters.Length; i++)
            snapshot.EventClusters.Add(new EventClusterSnapshot(_clusters[i].Definition.Id, buffer.ClusterRandomStates[i]));
        for (int i = 0; i < buffer.VoiceCount; i++)
        {
            RuntimeVoiceSnapshot voice = buffer.Voices[i];
            snapshot.Voices.Add(new VoiceSnapshot
            {
                SlotIndex = voice.SlotIndex,
                SoundId = _definition.Sounds[voice.SoundIndex].SoundId,
                GraphFrame = voice.GraphFrame,
                BusId = _definition.Buses[voice.BusIndex].Id,
                Gain = voice.CurrentGain,
                TargetGain = voice.TargetGain,
                Pan = voice.Pan,
                Priority = voice.Priority,
                Loop = voice.Loop,
                Protected = voice.Protected,
                Virtual = voice.Virtual,
                FadeFramesRemaining = voice.FadeFramesRemaining,
                StartDelayFrames = voice.StartDelayFrames,
                VirtualFramesOwed = voice.VirtualFramesOwed,
                StartSequence = voice.StartSequence,
                LayerIndex = voice.LayerIndex,
                EmitterIndex = voice.EmitterIndex,
                ZoneId = voice.ZoneIndex >= 0 ? _definition.AcousticZones[voice.ZoneIndex].Id : "",
                HandleId = voice.HandleId,
                Spatial = voice.Spatial
            });
        }
        return snapshot;
    }

    private PreparedSoundSceneSnapshot PrepareSnapshotForRestore(SoundSceneSnapshot snapshot)
    {
        if (snapshot.FormatVersion < 1)
            throw new NotSupportedException($"Snapshot format {snapshot.FormatVersion} is not supported.");
        if (snapshot.FormatVersion > SoundSceneSnapshot.CurrentFormatVersion)
            throw new NotSupportedException($"Snapshot format {snapshot.FormatVersion} is newer than this runtime supports.");
        if (!StringComparer.Ordinal.Equals(snapshot.SceneId, _definition.Id))
            throw new InvalidOperationException("Snapshot belongs to another scene.");
        if (snapshot.SampleFrame < 0) throw new InvalidDataException("Snapshot sample frame cannot be negative.");
        if (snapshot.StopTailFramesRemaining < 0) throw new InvalidDataException("Snapshot stop-tail frame count cannot be negative.");
        ValidateListener(snapshot.Listener);

        var parameterCurrent = new float[_parameters.Length];
        var parameterTarget = new float[_parameters.Length];
        for (int i = 0; i < _parameters.Length; i++)
        {
            parameterCurrent[i] = _definition.Parameters[i].DefaultValue;
            parameterTarget[i] = _definition.Parameters[i].DefaultValue;
        }
        foreach (SceneParameterSnapshot item in snapshot.Parameters)
        {
            if (!_parameterIndices.TryGetValue(item.Id, out int index)) continue;
            parameterCurrent[index] = RequireFinite(item.CurrentValue, $"parameter '{item.Id}' current value");
            parameterTarget[index] = RequireFinite(item.TargetValue, $"parameter '{item.Id}' target value");
        }

        var machineStates = new int[_stateMachines.Length];
        for (int i = 0; i < _stateMachines.Length; i++)
            machineStates[i] = _stateMachines[i].StateIndices[_stateMachines[i].Definition.InitialState];
        foreach (StateMachineSnapshot item in snapshot.StateMachines)
        {
            int machineIndex = _definition.StateMachines.FindIndex(machine => machine.Id == item.Id);
            if (machineIndex < 0) continue;
            if (!_stateMachines[machineIndex].StateIndices.TryGetValue(item.CurrentState, out int stateIndex))
                throw new InvalidDataException($"Snapshot state '{item.CurrentState}' does not exist in state machine '{item.Id}'.");
            machineStates[machineIndex] = stateIndex;
        }

        var layerGains = new float[_layers.Length];
        var layerSpatial = new SoundEmitterState[_layers.Length];
        for (int i = 0; i < _layers.Length; i++)
        {
            layerGains[i] = 1f;
            SoundEmitterStateDefinition spatial = _layers[i].Definition.Spatial;
            layerSpatial[i] = new SoundEmitterState(spatial.Enabled, spatial.Position, spatial.Velocity, spatial.MinimumDistance, spatial.MaximumDistance, spatial.Occlusion, spatial.Obstruction);
        }
        foreach (LayerSnapshot item in snapshot.Layers)
        {
            if (!_layerIndices.TryGetValue(item.Id, out int index)) continue;
            layerGains[index] = RequireFinite(item.SemanticGain, $"layer '{item.Id}' semantic gain");
            ValidateEmitter(item.Spatial, $"layer '{item.Id}' spatial state");
            layerSpatial[index] = item.Spatial;
        }

        var emitterFrames = new long[_emitters.Length];
        var emitterRandom = new Pcg32Snapshot[_emitters.Length];
        for (int i = 0; i < _emitters.Length; i++)
        {
            ulong seed = StableHash(_definition.RootSeed, _emitters[i].Definition.Id, i);
            var random = new Pcg32(seed, unchecked(seed ^ 0xA5A5A5A55A5A5A5AUL));
            emitterRandom[i] = random.CaptureState();
        }
        foreach (EventEmitterSnapshot item in snapshot.Emitters)
        {
            int index = _definition.Emitters.FindIndex(emitter => emitter.Id == item.Id);
            if (index < 0) continue;
            emitterFrames[index] = item.NextEventFrame;
            emitterRandom[index] = item.RandomState;
        }

        var clusterRandom = new Pcg32Snapshot[_clusters.Length];
        for (int i = 0; i < _clusters.Length; i++)
        {
            ulong seed = StableHash(_definition.RootSeed, _clusters[i].Definition.Id, i);
            var random = new Pcg32(seed, seed ^ 0xDB4F0B9175AE2165UL);
            clusterRandom[i] = random.CaptureState();
        }
        foreach (EventClusterSnapshot item in snapshot.EventClusters)
        {
            int index = _definition.EventClusters.FindIndex(cluster => cluster.Id == item.Id);
            if (index >= 0) clusterRandom[index] = item.RandomState;
        }

        var voices = new PreparedVoiceRestore[snapshot.Voices.Count];
        var perSoundCounts = new int[_soundPools.Length];
        var usedSlots = new bool[_voices.Length];
        var graphCompiler = new SoundGraphCompiler(_prepared.Registry);
        var restoreScratch = new float[Format.SamplesPerBlock];
        for (int i = 0; i < snapshot.Voices.Count; i++)
        {
            VoiceSnapshot voice = snapshot.Voices[i];
            if (voice.LayerIndex < -1 || voice.LayerIndex >= _layers.Length)
                throw new InvalidDataException($"Snapshot voice {i} references invalid layer index {voice.LayerIndex}.");
            if (voice.EmitterIndex < -1 || voice.EmitterIndex >= _emitters.Length)
                throw new InvalidDataException($"Snapshot voice {i} references invalid emitter index {voice.EmitterIndex}.");
            if (voice.GraphFrame < 0) throw new InvalidDataException($"Snapshot voice {i} has a negative graph frame.");
            if (voice.VirtualFramesOwed < 0 || voice.VirtualFramesOwed > voice.GraphFrame)
                throw new InvalidDataException($"Snapshot voice {i} has invalid virtual catch-up debt.");
            float currentGain = RequireFinite(voice.Gain, $"snapshot voice {i} gain");
            float targetGain = RequireFinite(voice.TargetGain, $"snapshot voice {i} target gain");
            float pan = Math.Clamp(RequireFinite(voice.Pan, $"snapshot voice {i} pan"), -1f, 1f);
            ValidateEmitter(voice.Spatial, $"snapshot voice {i} spatial state");
            if (!_soundIndices.TryGetValue(voice.SoundId, out int soundIndex))
                throw new InvalidDataException($"Snapshot references unknown sound '{voice.SoundId}'.");
            if (!_busIndices.TryGetValue(voice.BusId, out int busIndex))
                throw new InvalidDataException($"Snapshot references unknown bus '{voice.BusId}'.");
            int zoneIndex = string.IsNullOrEmpty(voice.ZoneId) ? -1 : (_zoneIndices.TryGetValue(voice.ZoneId, out int resolvedVoiceZoneIndex) ? resolvedVoiceZoneIndex : throw new InvalidDataException($"Snapshot references unknown zone '{voice.ZoneId}'."));
            if (++perSoundCounts[soundIndex] > _soundPools[soundIndex].Count)
                throw new InvalidDataException($"Snapshot contains more active '{voice.SoundId}' voices than its prepared pool can restore.");

            SoundPool pool = _soundPools[soundIndex];
            int slotIndex = ResolveSnapshotSlot(voice.SlotIndex, pool, usedSlots);
            usedSlots[slotIndex] = true;
            int variationIndex = slotIndex - pool.Start;
            CompiledSoundGraph graph = graphCompiler.Compile(
                _prepared.Assets[voice.SoundId],
                Format,
                new GraphCompileOptions
                {
                    VariationIndex = variationIndex,
                    EventQueueCapacity = 256,
                    EnableBufferReuse = true,
                    ApplyMacroDefaults = true
                });

            long graphFrame = Math.Max(0, voice.GraphFrame);
            long virtualFramesOwed = Math.Clamp(voice.VirtualFramesOwed, 0, graphFrame);
            AdvancePreparedGraph(graph, graphFrame - virtualFramesOwed, voice.Loop, restoreScratch, voice.SoundId);

            voices[i] = new PreparedVoiceRestore
            {
                SlotIndex = slotIndex,
                SoundIndex = soundIndex,
                Graph = graph,
                GraphFrame = graphFrame,
                BusIndex = busIndex,
                CurrentGain = currentGain,
                TargetGain = targetGain,
                Pan = pan,
                Priority = voice.Priority,
                Loop = voice.Loop,
                Protected = voice.Protected,
                Virtual = voice.Virtual,
                FadeFramesRemaining = Math.Max(0, voice.FadeFramesRemaining),
                StartDelayFrames = Math.Max(0, voice.StartDelayFrames),
                VirtualFramesOwed = virtualFramesOwed,
                StartSequence = voice.StartSequence,
                LayerIndex = voice.LayerIndex,
                EmitterIndex = voice.EmitterIndex,
                ZoneIndex = zoneIndex,
                HandleId = voice.HandleId,
                Spatial = voice.Spatial
            };
        }

        int listenerZone = string.IsNullOrEmpty(snapshot.ListenerZoneId)
            ? -1
            : (_zoneIndices.TryGetValue(snapshot.ListenerZoneId, out int resolvedListenerZoneIndex) ? resolvedListenerZoneIndex : throw new InvalidDataException($"Snapshot references unknown listener zone '{snapshot.ListenerZoneId}'."));

        return new PreparedSoundSceneSnapshot
        {
            SampleFrame = Math.Max(0, snapshot.SampleFrame),
            IsRunning = snapshot.IsRunning,
            IsStopping = snapshot.IsStopping,
            StopTailFramesRemaining = Math.Max(0, snapshot.StopTailFramesRemaining),
            NextVoiceHandle = Math.Max(0, snapshot.NextVoiceHandle),
            VoiceSequence = Math.Max(0, snapshot.VoiceSequence),
            Listener = snapshot.Listener,
            ListenerZoneIndex = listenerZone,
            ParameterCurrent = parameterCurrent,
            ParameterTarget = parameterTarget,
            StateMachineStates = machineStates,
            LayerSemanticGains = layerGains,
            LayerSpatialStates = layerSpatial,
            EmitterNextFrames = emitterFrames,
            EmitterRandomStates = emitterRandom,
            ClusterRandomStates = clusterRandom,
            Voices = voices
        };
    }


    private static float RequireFinite(float value, string field) =>
        float.IsFinite(value) ? value : throw new InvalidDataException($"The {field} must be finite.");

    private static void ValidateListener(in SoundListenerState listener)
    {
        if (!IsFinite(listener.Position) || !IsFinite(listener.Forward) || !IsFinite(listener.Up) || !IsFinite(listener.Velocity))
            throw new InvalidDataException("Snapshot listener vectors must be finite.");
    }

    private static void ValidateEmitter(in SoundEmitterState emitter, string field)
    {
        if (!IsFinite(emitter.Position) || !IsFinite(emitter.Velocity)
            || !float.IsFinite(emitter.MinimumDistance) || !float.IsFinite(emitter.MaximumDistance)
            || !float.IsFinite(emitter.Occlusion) || !float.IsFinite(emitter.Obstruction))
            throw new InvalidDataException($"The {field} must contain only finite values.");
        if (emitter.Enabled && (emitter.MinimumDistance < 0 || emitter.MaximumDistance <= emitter.MinimumDistance))
            throw new InvalidDataException($"The {field} has an invalid distance range.");
    }

    private static bool IsFinite(System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static int ResolveSnapshotSlot(int requestedSlot, SoundPool pool, bool[] usedSlots)
    {
        int first = pool.Start;
        int end = checked(pool.Start + pool.Count);
        if (requestedSlot >= first && requestedSlot < end && !usedSlots[requestedSlot]) return requestedSlot;
        for (int slot = first; slot < end; slot++)
        {
            if (!usedSlots[slot]) return slot;
        }
        throw new InvalidDataException("Snapshot voice pool contains no available restore slot.");
    }

    private void AdvancePreparedGraph(
        CompiledSoundGraph graph,
        long frames,
        bool loop,
        float[] scratch,
        string soundId)
    {
        long remaining = frames;
        while (remaining > 0)
        {
            int blockFrames = (int)Math.Min(remaining, Format.BlockFrames);
            int samples = checked(blockFrames * Format.Channels);
            graph.Process(scratch.AsSpan(0, samples), blockFrames);
            remaining -= blockFrames;
            if (!graph.IsComplete) continue;
            if (loop)
            {
                graph.Reset();
                continue;
            }
            if (remaining > 0)
                throw new InvalidDataException($"Snapshot frame {frames} exceeds the finite duration of sound '{soundId}'.");
        }
        if (graph.IsComplete && !loop)
            throw new InvalidDataException($"Snapshot restores completed sound '{soundId}' as an active voice.");
    }

    private void ApplyPreparedSnapshot(PreparedSoundSceneSnapshot snapshot)
    {
        foreach (VoiceRuntime voice in _voices) voice.ResetSlot();
        Array.Clear(_retirementTailValues);
        Array.Clear(_retirementTailRemaining);
        Array.Clear(_retirementTailBus);
        foreach (BusRuntime bus in _buses) bus.Reset();
        foreach (LayerRuntime layer in _layers) layer.VoiceIndex = -1;
        foreach (EventEmitterRuntime emitter in _emitters) emitter.ActiveVoices = 0;
        _activeLimitVoices = 0;
        _listener = snapshot.Listener;
        _listenerZoneIndex = snapshot.ListenerZoneIndex;
        _sampleFrame = snapshot.SampleFrame;
        _isRunning = snapshot.IsRunning;
        _stopping = snapshot.IsStopping;
        _stopTailFramesRemaining = snapshot.StopTailFramesRemaining;
        _nextVoiceHandle = snapshot.NextVoiceHandle;
        _voiceSequence = snapshot.VoiceSequence;

        for (int i = 0; i < _parameters.Length; i++) _parameters[i].Restore(snapshot.ParameterCurrent[i], snapshot.ParameterTarget[i]);
        for (int i = 0; i < _stateMachines.Length; i++) _stateMachines[i].CurrentStateIndex = snapshot.StateMachineStates[i];
        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].SemanticGain = snapshot.LayerSemanticGains[i];
            _layers[i].Spatial = snapshot.LayerSpatialStates[i];
        }
        for (int i = 0; i < _emitters.Length; i++)
        {
            _emitters[i].NextEventFrame = snapshot.EmitterNextFrames[i];
            _emitters[i].Random = new Pcg32(snapshot.EmitterRandomStates[i]);
            _emitters[i].ActiveVoices = 0;
            _emitters[i].RateScale = 1f;
            _emitters[i].GainScale = 1f;
        }
        for (int i = 0; i < _clusters.Length; i++) _clusters[i].Random = new Pcg32(snapshot.ClusterRandomStates[i]);
        for (int i = 0; i < _bindings.Length; i++) _bindings[i].LastValue = float.NaN;

        long maximumSequence = _voiceSequence;
        foreach (PreparedVoiceRestore item in snapshot.Voices)
        {
            VoiceRuntime voice = _voices[item.SlotIndex];
            BeginVoice(voice, new PendingVoiceStart(
                item.BusIndex,
                item.CurrentGain,
                item.Pan,
                item.Priority,
                item.Loop,
                item.Protected,
                item.StartDelayFrames,
                item.LayerIndex,
                item.EmitterIndex,
                item.ZoneIndex,
                item.HandleId,
                item.Spatial), fadeIn: false);
            voice.Graph = item.Graph;
            voice.TargetGain = item.TargetGain;
            voice.FadeFramesRemaining = item.FadeFramesRemaining;
            voice.LogicalFrame = item.GraphFrame;
            voice.Virtual = item.Virtual;
            voice.VirtualFramesOwed = item.VirtualFramesOwed;
            voice.StartSequence = item.StartSequence;
            maximumSequence = Math.Max(maximumSequence, item.StartSequence);
            if (voice.LayerIndex >= 0 && voice.LayerIndex < _layers.Length) _layers[voice.LayerIndex].VoiceIndex = voice.SlotIndex;
            if (voice.EmitterIndex >= 0 && voice.EmitterIndex < _emitters.Length) _emitters[voice.EmitterIndex].ActiveVoices++;
        }
        _voiceSequence = maximumSequence;
        PublishTelemetry();
    }

    private void PublishTelemetry()
    {
        int current = Volatile.Read(ref _publishedTelemetryIndex);
        int index = current == 0 ? 1 : 0;
        StreamingTelemetryBuffer buffer = _telemetryBuffers[index];
        BeginWrite(ref buffer.Version);
        buffer.SampleFrame = _sampleFrame;
        buffer.ActiveVoices = 0;
        buffer.VirtualVoices = 0;
        buffer.ProtectedVoices = 0;
        Array.Clear(buffer.PoolActiveVoices);
        foreach (VoiceRuntime voice in _voices)
        {
            if (!voice.Active) continue;
            buffer.ActiveVoices++;
            if (voice.Virtual) buffer.VirtualVoices++;
            if (voice.Protected) buffer.ProtectedVoices++;
            buffer.PoolActiveVoices[voice.SoundIndex]++;
        }
        buffer.StartedVoices = _startedVoices;
        buffer.CompletedVoices = _completedVoices;
        buffer.StolenVoices = _stolenVoices;
        buffer.RejectedVoices = _rejectedVoices;
        buffer.FaultedVoices = _faultedVoices;
        buffer.CommandQueueOverflows = Volatile.Read(ref _queueOverflows);
        buffer.CommandQueueCount = _commands.Count;
        buffer.LastRenderMilliseconds = _lastRenderMilliseconds;
        buffer.PeakRenderMilliseconds = _peakRenderMilliseconds;
        for (int i = 0; i < _buses.Length; i++)
        {
            buffer.BusPeaks[i] = _buses[i].Peak;
            buffer.BusRms[i] = _buses[i].Rms;
        }
        for (int i = 0; i < _stateMachines.Length; i++) buffer.StateMachineStates[i] = _stateMachines[i].CurrentStateIndex;
        EndWrite(ref buffer.Version);
        Volatile.Write(ref _publishedTelemetryIndex, index);
    }

    private StreamingTelemetrySnapshot CapturePublishedTelemetry()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int index = Volatile.Read(ref _publishedTelemetryIndex);
            if (index < 0) return StreamingTelemetrySnapshot.Empty;
            StreamingTelemetryBuffer buffer = _telemetryBuffers[index];
            int before = Volatile.Read(ref buffer.Version);
            if ((before & 1) != 0) continue;

            var buses = new BusTelemetry[_buses.Length];
            for (int i = 0; i < buses.Length; i++) buses[i] = new BusTelemetry(_buses[i].Id, buffer.BusPeaks[i], buffer.BusRms[i]);
            var machines = new StateMachineTelemetry[_stateMachines.Length];
            for (int i = 0; i < machines.Length; i++)
                machines[i] = new StateMachineTelemetry(_stateMachines[i].Definition.Id, _stateMachines[i].Definition.States[buffer.StateMachineStates[i]].Id);
            var pools = new SoundPoolTelemetry[_soundPools.Length];
            for (int i = 0; i < pools.Length; i++)
                pools[i] = new SoundPoolTelemetry(_definition.Sounds[i].SoundId, buffer.PoolActiveVoices[i], _soundPools[i].Count);

            var result = new StreamingTelemetrySnapshot(
                buffer.SampleFrame,
                buffer.ActiveVoices,
                buffer.VirtualVoices,
                buffer.ProtectedVoices,
                buffer.StartedVoices,
                buffer.CompletedVoices,
                buffer.StolenVoices,
                buffer.RejectedVoices,
                buffer.FaultedVoices,
                buffer.CommandQueueOverflows,
                buffer.CommandQueueCount,
                _commands.Capacity,
                _maximumVoices,
                buffer.LastRenderMilliseconds,
                buffer.PeakRenderMilliseconds,
                _renderDeadlineMilliseconds,
                _renderDeadlineMilliseconds <= 0 ? 0 : buffer.LastRenderMilliseconds / _renderDeadlineMilliseconds,
                _renderDeadlineMilliseconds <= 0 ? 0 : buffer.PeakRenderMilliseconds / _renderDeadlineMilliseconds,
                buses,
                machines,
                pools);
            int after = Volatile.Read(ref buffer.Version);
            if (before == after && (after & 1) == 0) return result;
        }
        return StreamingTelemetrySnapshot.Empty;
    }

    private static void BeginWrite(ref int version)
    {
        int current = Volatile.Read(ref version);
        if ((current & 1) != 0) current++;
        Volatile.Write(ref version, current + 1);
    }

    private static void EndWrite(ref int version)
    {
        int current = Volatile.Read(ref version);
        Volatile.Write(ref version, (current & 1) == 0 ? current + 2 : current + 1);
    }
}

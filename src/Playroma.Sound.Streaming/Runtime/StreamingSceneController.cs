using Playroma.Sound.Streaming.Snapshots;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Streaming.Telemetry;

namespace Playroma.Sound.Streaming.Runtime;

/// <summary>
/// Optional multi-producer facade. It serializes UI, MIDI, game, and background-control
/// callers before they reach the scene's single-producer audio command queue.
/// The audio thread never takes this lock.
/// </summary>
public sealed class StreamingSceneController
{
    private readonly object _gate = new();
    private readonly StreamingSceneInstance _scene;

    public StreamingSceneController(StreamingSceneInstance scene) => _scene = scene ?? throw new ArgumentNullException(nameof(scene));

    public long CurrentSampleFrame => _scene.CurrentSampleFrame;
    public bool IsRunning => _scene.IsRunning;
    public StreamingTelemetrySnapshot Telemetry => _scene.Telemetry;

    public bool TryStart() { lock (_gate) return _scene.TryStart(); }
    public bool TryStop() { lock (_gate) return _scene.TryStop(); }
    public void Start() { if (!TryStart()) throw new InvalidOperationException("The scene command queue is full."); }
    public void Stop() { if (!TryStop()) throw new InvalidOperationException("The scene command queue is full."); }
    public bool TryRequestReset() { lock (_gate) return _scene.TryRequestReset(); }
    public bool TrySetParameter(string parameterId, float value) { lock (_gate) return _scene.TrySetParameter(parameterId, value); }
    public bool TryTrigger(string trigger) { lock (_gate) return _scene.TryTrigger(trigger); }
    public bool TrySetLayerSpatialState(string layerId, SoundEmitterState state) { lock (_gate) return _scene.TrySetLayerSpatialState(layerId, state); }
    public bool TrySetLayerPreviewGain(string layerId, float gain) { lock (_gate) return _scene.TrySetLayerPreviewGain(layerId, gain); }
    public bool TrySetEmitterPreviewGain(string emitterId, float gain) { lock (_gate) return _scene.TrySetEmitterPreviewGain(emitterId, gain); }
    public bool TrySetState(string stateMachineId, string stateId) { lock (_gate) return _scene.TrySetState(stateMachineId, stateId); }
    public bool TrySetListener(SoundListenerState listener) { lock (_gate) return _scene.TrySetListener(listener); }
    public bool TrySetListenerZone(string zoneId) { lock (_gate) return _scene.TrySetListenerZone(zoneId); }
    public bool TryStopVoice(SoundVoiceHandle handle) { lock (_gate) return _scene.TryStopVoice(handle); }
    public bool TrySetVoiceSpatialState(SoundVoiceHandle handle, SoundEmitterState state) { lock (_gate) return _scene.TrySetVoiceSpatialState(handle, state); }

    public bool TryPlaySound(string soundId, VoiceStartOptions options, out SoundVoiceHandle handle)
    {
        lock (_gate) return _scene.TryPlaySound(soundId, options, out handle);
    }

    public bool TryRequestSnapshot(out long requestId)
    {
        lock (_gate) return _scene.TryRequestSnapshot(out requestId);
    }

    public bool TryGetSnapshot(long requestId, out SoundSceneSnapshot? snapshot) =>
        _scene.TryGetSnapshot(requestId, out snapshot);

    public bool TryRestoreSnapshot(SoundSceneSnapshot snapshot)
    {
        lock (_gate) return _scene.TryRestoreSnapshot(snapshot);
    }
}

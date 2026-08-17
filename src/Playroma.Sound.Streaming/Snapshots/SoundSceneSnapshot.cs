using Playroma.Sound.Core.Random;
using Playroma.Sound.Streaming.Spatial;

namespace Playroma.Sound.Streaming.Snapshots;

/// <summary>
/// Reconstructive scene snapshot. It captures deterministic scheduling, graph positions,
/// parameters, voices, and spatial state. Stateful bus effect tails are intentionally not serialized.
/// </summary>
public sealed class SoundSceneSnapshot
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string SceneId { get; set; } = "";
    public long SampleFrame { get; set; }
    public bool IsRunning { get; set; }
    public bool IsStopping { get; set; }
    public int StopTailFramesRemaining { get; set; }
    public int NextVoiceHandle { get; set; }
    public long VoiceSequence { get; set; }
    public SoundListenerState Listener { get; set; } = SoundListenerState.Default;
    public string ListenerZoneId { get; set; } = "";
    public List<SceneParameterSnapshot> Parameters { get; set; } = [];
    public List<StateMachineSnapshot> StateMachines { get; set; } = [];
    public List<LayerSnapshot> Layers { get; set; } = [];
    public List<EventEmitterSnapshot> Emitters { get; set; } = [];
    public List<EventClusterSnapshot> EventClusters { get; set; } = [];
    public List<VoiceSnapshot> Voices { get; set; } = [];
}

public sealed record SceneParameterSnapshot(string Id, float CurrentValue, float TargetValue);
public sealed record StateMachineSnapshot(string Id, string CurrentState);
public sealed record LayerSnapshot(string Id, float SemanticGain, SoundEmitterState Spatial);
public sealed record EventEmitterSnapshot(string Id, long NextEventFrame, Pcg32Snapshot RandomState);
public sealed record EventClusterSnapshot(string Id, Pcg32Snapshot RandomState);

public sealed class VoiceSnapshot
{
    public int SlotIndex { get; set; } = -1;
    public string SoundId { get; set; } = "";
    public long GraphFrame { get; set; }
    public string BusId { get; set; } = "master";
    public float Gain { get; set; }
    public float TargetGain { get; set; }
    public float Pan { get; set; }
    public int Priority { get; set; }
    public bool Loop { get; set; }
    public bool Protected { get; set; }
    public bool Virtual { get; set; }
    public int FadeFramesRemaining { get; set; }
    public int StartDelayFrames { get; set; }
    public long VirtualFramesOwed { get; set; }
    public long StartSequence { get; set; }
    public int LayerIndex { get; set; } = -1;
    public int EmitterIndex { get; set; } = -1;
    public string ZoneId { get; set; } = "";
    public int HandleId { get; set; } = -1;
    public SoundEmitterState Spatial { get; set; } = SoundEmitterState.Disabled;
}

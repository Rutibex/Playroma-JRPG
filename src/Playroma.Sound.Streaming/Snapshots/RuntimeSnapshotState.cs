using Playroma.Sound.Core.Random;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Graph.Runtime;

namespace Playroma.Sound.Streaming.Snapshots;

internal sealed class RuntimeSnapshotBuffer
{
    public RuntimeSnapshotBuffer(int parameterCount, int stateMachineCount, int layerCount, int emitterCount, int clusterCount, int maximumVoiceCount)
    {
        ParameterCurrent = new float[parameterCount];
        ParameterTarget = new float[parameterCount];
        StateMachineStates = new int[stateMachineCount];
        LayerSemanticGains = new float[layerCount];
        LayerSpatialStates = new SoundEmitterState[layerCount];
        EmitterNextFrames = new long[emitterCount];
        EmitterRandomStates = new Pcg32Snapshot[emitterCount];
        ClusterRandomStates = new Pcg32Snapshot[clusterCount];
        Voices = new RuntimeVoiceSnapshot[maximumVoiceCount];
    }

    public int Version;
    public long RequestId;
    public long SampleFrame;
    public bool IsRunning;
    public bool IsStopping;
    public int StopTailFramesRemaining;
    public int NextVoiceHandle;
    public long VoiceSequence;
    public SoundListenerState Listener;
    public int ListenerZoneIndex;
    public float[] ParameterCurrent { get; }
    public float[] ParameterTarget { get; }
    public int[] StateMachineStates { get; }
    public float[] LayerSemanticGains { get; }
    public SoundEmitterState[] LayerSpatialStates { get; }
    public long[] EmitterNextFrames { get; }
    public Pcg32Snapshot[] EmitterRandomStates { get; }
    public Pcg32Snapshot[] ClusterRandomStates { get; }
    public RuntimeVoiceSnapshot[] Voices { get; }
    public int VoiceCount;
}

internal struct RuntimeVoiceSnapshot
{
    public int SlotIndex;
    public int SoundIndex;
    public long GraphFrame;
    public int BusIndex;
    public float CurrentGain;
    public float TargetGain;
    public float Pan;
    public int Priority;
    public bool Loop;
    public bool Protected;
    public bool Virtual;
    public int FadeFramesRemaining;
    public int StartDelayFrames;
    public long VirtualFramesOwed;
    public long StartSequence;
    public int LayerIndex;
    public int EmitterIndex;
    public int ZoneIndex;
    public int HandleId;
    public SoundEmitterState Spatial;
}

internal sealed class PreparedSoundSceneSnapshot
{
    public required long SampleFrame { get; init; }
    public required bool IsRunning { get; init; }
    public required bool IsStopping { get; init; }
    public required int StopTailFramesRemaining { get; init; }
    public required int NextVoiceHandle { get; init; }
    public required long VoiceSequence { get; init; }
    public required SoundListenerState Listener { get; init; }
    public required int ListenerZoneIndex { get; init; }
    public required float[] ParameterCurrent { get; init; }
    public required float[] ParameterTarget { get; init; }
    public required int[] StateMachineStates { get; init; }
    public required float[] LayerSemanticGains { get; init; }
    public required SoundEmitterState[] LayerSpatialStates { get; init; }
    public required long[] EmitterNextFrames { get; init; }
    public required Pcg32Snapshot[] EmitterRandomStates { get; init; }
    public required Pcg32Snapshot[] ClusterRandomStates { get; init; }
    public required PreparedVoiceRestore[] Voices { get; init; }
}

internal sealed class PreparedVoiceRestore
{
    public required int SlotIndex { get; init; }
    public required int SoundIndex { get; init; }
    public required CompiledSoundGraph Graph { get; init; }
    public required long GraphFrame { get; init; }
    public required int BusIndex { get; init; }
    public required float CurrentGain { get; init; }
    public required float TargetGain { get; init; }
    public required float Pan { get; init; }
    public required int Priority { get; init; }
    public required bool Loop { get; init; }
    public required bool Protected { get; init; }
    public required bool Virtual { get; init; }
    public required int FadeFramesRemaining { get; init; }
    public required int StartDelayFrames { get; init; }
    public required long VirtualFramesOwed { get; init; }
    public required long StartSequence { get; init; }
    public required int LayerIndex { get; init; }
    public required int EmitterIndex { get; init; }
    public required int ZoneIndex { get; init; }
    public required int HandleId { get; init; }
    public required SoundEmitterState Spatial { get; init; }
}

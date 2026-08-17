using System.Numerics;

namespace Playroma.Sound.Streaming.Definitions;

public sealed class SoundSceneDefinition
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Id { get; set; } = "untitled.scene";
    public string DisplayName { get; set; } = "Untitled Sound Scene";
    public ulong RootSeed { get; set; } = 1;
    public int MaximumVoices { get; set; } = 48;
    public float VirtualizationThreshold { get; set; } = 0.001f;
    public List<SoundReferenceDefinition> Sounds { get; set; } = [];
    public List<SoundBusDefinition> Buses { get; set; } = [new() { Id = "master" }];
    public List<AcousticZoneDefinition> AcousticZones { get; set; } = [];
    public List<AcousticPortalDefinition> AcousticPortals { get; set; } = [];
    public List<SceneParameterDefinition> Parameters { get; set; } = [];
    public List<SceneParameterBindingDefinition> ParameterBindings { get; set; } = [];
    public List<SoundLayerDefinition> Layers { get; set; } = [];
    public List<SoundEventEmitterDefinition> Emitters { get; set; } = [];
    public List<SoundEventClusterDefinition> EventClusters { get; set; } = [];
    public List<SceneStateMachineDefinition> StateMachines { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SoundReferenceDefinition
{
    public string SoundId { get; set; } = "";
    public int PoolSize { get; set; } = 1;
}

public enum SoundLayerKind
{
    Continuous,
    Loop
}

public sealed class SoundLayerDefinition
{
    public string Id { get; set; } = "";
    public SoundLayerKind Kind { get; set; } = SoundLayerKind.Continuous;
    public string SoundId { get; set; } = "";
    public string BusId { get; set; } = "master";
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public int Priority { get; set; } = 50;
    public bool Protected { get; set; }
    public double FadeSeconds { get; set; } = 0.1;
    public double StartDelaySeconds { get; set; }
    public string StateMachineId { get; set; } = "";
    public List<string> ActiveStates { get; set; } = [];
    public SoundEmitterStateDefinition Spatial { get; set; } = new();
}

public sealed class SoundEventEmitterDefinition
{
    public string Id { get; set; } = "";
    public List<string> SoundIds { get; set; } = [];
    public string BusId { get; set; } = "master";
    public double MinimumIntervalSeconds { get; set; } = 1;
    public double MaximumIntervalSeconds { get; set; } = 5;
    public float Probability { get; set; } = 1f;
    public int MinimumBurstCount { get; set; } = 1;
    public int MaximumBurstCount { get; set; } = 1;
    public double BurstSpreadSeconds { get; set; } = 0.1;
    public int MaximumConcurrentVoices { get; set; } = 4;
    public float Gain { get; set; } = 1f;
    public int Priority { get; set; } = 25;
    public string StateMachineId { get; set; } = "";
    public List<string> ActiveStates { get; set; } = [];
    public SpatialDistributionDefinition SpatialDistribution { get; set; } = new();
}


public sealed class SoundEventClusterDefinition
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public List<SoundClusterEventDefinition> Events { get; set; } = [];
}

public sealed class SoundClusterEventDefinition
{
    public string SoundId { get; set; } = "";
    public string BusId { get; set; } = "master";
    public double OffsetSeconds { get; set; }
    public float Probability { get; set; } = 1f;
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public int Priority { get; set; } = 50;
    public SoundEmitterStateDefinition Spatial { get; set; } = new();
}

public sealed class SpatialDistributionDefinition
{
    public Vector3 Centre { get; set; }
    public float MinimumRadius { get; set; }
    public float MaximumRadius { get; set; }
    public float MinimumDistance { get; set; } = 1f;
    public float MaximumDistance { get; set; } = 100f;
    public float Occlusion { get; set; }
    public float Obstruction { get; set; }
    public string ZoneId { get; set; } = "";
}

public sealed class SoundEmitterStateDefinition
{
    public bool Enabled { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float MinimumDistance { get; set; } = 1f;
    public float MaximumDistance { get; set; } = 100f;
    public float Occlusion { get; set; }
    public float Obstruction { get; set; }
    public string ZoneId { get; set; } = "";
}


public sealed class AcousticZoneDefinition
{
    public string Id { get; set; } = "";
    public string EnvironmentBusId { get; set; } = "";
    public float AmbientGain { get; set; } = 1f;
}

public sealed class AcousticPortalDefinition
{
    public string Id { get; set; } = "";
    public string ZoneAId { get; set; } = "";
    public string ZoneBId { get; set; } = "";
    public float Transmission { get; set; } = 1f;
    public float LowPassAmount { get; set; }
}

public sealed class SoundBusDefinition
{
    public string Id { get; set; } = "";
    public string ParentId { get; set; } = "";
    public float Gain { get; set; } = 1f;
    public List<SoundBusEffectDefinition> Effects { get; set; } = [];
    public List<SoundBusSendDefinition> Sends { get; set; } = [];
}

public enum SoundBusEffectKind
{
    LowPass,
    HighPass,
    Compressor,
    Limiter,
    Reverb,
    Saturation
}

public sealed class SoundBusEffectDefinition
{
    public SoundBusEffectKind Kind { get; set; }
    public float Amount { get; set; } = 0.5f;
    public float Secondary { get; set; } = 0.5f;
    public float Mix { get; set; } = 1f;
    public bool WetOnly { get; set; }
}

public sealed class SoundBusSendDefinition
{
    public string TargetBusId { get; set; } = "";
    public float Gain { get; set; } = 1f;
}

public sealed class SceneParameterDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float Minimum { get; set; }
    public float Maximum { get; set; } = 1f;
    public float DefaultValue { get; set; }
    public double SmoothingSeconds { get; set; } = 0.05;
}

public enum SceneParameterTargetKind
{
    LayerGain,
    EmitterRate,
    EmitterGain,
    BusGain,
    LayerMacro
}

public enum SceneMappingCurve
{
    Linear,
    SmoothStep,
    Exponential,
    Inverted
}

public sealed class SceneParameterBindingDefinition
{
    public string ParameterId { get; set; } = "";
    public SceneParameterTargetKind TargetKind { get; set; }
    public string TargetId { get; set; } = "";
    public string TargetParameter { get; set; } = "";
    public float OutputMinimum { get; set; }
    public float OutputMaximum { get; set; } = 1f;
    public SceneMappingCurve Curve { get; set; }
}

public sealed class SceneStateMachineDefinition
{
    public string Id { get; set; } = "";
    public string InitialState { get; set; } = "";
    public List<SceneStateDefinition> States { get; set; } = [];
    public List<SceneTransitionDefinition> Transitions { get; set; } = [];
}

public sealed class SceneStateDefinition
{
    public string Id { get; set; } = "";
}

public sealed class SceneTransitionDefinition
{
    public string FromState { get; set; } = "*";
    public string ToState { get; set; } = "";
    public string Trigger { get; set; } = "";
}

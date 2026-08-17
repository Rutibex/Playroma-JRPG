using System.Text.Json;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Serialization;

namespace Playroma.Sound.Authoring.Scenes;

public enum StreamingSceneObjectKind
{
    Scene,
    Sound,
    Bus,
    BusEffect,
    BusSend,
    Layer,
    Emitter,
    EventCluster,
    ClusterEvent,
    StateMachine,
    State,
    Transition,
    Parameter,
    ParameterBinding,
    AcousticZone,
    AcousticPortal
}

public readonly record struct StreamingSceneObjectKey(
    StreamingSceneObjectKind Kind,
    string Id = "",
    string ParentId = "",
    int Index = -1)
{
    public static StreamingSceneObjectKey Scene { get; } = new(StreamingSceneObjectKind.Scene);
    public bool IsScene => Kind == StreamingSceneObjectKind.Scene;
}

/// <summary>
/// Platform-neutral scene operations used by structured editors, command-line tools, and tests.
/// The service never retains authoring objects and is safe to use on detached document snapshots.
/// </summary>
public static class StreamingSceneEditing
{
    private static readonly SoundSceneSerializer Serializer = new(indented: false);

    public static object? Resolve(SoundSceneDefinition scene, StreamingSceneObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return key.Kind switch
        {
            StreamingSceneObjectKind.Scene => scene,
            StreamingSceneObjectKind.Sound => scene.Sounds.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.SoundId, key.Id)),
            StreamingSceneObjectKind.Bus => scene.Buses.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.BusEffect => At(scene.Buses.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.ParentId))?.Effects, key.Index),
            StreamingSceneObjectKind.BusSend => At(scene.Buses.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.ParentId))?.Sends, key.Index),
            StreamingSceneObjectKind.Layer => scene.Layers.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.Emitter => scene.Emitters.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.EventCluster => scene.EventClusters.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.ClusterEvent => At(scene.EventClusters.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.ParentId))?.Events, key.Index),
            StreamingSceneObjectKind.StateMachine => scene.StateMachines.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.State => scene.StateMachines.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.ParentId))?.States.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.Transition => At(scene.StateMachines.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.ParentId))?.Transitions, key.Index),
            StreamingSceneObjectKind.Parameter => scene.Parameters.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.ParameterBinding => At(scene.ParameterBindings, key.Index),
            StreamingSceneObjectKind.AcousticZone => scene.AcousticZones.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            StreamingSceneObjectKind.AcousticPortal => scene.AcousticPortals.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, key.Id)),
            _ => null
        };
    }

    public static StreamingSceneObjectKey Add(
        SoundSceneDefinition scene,
        StreamingSceneObjectKind kind,
        string parentId = "")
    {
        ArgumentNullException.ThrowIfNull(scene);
        string soundId = scene.Sounds.FirstOrDefault()?.SoundId ?? "";
        string busId = scene.Buses.FirstOrDefault(item => item.Id == "master")?.Id ?? scene.Buses.FirstOrDefault()?.Id ?? "master";
        switch (kind)
        {
            case StreamingSceneObjectKind.Sound:
            {
                string id = Unique("sound", scene.Sounds.Select(item => item.SoundId));
                scene.Sounds.Add(new SoundReferenceDefinition { SoundId = id, PoolSize = 4 });
                return new(kind, id);
            }
            case StreamingSceneObjectKind.Bus:
            {
                string id = Unique("bus", scene.Buses.Select(item => item.Id));
                scene.Buses.Add(new SoundBusDefinition { Id = id, ParentId = busId });
                return new(kind, id);
            }
            case StreamingSceneObjectKind.BusEffect:
            {
                SoundBusDefinition bus = RequiredBus(scene, parentId);
                bus.Effects.Add(new SoundBusEffectDefinition { Kind = SoundBusEffectKind.LowPass, Amount = 0.5f, Secondary = 0.25f, Mix = 1f });
                return new(kind, ParentId: bus.Id, Index: bus.Effects.Count - 1);
            }
            case StreamingSceneObjectKind.BusSend:
            {
                SoundBusDefinition bus = RequiredBus(scene, parentId);
                string target = scene.Buses.Select(item => item.Id).FirstOrDefault(id => id != bus.Id) ?? "";
                bus.Sends.Add(new SoundBusSendDefinition { TargetBusId = target, Gain = 0.5f });
                return new(kind, ParentId: bus.Id, Index: bus.Sends.Count - 1);
            }
            case StreamingSceneObjectKind.Layer:
            {
                string id = Unique("layer", scene.Layers.Select(item => item.Id));
                scene.Layers.Add(new SoundLayerDefinition { Id = id, SoundId = soundId, BusId = busId, Kind = SoundLayerKind.Loop, Gain = 1f });
                return new(kind, id);
            }
            case StreamingSceneObjectKind.Emitter:
            {
                string id = Unique("emitter", scene.Emitters.Select(item => item.Id));
                var emitter = new SoundEventEmitterDefinition { Id = id, BusId = busId, MinimumIntervalSeconds = 1, MaximumIntervalSeconds = 5 };
                if (!string.IsNullOrWhiteSpace(soundId)) emitter.SoundIds.Add(soundId);
                scene.Emitters.Add(emitter);
                return new(kind, id);
            }
            case StreamingSceneObjectKind.EventCluster:
            {
                string id = Unique("cluster", scene.EventClusters.Select(item => item.Id));
                string trigger = Unique("trigger", scene.EventClusters.Select(item => item.Trigger).Concat(scene.StateMachines.SelectMany(item => item.Transitions).Select(item => item.Trigger)));
                var cluster = new SoundEventClusterDefinition { Id = id, Trigger = trigger };
                if (!string.IsNullOrWhiteSpace(soundId)) cluster.Events.Add(new SoundClusterEventDefinition { SoundId = soundId, BusId = busId });
                scene.EventClusters.Add(cluster);
                return new(kind, id);
            }
            case StreamingSceneObjectKind.ClusterEvent:
            {
                SoundEventClusterDefinition cluster = RequiredCluster(scene, parentId);
                cluster.Events.Add(new SoundClusterEventDefinition { SoundId = soundId, BusId = busId });
                return new(kind, ParentId: cluster.Id, Index: cluster.Events.Count - 1);
            }
            case StreamingSceneObjectKind.StateMachine:
            {
                string id = Unique("state-machine", scene.StateMachines.Select(item => item.Id));
                var machine = new SceneStateMachineDefinition { Id = id, InitialState = "idle" };
                machine.States.Add(new SceneStateDefinition { Id = "idle" });
                machine.States.Add(new SceneStateDefinition { Id = "active" });
                machine.Transitions.Add(new SceneTransitionDefinition { FromState = "*", ToState = "active", Trigger = Unique("activate", AllTriggers(scene)) });
                scene.StateMachines.Add(machine);
                return new(kind, id);
            }
            case StreamingSceneObjectKind.State:
            {
                SceneStateMachineDefinition machine = RequiredMachine(scene, parentId);
                string id = Unique("state", machine.States.Select(item => item.Id));
                machine.States.Add(new SceneStateDefinition { Id = id });
                if (string.IsNullOrWhiteSpace(machine.InitialState)) machine.InitialState = id;
                return new(kind, id, machine.Id);
            }
            case StreamingSceneObjectKind.Transition:
            {
                SceneStateMachineDefinition machine = RequiredMachine(scene, parentId);
                string state = machine.States.FirstOrDefault()?.Id ?? "";
                machine.Transitions.Add(new SceneTransitionDefinition { FromState = "*", ToState = state, Trigger = Unique("trigger", AllTriggers(scene)) });
                return new(kind, ParentId: machine.Id, Index: machine.Transitions.Count - 1);
            }
            case StreamingSceneObjectKind.Parameter:
            {
                string id = Unique("parameter", scene.Parameters.Select(item => item.Id));
                scene.Parameters.Add(new SceneParameterDefinition { Id = id, DisplayName = Humanize(id), Minimum = 0f, Maximum = 1f, DefaultValue = 0.5f });
                return new(kind, id);
            }
            case StreamingSceneObjectKind.ParameterBinding:
            {
                string parameter = scene.Parameters.FirstOrDefault()?.Id ?? "";
                SceneParameterBindingDefinition binding;
                if (scene.Layers.Count > 0)
                    binding = new SceneParameterBindingDefinition { ParameterId = parameter, TargetKind = SceneParameterTargetKind.LayerGain, TargetId = scene.Layers[0].Id, OutputMinimum = 0f, OutputMaximum = 1f };
                else
                    binding = new SceneParameterBindingDefinition { ParameterId = parameter, TargetKind = SceneParameterTargetKind.BusGain, TargetId = busId, OutputMinimum = 0f, OutputMaximum = 1f };
                scene.ParameterBindings.Add(binding);
                return new(kind, Index: scene.ParameterBindings.Count - 1);
            }
            case StreamingSceneObjectKind.AcousticZone:
            {
                string id = Unique("zone", scene.AcousticZones.Select(item => item.Id));
                scene.AcousticZones.Add(new AcousticZoneDefinition { Id = id, AmbientGain = 1f });
                return new(kind, id);
            }
            case StreamingSceneObjectKind.AcousticPortal:
            {
                string id = Unique("portal", scene.AcousticPortals.Select(item => item.Id));
                scene.AcousticPortals.Add(new AcousticPortalDefinition
                {
                    Id = id,
                    ZoneAId = scene.AcousticZones.ElementAtOrDefault(0)?.Id ?? "",
                    ZoneBId = scene.AcousticZones.ElementAtOrDefault(1)?.Id ?? "",
                    Transmission = 1f
                });
                return new(kind, id);
            }
            default:
                throw new NotSupportedException($"Cannot add a {kind} object.");
        }
    }

    public static StreamingSceneObjectKey Duplicate(SoundSceneDefinition scene, StreamingSceneObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(scene);
        switch (key.Kind)
        {
            case StreamingSceneObjectKind.Sound:
            {
                SoundReferenceDefinition source = Required<SoundReferenceDefinition>(scene, key);
                SoundReferenceDefinition clone = Clone(source);
                clone.SoundId = Unique(source.SoundId + "-copy", scene.Sounds.Select(item => item.SoundId));
                scene.Sounds.Insert(scene.Sounds.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.SoundId);
            }
            case StreamingSceneObjectKind.Bus:
            {
                SoundBusDefinition source = Required<SoundBusDefinition>(scene, key);
                SoundBusDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.Buses.Select(item => item.Id));
                scene.Buses.Insert(scene.Buses.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.Layer:
            {
                SoundLayerDefinition source = Required<SoundLayerDefinition>(scene, key);
                SoundLayerDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.Layers.Select(item => item.Id));
                scene.Layers.Insert(scene.Layers.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.Emitter:
            {
                SoundEventEmitterDefinition source = Required<SoundEventEmitterDefinition>(scene, key);
                SoundEventEmitterDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.Emitters.Select(item => item.Id));
                scene.Emitters.Insert(scene.Emitters.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.EventCluster:
            {
                SoundEventClusterDefinition source = Required<SoundEventClusterDefinition>(scene, key);
                SoundEventClusterDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.EventClusters.Select(item => item.Id));
                clone.Trigger = Unique(source.Trigger + "-copy", AllTriggers(scene));
                scene.EventClusters.Insert(scene.EventClusters.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.StateMachine:
            {
                SceneStateMachineDefinition source = Required<SceneStateMachineDefinition>(scene, key);
                SceneStateMachineDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.StateMachines.Select(item => item.Id));
                scene.StateMachines.Insert(scene.StateMachines.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.Parameter:
            {
                SceneParameterDefinition source = Required<SceneParameterDefinition>(scene, key);
                SceneParameterDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.Parameters.Select(item => item.Id));
                clone.DisplayName = Humanize(clone.Id);
                scene.Parameters.Insert(scene.Parameters.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.AcousticZone:
            {
                AcousticZoneDefinition source = Required<AcousticZoneDefinition>(scene, key);
                AcousticZoneDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.AcousticZones.Select(item => item.Id));
                scene.AcousticZones.Insert(scene.AcousticZones.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.AcousticPortal:
            {
                AcousticPortalDefinition source = Required<AcousticPortalDefinition>(scene, key);
                AcousticPortalDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", scene.AcousticPortals.Select(item => item.Id));
                scene.AcousticPortals.Insert(scene.AcousticPortals.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id);
            }
            case StreamingSceneObjectKind.BusEffect:
                return DuplicateIndexed(RequiredBus(scene, key.ParentId).Effects, key, clone => clone);
            case StreamingSceneObjectKind.BusSend:
                return DuplicateIndexed(RequiredBus(scene, key.ParentId).Sends, key, clone => clone);
            case StreamingSceneObjectKind.ClusterEvent:
                return DuplicateIndexed(RequiredCluster(scene, key.ParentId).Events, key, clone => clone);
            case StreamingSceneObjectKind.Transition:
                return DuplicateIndexed(RequiredMachine(scene, key.ParentId).Transitions, key, clone => clone);
            case StreamingSceneObjectKind.ParameterBinding:
                return DuplicateIndexed(scene.ParameterBindings, key, clone => clone);
            case StreamingSceneObjectKind.State:
            {
                SceneStateMachineDefinition machine = RequiredMachine(scene, key.ParentId);
                SceneStateDefinition source = Required<SceneStateDefinition>(scene, key);
                SceneStateDefinition clone = Clone(source);
                clone.Id = Unique(source.Id + "-copy", machine.States.Select(item => item.Id));
                machine.States.Insert(machine.States.IndexOf(source) + 1, clone);
                return new(key.Kind, clone.Id, machine.Id);
            }
            default:
                throw new NotSupportedException($"Cannot duplicate a {key.Kind} object.");
        }
    }

    public static bool Move(SoundSceneDefinition scene, StreamingSceneObjectKey key, int offset)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (offset == 0) return false;
        return key.Kind switch
        {
            StreamingSceneObjectKind.Sound => MoveItem(scene.Sounds, item => item.SoundId == key.Id, offset),
            StreamingSceneObjectKind.Bus => MoveItem(scene.Buses, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.BusEffect => MoveIndex(RequiredBus(scene, key.ParentId).Effects, key.Index, offset),
            StreamingSceneObjectKind.BusSend => MoveIndex(RequiredBus(scene, key.ParentId).Sends, key.Index, offset),
            StreamingSceneObjectKind.Layer => MoveItem(scene.Layers, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.Emitter => MoveItem(scene.Emitters, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.EventCluster => MoveItem(scene.EventClusters, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.ClusterEvent => MoveIndex(RequiredCluster(scene, key.ParentId).Events, key.Index, offset),
            StreamingSceneObjectKind.StateMachine => MoveItem(scene.StateMachines, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.State => MoveItem(RequiredMachine(scene, key.ParentId).States, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.Transition => MoveIndex(RequiredMachine(scene, key.ParentId).Transitions, key.Index, offset),
            StreamingSceneObjectKind.Parameter => MoveItem(scene.Parameters, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.ParameterBinding => MoveIndex(scene.ParameterBindings, key.Index, offset),
            StreamingSceneObjectKind.AcousticZone => MoveItem(scene.AcousticZones, item => item.Id == key.Id, offset),
            StreamingSceneObjectKind.AcousticPortal => MoveItem(scene.AcousticPortals, item => item.Id == key.Id, offset),
            _ => false
        };
    }

    public static bool Remove(SoundSceneDefinition scene, StreamingSceneObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(scene);
        switch (key.Kind)
        {
            case StreamingSceneObjectKind.Sound:
                if (!RemoveItem(scene.Sounds, item => item.SoundId == key.Id)) return false;
                scene.Layers.RemoveAll(item => item.SoundId == key.Id);
                for (int i = scene.Emitters.Count - 1; i >= 0; i--)
                {
                    SoundEventEmitterDefinition emitter = scene.Emitters[i];
                    emitter.SoundIds.RemoveAll(id => id == key.Id);
                    if (emitter.SoundIds.Count == 0)
                    {
                        scene.ParameterBindings.RemoveAll(item =>
                            (item.TargetKind is SceneParameterTargetKind.EmitterGain or SceneParameterTargetKind.EmitterRate) && item.TargetId == emitter.Id);
                        scene.Emitters.RemoveAt(i);
                    }
                }
                for (int i = scene.EventClusters.Count - 1; i >= 0; i--)
                {
                    SoundEventClusterDefinition cluster = scene.EventClusters[i];
                    cluster.Events.RemoveAll(item => item.SoundId == key.Id);
                    if (cluster.Events.Count == 0) scene.EventClusters.RemoveAt(i);
                }
                return true;
            case StreamingSceneObjectKind.Bus:
                if (key.Id == "master" || !RemoveItem(scene.Buses, item => item.Id == key.Id)) return false;
                foreach (SoundBusDefinition bus in scene.Buses)
                {
                    if (bus.ParentId == key.Id) bus.ParentId = "master";
                    bus.Sends.RemoveAll(send => send.TargetBusId == key.Id);
                }
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.BusId == key.Id) layer.BusId = "master";
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.BusId == key.Id) emitter.BusId = "master";
                foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
                    foreach (SoundClusterEventDefinition item in cluster.Events) if (item.BusId == key.Id) item.BusId = "master";
                foreach (AcousticZoneDefinition zone in scene.AcousticZones) if (zone.EnvironmentBusId == key.Id) zone.EnvironmentBusId = "";
                scene.ParameterBindings.RemoveAll(item => item.TargetKind == SceneParameterTargetKind.BusGain && item.TargetId == key.Id);
                return true;
            case StreamingSceneObjectKind.BusEffect:
                return RemoveIndex(RequiredBus(scene, key.ParentId).Effects, key.Index);
            case StreamingSceneObjectKind.BusSend:
                return RemoveIndex(RequiredBus(scene, key.ParentId).Sends, key.Index);
            case StreamingSceneObjectKind.Layer:
                if (!RemoveItem(scene.Layers, item => item.Id == key.Id)) return false;
                scene.ParameterBindings.RemoveAll(item => (item.TargetKind is SceneParameterTargetKind.LayerGain or SceneParameterTargetKind.LayerMacro) && item.TargetId == key.Id);
                return true;
            case StreamingSceneObjectKind.Emitter:
                if (!RemoveItem(scene.Emitters, item => item.Id == key.Id)) return false;
                scene.ParameterBindings.RemoveAll(item => (item.TargetKind is SceneParameterTargetKind.EmitterGain or SceneParameterTargetKind.EmitterRate) && item.TargetId == key.Id);
                return true;
            case StreamingSceneObjectKind.EventCluster:
                return RemoveItem(scene.EventClusters, item => item.Id == key.Id);
            case StreamingSceneObjectKind.ClusterEvent:
                return RemoveIndex(RequiredCluster(scene, key.ParentId).Events, key.Index);
            case StreamingSceneObjectKind.StateMachine:
                if (!RemoveItem(scene.StateMachines, item => item.Id == key.Id)) return false;
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.StateMachineId == key.Id) { layer.StateMachineId = ""; layer.ActiveStates.Clear(); }
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.StateMachineId == key.Id) { emitter.StateMachineId = ""; emitter.ActiveStates.Clear(); }
                return true;
            case StreamingSceneObjectKind.State:
            {
                SceneStateMachineDefinition machine = RequiredMachine(scene, key.ParentId);
                if (!RemoveItem(machine.States, item => item.Id == key.Id)) return false;
                machine.Transitions.RemoveAll(item => item.FromState == key.Id || item.ToState == key.Id);
                if (machine.InitialState == key.Id) machine.InitialState = machine.States.FirstOrDefault()?.Id ?? "";
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.StateMachineId == machine.Id) layer.ActiveStates.RemoveAll(id => id == key.Id);
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.StateMachineId == machine.Id) emitter.ActiveStates.RemoveAll(id => id == key.Id);
                return true;
            }
            case StreamingSceneObjectKind.Transition:
                return RemoveIndex(RequiredMachine(scene, key.ParentId).Transitions, key.Index);
            case StreamingSceneObjectKind.Parameter:
                if (!RemoveItem(scene.Parameters, item => item.Id == key.Id)) return false;
                scene.ParameterBindings.RemoveAll(item => item.ParameterId == key.Id);
                return true;
            case StreamingSceneObjectKind.ParameterBinding:
                return RemoveIndex(scene.ParameterBindings, key.Index);
            case StreamingSceneObjectKind.AcousticZone:
                if (!RemoveItem(scene.AcousticZones, item => item.Id == key.Id)) return false;
                scene.AcousticPortals.RemoveAll(item => item.ZoneAId == key.Id || item.ZoneBId == key.Id);
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.Spatial.ZoneId == key.Id) layer.Spatial.ZoneId = "";
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.SpatialDistribution.ZoneId == key.Id) emitter.SpatialDistribution.ZoneId = "";
                foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
                    foreach (SoundClusterEventDefinition item in cluster.Events) if (item.Spatial.ZoneId == key.Id) item.Spatial.ZoneId = "";
                return true;
            case StreamingSceneObjectKind.AcousticPortal:
                return RemoveItem(scene.AcousticPortals, item => item.Id == key.Id);
            default:
                return false;
        }
    }

    /// <summary>
    /// Updates references after the PropertyGrid has already assigned a new identifier to the selected object.
    /// </summary>
    public static StreamingSceneObjectKey PropagateRename(
        SoundSceneDefinition scene,
        StreamingSceneObjectKey oldKey,
        string newId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        newId = newId?.Trim() ?? "";
        string oldId = oldKey.Id;
        if (StringComparer.Ordinal.Equals(oldId, newId)) return oldKey;
        switch (oldKey.Kind)
        {
            case StreamingSceneObjectKind.Sound:
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.SoundId == oldId) layer.SoundId = newId;
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters)
                    for (int i = 0; i < emitter.SoundIds.Count; i++) if (emitter.SoundIds[i] == oldId) emitter.SoundIds[i] = newId;
                foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
                    foreach (SoundClusterEventDefinition item in cluster.Events) if (item.SoundId == oldId) item.SoundId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.Bus:
                foreach (SoundBusDefinition bus in scene.Buses)
                {
                    if (bus.ParentId == oldId) bus.ParentId = newId;
                    foreach (SoundBusSendDefinition send in bus.Sends) if (send.TargetBusId == oldId) send.TargetBusId = newId;
                }
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.BusId == oldId) layer.BusId = newId;
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.BusId == oldId) emitter.BusId = newId;
                foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
                    foreach (SoundClusterEventDefinition item in cluster.Events) if (item.BusId == oldId) item.BusId = newId;
                foreach (AcousticZoneDefinition zone in scene.AcousticZones) if (zone.EnvironmentBusId == oldId) zone.EnvironmentBusId = newId;
                foreach (SceneParameterBindingDefinition binding in scene.ParameterBindings)
                    if (binding.TargetKind == SceneParameterTargetKind.BusGain && binding.TargetId == oldId) binding.TargetId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.Layer:
                foreach (SceneParameterBindingDefinition binding in scene.ParameterBindings)
                    if ((binding.TargetKind is SceneParameterTargetKind.LayerGain or SceneParameterTargetKind.LayerMacro) && binding.TargetId == oldId) binding.TargetId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.Emitter:
                foreach (SceneParameterBindingDefinition binding in scene.ParameterBindings)
                    if ((binding.TargetKind is SceneParameterTargetKind.EmitterGain or SceneParameterTargetKind.EmitterRate) && binding.TargetId == oldId) binding.TargetId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.StateMachine:
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.StateMachineId == oldId) layer.StateMachineId = newId;
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.StateMachineId == oldId) emitter.StateMachineId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.State:
            {
                SceneStateMachineDefinition machine = RequiredMachine(scene, oldKey.ParentId);
                if (machine.InitialState == oldId) machine.InitialState = newId;
                foreach (SceneTransitionDefinition transition in machine.Transitions)
                {
                    if (transition.FromState == oldId) transition.FromState = newId;
                    if (transition.ToState == oldId) transition.ToState = newId;
                }
                foreach (SoundLayerDefinition layer in scene.Layers)
                    if (layer.StateMachineId == machine.Id)
                        for (int i = 0; i < layer.ActiveStates.Count; i++) if (layer.ActiveStates[i] == oldId) layer.ActiveStates[i] = newId;
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters)
                    if (emitter.StateMachineId == machine.Id)
                        for (int i = 0; i < emitter.ActiveStates.Count; i++) if (emitter.ActiveStates[i] == oldId) emitter.ActiveStates[i] = newId;
                return oldKey with { Id = newId };
            }
            case StreamingSceneObjectKind.Parameter:
                foreach (SceneParameterBindingDefinition binding in scene.ParameterBindings) if (binding.ParameterId == oldId) binding.ParameterId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.AcousticZone:
                foreach (AcousticPortalDefinition portal in scene.AcousticPortals)
                {
                    if (portal.ZoneAId == oldId) portal.ZoneAId = newId;
                    if (portal.ZoneBId == oldId) portal.ZoneBId = newId;
                }
                foreach (SoundLayerDefinition layer in scene.Layers) if (layer.Spatial.ZoneId == oldId) layer.Spatial.ZoneId = newId;
                foreach (SoundEventEmitterDefinition emitter in scene.Emitters) if (emitter.SpatialDistribution.ZoneId == oldId) emitter.SpatialDistribution.ZoneId = newId;
                foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
                    foreach (SoundClusterEventDefinition item in cluster.Events) if (item.Spatial.ZoneId == oldId) item.Spatial.ZoneId = newId;
                return oldKey with { Id = newId };
            case StreamingSceneObjectKind.EventCluster:
            case StreamingSceneObjectKind.AcousticPortal:
                return oldKey with { Id = newId };
            default:
                return oldKey;
        }
    }

    public static string DisplayName(SoundSceneDefinition scene, StreamingSceneObjectKey key)
    {
        object? value = Resolve(scene, key);
        return key.Kind switch
        {
            StreamingSceneObjectKind.Scene => scene.DisplayName,
            StreamingSceneObjectKind.Sound => key.Id,
            StreamingSceneObjectKind.Bus => key.Id,
            StreamingSceneObjectKind.BusEffect => value is SoundBusEffectDefinition effect ? $"{effect.Kind} effect" : "Bus effect",
            StreamingSceneObjectKind.BusSend => value is SoundBusSendDefinition send ? $"Send to {send.TargetBusId}" : "Bus send",
            StreamingSceneObjectKind.Layer => key.Id,
            StreamingSceneObjectKind.Emitter => key.Id,
            StreamingSceneObjectKind.EventCluster => key.Id,
            StreamingSceneObjectKind.ClusterEvent => value is SoundClusterEventDefinition item ? $"{item.OffsetSeconds:0.###}s — {item.SoundId}" : "Cluster event",
            StreamingSceneObjectKind.StateMachine => key.Id,
            StreamingSceneObjectKind.State => key.Id,
            StreamingSceneObjectKind.Transition => value is SceneTransitionDefinition transition ? $"{transition.FromState} → {transition.ToState} [{transition.Trigger}]" : "Transition",
            StreamingSceneObjectKind.Parameter => value is SceneParameterDefinition parameter ? string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName : key.Id,
            StreamingSceneObjectKind.ParameterBinding => value is SceneParameterBindingDefinition binding ? $"{binding.ParameterId} → {binding.TargetId}" : "Parameter binding",
            StreamingSceneObjectKind.AcousticZone => key.Id,
            StreamingSceneObjectKind.AcousticPortal => key.Id,
            _ => key.Id
        };
    }

    private static IEnumerable<string> AllTriggers(SoundSceneDefinition scene) =>
        scene.EventClusters.Select(item => item.Trigger).Concat(scene.StateMachines.SelectMany(item => item.Transitions).Select(item => item.Trigger));

    private static SoundBusDefinition RequiredBus(SoundSceneDefinition scene, string id) =>
        scene.Buses.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException($"Bus '{id}' was not found.");

    private static SoundEventClusterDefinition RequiredCluster(SoundSceneDefinition scene, string id) =>
        scene.EventClusters.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException($"Event cluster '{id}' was not found.");

    private static SceneStateMachineDefinition RequiredMachine(SoundSceneDefinition scene, string id) =>
        scene.StateMachines.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException($"State machine '{id}' was not found.");

    private static T Required<T>(SoundSceneDefinition scene, StreamingSceneObjectKey key) where T : class =>
        Resolve(scene, key) as T ?? throw new KeyNotFoundException($"{key.Kind} '{key.Id}' was not found.");

    private static T? At<T>(IReadOnlyList<T>? values, int index) where T : class =>
        values is not null && index >= 0 && index < values.Count ? values[index] : null;

    private static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Serializer.Options), Serializer.Options)
        ?? throw new InvalidOperationException($"Could not clone {typeof(T).Name}.");

    private static StreamingSceneObjectKey DuplicateIndexed<T>(List<T> values, StreamingSceneObjectKey key, Func<T, T> transform) where T : class
    {
        if (key.Index < 0 || key.Index >= values.Count) throw new KeyNotFoundException($"{key.Kind} index {key.Index} was not found.");
        T clone = transform(Clone(values[key.Index]));
        int index = key.Index + 1;
        values.Insert(index, clone);
        return key with { Index = index };
    }

    private static bool MoveItem<T>(List<T> values, Predicate<T> predicate, int offset)
    {
        int index = values.FindIndex(predicate);
        return MoveIndex(values, index, offset);
    }

    private static bool MoveIndex<T>(List<T> values, int index, int offset)
    {
        if (index < 0 || index >= values.Count) return false;
        int target = Math.Clamp(index + offset, 0, values.Count - 1);
        if (target == index) return false;
        T value = values[index];
        values.RemoveAt(index);
        values.Insert(target, value);
        return true;
    }

    private static bool RemoveItem<T>(List<T> values, Predicate<T> predicate)
    {
        int index = values.FindIndex(predicate);
        if (index < 0) return false;
        values.RemoveAt(index);
        return true;
    }

    private static bool RemoveIndex<T>(List<T> values, int index)
    {
        if (index < 0 || index >= values.Count) return false;
        values.RemoveAt(index);
        return true;
    }

    private static string Unique(string basis, IEnumerable<string> existing)
    {
        string normalized = Slug(string.IsNullOrWhiteSpace(basis) ? "item" : basis);
        var used = existing.Where(item => !string.IsNullOrWhiteSpace(item)).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(normalized)) return normalized;
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{normalized}-{suffix}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static string Slug(string value)
    {
        var characters = value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-').ToArray();
        string result = new(characters);
        while (result.Contains("--", StringComparison.Ordinal)) result = result.Replace("--", "-", StringComparison.Ordinal);
        return result.Trim('-');
    }

    private static string Humanize(string value) =>
        string.Join(' ', value.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}

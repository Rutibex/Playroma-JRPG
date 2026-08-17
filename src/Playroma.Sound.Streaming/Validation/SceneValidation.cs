using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.Streaming.Validation;

public enum SceneDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record SceneDiagnostic(string Code, SceneDiagnosticSeverity Severity, string Message, string ObjectId = "");

public sealed class SceneValidationReport
{
    public SceneValidationReport(IReadOnlyList<SceneDiagnostic> diagnostics) => Diagnostics = diagnostics;
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(item => item.Severity == SceneDiagnosticSeverity.Error);
}

public sealed class SoundSceneValidator
{
    public SceneValidationReport Validate(SoundSceneDefinition scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var diagnostics = new List<SceneDiagnostic>();
        if (string.IsNullOrWhiteSpace(scene.Id)) Error("SCENE001", "Scene ID is required.");
        if (scene.MaximumVoices is < 1 or > 4096) Error("SCENE002", "MaximumVoices must be between 1 and 4096.");
        if (!float.IsFinite(scene.VirtualizationThreshold) || scene.VirtualizationThreshold < 0) Error("SCENE003", "VirtualizationThreshold must be finite and non-negative.");

        ValidateUnique(scene.Sounds.Select(x => x.SoundId), "sound", "SCENE010");
        ValidateUnique(scene.Buses.Select(x => x.Id), "bus", "SCENE011");
        ValidateUnique(scene.AcousticZones.Select(x => x.Id), "acoustic zone", "SCENE017");
        ValidateUnique(scene.AcousticPortals.Select(x => x.Id), "acoustic portal", "SCENE018");
        ValidateUnique(scene.Parameters.Select(x => x.Id), "parameter", "SCENE012");
        ValidateUnique(scene.Layers.Select(x => x.Id), "layer", "SCENE013");
        ValidateUnique(scene.Emitters.Select(x => x.Id), "emitter", "SCENE014");
        ValidateUnique(scene.EventClusters.Select(x => x.Id), "event cluster", "SCENE016");
        ValidateUnique(scene.StateMachines.Select(x => x.Id), "state machine", "SCENE015");

        var sounds = scene.Sounds.Select(x => x.SoundId).ToHashSet(StringComparer.Ordinal);
        var buses = scene.Buses.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var zones = scene.AcousticZones.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var parameters = scene.Parameters.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var layers = scene.Layers.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var emitters = scene.Emitters.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var machines = new Dictionary<string, SceneStateMachineDefinition>(StringComparer.Ordinal);
        foreach (SceneStateMachineDefinition machine in scene.StateMachines)
            if (!string.IsNullOrWhiteSpace(machine.Id)) machines.TryAdd(machine.Id, machine);

        if (!buses.Contains("master")) Error("SCENE020", "A bus named 'master' is required.");
        foreach (SoundReferenceDefinition sound in scene.Sounds)
        {
            if (string.IsNullOrWhiteSpace(sound.SoundId)) Error("SCENE021", "Sound reference ID is required.");
            if (sound.PoolSize is < 1 or > 1024) Error("SCENE022", $"Sound '{sound.SoundId}' has an invalid pool size.", sound.SoundId);
        }

        foreach (SoundBusDefinition bus in scene.Buses)
        {
            if (bus.Id == "master" && !string.IsNullOrEmpty(bus.ParentId)) Error("SCENE030", "The master bus cannot have a parent.", bus.Id);
            if (!string.IsNullOrEmpty(bus.ParentId) && !buses.Contains(bus.ParentId)) Error("SCENE031", $"Bus parent '{bus.ParentId}' does not exist.", bus.Id);
            foreach (SoundBusSendDefinition send in bus.Sends)
            {
                if (!buses.Contains(send.TargetBusId)) Error("SCENE032", $"Send target '{send.TargetBusId}' does not exist.", bus.Id);
                if (send.TargetBusId == bus.Id) Error("SCENE033", "A bus cannot send to itself.", bus.Id);
            }
        }
        ValidateBusCycles(scene, diagnostics);

        foreach (AcousticZoneDefinition zone in scene.AcousticZones)
        {
            if (!string.IsNullOrEmpty(zone.EnvironmentBusId) && !buses.Contains(zone.EnvironmentBusId)) Error("SCENE035", $"Zone environment bus '{zone.EnvironmentBusId}' does not exist.", zone.Id);
        }
        foreach (AcousticPortalDefinition portal in scene.AcousticPortals)
        {
            if (!zones.Contains(portal.ZoneAId) || !zones.Contains(portal.ZoneBId)) Error("SCENE036", "Portal references an unknown acoustic zone.", portal.Id);
            if (portal.ZoneAId == portal.ZoneBId) Error("SCENE037", "Portal zones must be different.", portal.Id);
            if (!float.IsFinite(portal.Transmission) || portal.Transmission < 0 || portal.Transmission > 1) Error("SCENE038", "Portal transmission must be between zero and one.", portal.Id);
        }

        foreach (SceneParameterDefinition parameter in scene.Parameters)
        {
            if (!float.IsFinite(parameter.Minimum) || !float.IsFinite(parameter.Maximum) || parameter.Maximum <= parameter.Minimum)
                Error("SCENE040", $"Parameter '{parameter.Id}' has invalid limits.", parameter.Id);
            if (parameter.DefaultValue < parameter.Minimum || parameter.DefaultValue > parameter.Maximum)
                Error("SCENE041", $"Parameter '{parameter.Id}' default is outside its limits.", parameter.Id);
        }

        foreach (SoundLayerDefinition layer in scene.Layers)
        {
            if (!sounds.Contains(layer.SoundId)) Error("SCENE050", $"Layer sound '{layer.SoundId}' is not referenced.", layer.Id);
            if (!buses.Contains(layer.BusId)) Error("SCENE051", $"Layer bus '{layer.BusId}' does not exist.", layer.Id);
            ValidateStateSelector(layer.StateMachineId, layer.ActiveStates, layer.Id);
            if (!string.IsNullOrEmpty(layer.Spatial.ZoneId) && !zones.Contains(layer.Spatial.ZoneId)) Error("SCENE052", $"Layer zone '{layer.Spatial.ZoneId}' does not exist.", layer.Id);
        }

        foreach (SoundEventEmitterDefinition emitter in scene.Emitters)
        {
            if (emitter.SoundIds.Count == 0) Error("SCENE060", "Event emitter must reference at least one sound.", emitter.Id);
            foreach (string soundId in emitter.SoundIds) if (!sounds.Contains(soundId)) Error("SCENE061", $"Emitter sound '{soundId}' is not referenced.", emitter.Id);
            if (!buses.Contains(emitter.BusId)) Error("SCENE062", $"Emitter bus '{emitter.BusId}' does not exist.", emitter.Id);
            if (emitter.MinimumIntervalSeconds < 0 || emitter.MaximumIntervalSeconds < emitter.MinimumIntervalSeconds) Error("SCENE063", "Emitter interval range is invalid.", emitter.Id);
            if (emitter.MinimumBurstCount < 1 || emitter.MaximumBurstCount < emitter.MinimumBurstCount) Error("SCENE064", "Emitter burst range is invalid.", emitter.Id);
            ValidateStateSelector(emitter.StateMachineId, emitter.ActiveStates, emitter.Id);
            if (!string.IsNullOrEmpty(emitter.SpatialDistribution.ZoneId) && !zones.Contains(emitter.SpatialDistribution.ZoneId)) Error("SCENE064A", $"Emitter zone '{emitter.SpatialDistribution.ZoneId}' does not exist.", emitter.Id);
        }

        foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
        {
            if (string.IsNullOrWhiteSpace(cluster.Trigger)) Error("SCENE065", "Event cluster trigger is required.", cluster.Id);
            if (cluster.Events.Count == 0) Error("SCENE066", "Event cluster must contain at least one event.", cluster.Id);
            foreach (SoundClusterEventDefinition item in cluster.Events)
            {
                if (!sounds.Contains(item.SoundId)) Error("SCENE067", $"Cluster sound '{item.SoundId}' is not referenced.", cluster.Id);
                if (!buses.Contains(item.BusId)) Error("SCENE068", $"Cluster bus '{item.BusId}' does not exist.", cluster.Id);
                if (item.OffsetSeconds < 0) Error("SCENE069", "Cluster offsets cannot be negative.", cluster.Id);
                if (!string.IsNullOrEmpty(item.Spatial.ZoneId) && !zones.Contains(item.Spatial.ZoneId)) Error("SCENE069A", $"Cluster event zone '{item.Spatial.ZoneId}' does not exist.", cluster.Id);
            }
        }

        foreach (SceneStateMachineDefinition machine in scene.StateMachines)
        {
            var states = machine.States.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            if (!states.Contains(machine.InitialState)) Error("SCENE070", $"Initial state '{machine.InitialState}' does not exist.", machine.Id);
            if (states.Count != machine.States.Count) Error("SCENE071", "State IDs must be unique.", machine.Id);
            foreach (SceneTransitionDefinition transition in machine.Transitions)
            {
                if (transition.FromState != "*" && !states.Contains(transition.FromState)) Error("SCENE072", $"Transition source '{transition.FromState}' does not exist.", machine.Id);
                if (!states.Contains(transition.ToState)) Error("SCENE073", $"Transition target '{transition.ToState}' does not exist.", machine.Id);
                if (string.IsNullOrWhiteSpace(transition.Trigger)) Error("SCENE074", "Transition trigger is required.", machine.Id);
            }
        }

        foreach (SceneParameterBindingDefinition binding in scene.ParameterBindings)
        {
            if (!parameters.Contains(binding.ParameterId)) Error("SCENE080", $"Binding parameter '{binding.ParameterId}' does not exist.", binding.ParameterId);
            bool targetExists = binding.TargetKind switch
            {
                SceneParameterTargetKind.LayerGain or SceneParameterTargetKind.LayerMacro => layers.Contains(binding.TargetId),
                SceneParameterTargetKind.EmitterRate or SceneParameterTargetKind.EmitterGain => emitters.Contains(binding.TargetId),
                SceneParameterTargetKind.BusGain => buses.Contains(binding.TargetId),
                _ => false
            };
            if (!targetExists) Error("SCENE081", $"Binding target '{binding.TargetId}' does not exist.", binding.TargetId);
            if (binding.TargetKind == SceneParameterTargetKind.LayerMacro && string.IsNullOrWhiteSpace(binding.TargetParameter)) Error("SCENE082", "LayerMacro binding requires a macro ID.", binding.TargetId);
        }

        int declaredPool = scene.Sounds.Sum(x => x.PoolSize);
        if (declaredPool < scene.MaximumVoices)
            diagnostics.Add(new SceneDiagnostic("SCENE090", SceneDiagnosticSeverity.Warning, "The sum of per-sound pool sizes is smaller than MaximumVoices; pool exhaustion may occur."));

        return new SceneValidationReport(diagnostics);

        void Error(string code, string message, string objectId = "") => diagnostics.Add(new SceneDiagnostic(code, SceneDiagnosticSeverity.Error, message, objectId));

        void ValidateUnique(IEnumerable<string> values, string kind, string code)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) Error(code, $"A {kind} ID is required.");
                else if (!seen.Add(value)) Error(code, $"Duplicate {kind} ID '{value}'.", value);
            }
        }

        void ValidateStateSelector(string machineId, IReadOnlyList<string> activeStates, string objectId)
        {
            if (string.IsNullOrEmpty(machineId)) return;
            if (!machines.TryGetValue(machineId, out SceneStateMachineDefinition? machine))
            {
                Error("SCENE091", $"State machine '{machineId}' does not exist.", objectId);
                return;
            }
            var stateIds = machine.States.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (string state in activeStates) if (!stateIds.Contains(state)) Error("SCENE092", $"Active state '{state}' does not exist in '{machineId}'.", objectId);
        }
    }

    private static void ValidateBusCycles(SoundSceneDefinition scene, List<SceneDiagnostic> diagnostics)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (SoundBusDefinition bus in scene.Buses)
            if (!string.IsNullOrWhiteSpace(bus.Id)) edges.TryAdd(bus.Id, []);
        foreach (SoundBusDefinition bus in scene.Buses)
        {
            if (!edges.TryGetValue(bus.Id, out List<string>? outgoing)) continue;
            if (!string.IsNullOrEmpty(bus.ParentId) && edges.ContainsKey(bus.ParentId)) outgoing.Add(bus.ParentId);
            foreach (SoundBusSendDefinition send in bus.Sends)
                if (edges.ContainsKey(send.TargetBusId)) outgoing.Add(send.TargetBusId);
        }
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in edges.Keys) Visit(id);
        void Visit(string id)
        {
            if (state.TryGetValue(id, out int current))
            {
                if (current == 1) diagnostics.Add(new SceneDiagnostic("SCENE034", SceneDiagnosticSeverity.Error, "Bus parent/send routing contains a cycle.", id));
                return;
            }
            state[id] = 1;
            foreach (string next in edges[id]) Visit(next);
            state[id] = 2;
        }
    }
}

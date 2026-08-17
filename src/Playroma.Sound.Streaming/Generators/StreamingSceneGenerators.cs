using Playroma.Sound.Core.Generation;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Generators;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.Streaming.Generators;

public sealed record StreamingSceneGenerationContext
{
    public ulong RootSeed { get; init; } = 1;
    public SoundQualityMode Quality { get; init; } = SoundQualityMode.RealtimeHigh;
    public string? SceneId { get; init; }
    public string? DisplayName { get; init; }
}

public sealed record StreamingSceneDescription
{
    public float Intensity { get; init; } = 0.5f;
    public float Activity { get; init; } = 0.5f;
    public float Distance { get; init; } = 0.25f;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.OpenField;
}

public sealed record GeneratedSoundScene(
    SoundSceneDefinition Scene,
    IReadOnlyDictionary<string, SoundGraphDefinition> Assets);

public interface IStreamingSceneGenerator
{
    string Id { get; }
    string DisplayName { get; }
    GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context);
}

public static class BuiltInStreamingSceneCatalog
{
    public static IReadOnlyList<IStreamingSceneGenerator> All { get; } =
    [
        new WindSceneGenerator(),
        new RainSceneGenerator(),
        new StormSceneGenerator(),
        new FireSceneGenerator(),
        new WaterSceneGenerator(),
        new EngineSceneGenerator(),
        new MachineSceneGenerator(),
        new IndustrialAmbienceGenerator(),
        new ForestAmbienceGenerator(),
        new CaveAmbienceGenerator(),
        new SpaceshipInteriorGenerator(),
        new HauntedAmbienceGenerator()
    ];

    public static IStreamingSceneGenerator GetRequired(string id) => All.FirstOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException($"Streaming scene generator '{id}' was not found.");
}

public abstract class StreamingSceneGeneratorBase : IStreamingSceneGenerator
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context);

    protected internal static SoundSceneDefinition Scene(string id, string name, ulong seed, int voices = 32) => new()
    {
        Id = id,
        DisplayName = name,
        RootSeed = seed,
        MaximumVoices = voices,
        Buses =
        [
            new SoundBusDefinition { Id = "master", Gain = 0.92f, Effects = [new SoundBusEffectDefinition { Kind = SoundBusEffectKind.Limiter, Amount = 0.12f }] },
            new SoundBusDefinition { Id = "bed", ParentId = "master", Gain = 1f },
            new SoundBusDefinition { Id = "events", ParentId = "master", Gain = 1f },
            new SoundBusDefinition { Id = "space", ParentId = "master", Gain = 1f, Effects = [new SoundBusEffectDefinition { Kind = SoundBusEffectKind.Reverb, Amount = 0.65f, Secondary = 0.45f, Mix = 0.28f, WetOnly = true }] }
        ],
        Parameters =
        [
            new SceneParameterDefinition { Id = "intensity", DisplayName = "Intensity", Minimum = 0, Maximum = 1, DefaultValue = 0.5f, SmoothingSeconds = 0.2 },
            new SceneParameterDefinition { Id = "activity", DisplayName = "Activity", Minimum = 0, Maximum = 1, DefaultValue = 0.5f, SmoothingSeconds = 0.2 }
        ]
    };

    protected internal static void AddAsset(Dictionary<string, SoundGraphDefinition> assets, SoundSceneDefinition scene, SoundGraphDefinition graph, int pool)
    {
        assets.Add(graph.Id, graph);
        scene.Sounds.Add(new SoundReferenceDefinition { SoundId = graph.Id, PoolSize = pool });
    }

    protected internal static SoundGenerationContext ShortContext(StreamingSceneGenerationContext context, string graphId, int variation = 0) => new()
    {
        RootSeed = context.RootSeed,
        FamilySeed = context.RootSeed ^ 0xA53C9E17UL,
        VariationIndex = variation,
        QualityMode = context.Quality,
        GraphId = graphId,
        DisplayName = graphId
    };
}

public sealed class WindSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.wind";
    public override string DisplayName => "Procedural Wind";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        string id = context.SceneId ?? "scene.wind";
        SoundSceneDefinition scene = Scene(id, context.DisplayName ?? DisplayName, context.RootSeed, 20);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition bed = LongGraphFactory.NoiseBed("stream.wind-bed", NoiseColor.Pink, 0.22f, 900f, 8_000f, context.RootSeed);
        SoundGraphDefinition gust = LongGraphFactory.NoiseEvent("stream.wind-gust", NoiseColor.Pink, 1.8, 0.34f, 600f, 5_000f, context.RootSeed + 1);
        AddAsset(assets, scene, bed, 1); AddAsset(assets, scene, gust, 6);
        scene.Layers.Add(new SoundLayerDefinition { Id = "wind", SoundId = bed.Id, BusId = "bed", Gain = 0.8f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "gusts", SoundIds = [gust.Id], BusId = "events", MinimumIntervalSeconds = 2, MaximumIntervalSeconds = 10, Gain = 0.8f, MaximumConcurrentVoices = 3, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 4, MaximumRadius = 30, MaximumDistance = 80 } });
        BindIntensity(scene, "wind", "gusts");
        return new GeneratedSoundScene(scene, assets);
    }

    internal static void BindIntensity(SoundSceneDefinition scene, string layerId, string emitterId)
    {
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.LayerGain, TargetId = layerId, OutputMinimum = 0.15f, OutputMaximum = 1.25f, Curve = SceneMappingCurve.SmoothStep });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "activity", TargetKind = SceneParameterTargetKind.EmitterRate, TargetId = emitterId, OutputMinimum = 0.25f, OutputMaximum = 2.2f, Curve = SceneMappingCurve.Exponential });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.EmitterGain, TargetId = emitterId, OutputMinimum = 0.25f, OutputMaximum = 1.2f });
    }
}

public sealed class RainSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.rain";
    public override string DisplayName => "Procedural Rain";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.rain", context.DisplayName ?? DisplayName, context.RootSeed, 28);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition rain = LongGraphFactory.NoiseBed("stream.rain-bed", NoiseColor.White, 0.14f, 1_800f, 14_000f, context.RootSeed);
        SoundGraphDefinition drop = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Water, MaterialB = SoundMaterial.Steel, Mass = 0.08f, Velocity = 0.35f, Environment = description.Environment }, ShortContext(context, "stream.rain-drop"));
        AddAsset(assets, scene, rain, 1); AddAsset(assets, scene, drop, 12);
        scene.Layers.Add(new SoundLayerDefinition { Id = "rain", SoundId = rain.Id, BusId = "bed", Gain = 0.72f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "drops", SoundIds = [drop.Id], BusId = "space", MinimumIntervalSeconds = 0.08, MaximumIntervalSeconds = 0.8, MaximumBurstCount = 3, BurstSpreadSeconds = 0.05, MaximumConcurrentVoices = 10, Gain = 0.42f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 1, MaximumRadius = 18, MaximumDistance = 45 } });
        WindSceneGenerator.BindIntensity(scene, "rain", "drops");
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class StormSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.storm";
    public override string DisplayName => "Procedural Storm";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.storm", context.DisplayName ?? DisplayName, context.RootSeed, 36);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition wind = LongGraphFactory.NoiseBed("stream.storm-wind", NoiseColor.Pink, 0.2f, 350f, 7_000f, context.RootSeed);
        SoundGraphDefinition rain = LongGraphFactory.NoiseBed("stream.storm-rain", NoiseColor.White, 0.13f, 2_000f, 15_000f, context.RootSeed + 1);
        SoundGraphDefinition thunder = new ExplosionGenerator().Generate(new ExplosionDescription { EnergyType = ExplosionEnergyType.Chemical, Size = 0.92f, Distance = description.Distance, Environment = description.Environment, Debris = 0.08f }, ShortContext(context, "stream.thunder"));
        AddAsset(assets, scene, wind, 1); AddAsset(assets, scene, rain, 1); AddAsset(assets, scene, thunder, 8);
        scene.Layers.Add(new SoundLayerDefinition { Id = "wind", SoundId = wind.Id, BusId = "bed", Gain = 0.8f, Protected = true });
        scene.Layers.Add(new SoundLayerDefinition { Id = "rain", SoundId = rain.Id, BusId = "bed", Gain = 0.75f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "thunder", SoundIds = [thunder.Id], BusId = "space", MinimumIntervalSeconds = 4, MaximumIntervalSeconds = 28, MaximumConcurrentVoices = 4, Gain = 0.95f, Priority = 60, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 10, MaximumRadius = 75, MinimumDistance = 5, MaximumDistance = 150 } });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.LayerGain, TargetId = "wind", OutputMinimum = 0.2f, OutputMaximum = 1.2f });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.LayerGain, TargetId = "rain", OutputMinimum = 0.15f, OutputMaximum = 1.15f });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "activity", TargetKind = SceneParameterTargetKind.EmitterRate, TargetId = "thunder", OutputMinimum = 0.25f, OutputMaximum = 2.5f, Curve = SceneMappingCurve.Exponential });
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class FireSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.fire";
    public override string DisplayName => "Procedural Fire";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.fire", context.DisplayName ?? DisplayName, context.RootSeed, 24);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition bed = LongGraphFactory.NoiseBed("stream.fire-bed", NoiseColor.Brown, 0.18f, 80f, 3_500f, context.RootSeed);
        SoundGraphDefinition pop = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Wood, MaterialB = SoundMaterial.Wood, Mass = 0.12f, Velocity = 0.55f, Breakage = 0.65f, Environment = description.Environment }, ShortContext(context, "stream.fire-pop"));
        AddAsset(assets, scene, bed, 1); AddAsset(assets, scene, pop, 10);
        scene.Layers.Add(new SoundLayerDefinition { Id = "flame", SoundId = bed.Id, BusId = "bed", Gain = 0.65f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "pops", SoundIds = [pop.Id], BusId = "events", MinimumIntervalSeconds = 0.18, MaximumIntervalSeconds = 2.2, MaximumBurstCount = 2, MaximumConcurrentVoices = 8, Gain = 0.5f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 0.2f, MaximumRadius = 2.5f, MaximumDistance = 18 } });
        WindSceneGenerator.BindIntensity(scene, "flame", "pops");
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class WaterSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.water";
    public override string DisplayName => "Procedural Water";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.water", context.DisplayName ?? DisplayName, context.RootSeed, 24);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition flow = LongGraphFactory.NoiseBed("stream.water-flow", NoiseColor.Pink, 0.16f, 120f, 7_000f, context.RootSeed);
        SoundGraphDefinition splash = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Water, MaterialB = SoundMaterial.Water, Mass = 0.35f, Velocity = 0.55f, Environment = description.Environment }, ShortContext(context, "stream.water-splash"));
        AddAsset(assets, scene, flow, 1); AddAsset(assets, scene, splash, 8);
        scene.Layers.Add(new SoundLayerDefinition { Id = "flow", SoundId = flow.Id, BusId = "bed", Gain = 0.75f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "splashes", SoundIds = [splash.Id], BusId = "space", MinimumIntervalSeconds = 0.8, MaximumIntervalSeconds = 5, MaximumConcurrentVoices = 5, Gain = 0.6f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 1, MaximumRadius = 12, MaximumDistance = 40 } });
        WindSceneGenerator.BindIntensity(scene, "flow", "splashes");
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class EngineSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.engine";
    public override string DisplayName => "Reactive Engine";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.engine", context.DisplayName ?? DisplayName, context.RootSeed, 20);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition engine = LongGraphFactory.EngineDrone("stream.engine-drone", context.RootSeed);
        SoundGraphDefinition clank = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Steel, MaterialB = SoundMaterial.Iron, Mass = 0.5f, Velocity = 0.35f, Environment = description.Environment }, ShortContext(context, "stream.engine-clank"));
        AddAsset(assets, scene, engine, 1); AddAsset(assets, scene, clank, 6);
        scene.StateMachines.Add(new SceneStateMachineDefinition
        {
            Id = "engine",
            InitialState = "off",
            States = [new SceneStateDefinition { Id = "off" }, new SceneStateDefinition { Id = "running" }, new SceneStateDefinition { Id = "damaged" }],
            Transitions =
            [
                new SceneTransitionDefinition { FromState = "off", ToState = "running", Trigger = "engine.start" },
                new SceneTransitionDefinition { FromState = "running", ToState = "damaged", Trigger = "engine.damage" },
                new SceneTransitionDefinition { FromState = "damaged", ToState = "running", Trigger = "engine.repair" },
                new SceneTransitionDefinition { FromState = "*", ToState = "off", Trigger = "engine.stop" }
            ]
        });
        scene.Layers.Add(new SoundLayerDefinition { Id = "motor", SoundId = engine.Id, BusId = "bed", Gain = 0.78f, Protected = true, StateMachineId = "engine", ActiveStates = ["running", "damaged"] });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "mechanics", SoundIds = [clank.Id], BusId = "events", MinimumIntervalSeconds = 0.8, MaximumIntervalSeconds = 4, Gain = 0.42f, StateMachineId = "engine", ActiveStates = ["running", "damaged"], MaximumConcurrentVoices = 4 });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.LayerMacro, TargetId = "motor", TargetParameter = "throttle", OutputMinimum = 0f, OutputMaximum = 1f });
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "activity", TargetKind = SceneParameterTargetKind.EmitterRate, TargetId = "mechanics", OutputMinimum = 0.3f, OutputMaximum = 2.5f });
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class MachineSceneGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.machine";
    public override string DisplayName => "Procedural Machine";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context) =>
        SceneRecipes.MachineLike(context, description, "scene.machine", DisplayName, false);
}

public sealed class IndustrialAmbienceGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.industrial";
    public override string DisplayName => "Industrial Ambience";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context) =>
        SceneRecipes.MachineLike(context, description, "scene.industrial", DisplayName, true);
}

public sealed class ForestAmbienceGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.forest";
    public override string DisplayName => "Forest Ambience";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.forest", context.DisplayName ?? DisplayName, context.RootSeed, 30);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition wind = LongGraphFactory.NoiseBed("stream.forest-wind", NoiseColor.Pink, 0.12f, 500f, 8_000f, context.RootSeed);
        SoundGraphDefinition bird = new CreatureVoiceGenerator().Generate(new CreatureVoiceDescription { Call = CreatureCallKind.Chirp, Size = 0.15f, Aggression = 0.05f, Duration = 0.35f, Environment = AcousticEnvironment.Forest }, ShortContext(context, "stream.forest-bird"));
        AddAsset(assets, scene, wind, 1); AddAsset(assets, scene, bird, 12);
        scene.Layers.Add(new SoundLayerDefinition { Id = "trees", SoundId = wind.Id, BusId = "bed", Gain = 0.48f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "birds", SoundIds = [bird.Id], BusId = "space", MinimumIntervalSeconds = 0.4, MaximumIntervalSeconds = 5, MaximumBurstCount = 3, BurstSpreadSeconds = 0.35, MaximumConcurrentVoices = 10, Gain = 0.55f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 4, MaximumRadius = 35, MaximumDistance = 70 } });
        WindSceneGenerator.BindIntensity(scene, "trees", "birds");
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class CaveAmbienceGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.cave";
    public override string DisplayName => "Cave Ambience";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.cave", context.DisplayName ?? DisplayName, context.RootSeed, 24);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition air = LongGraphFactory.NoiseBed("stream.cave-air", NoiseColor.Brown, 0.12f, 60f, 2_500f, context.RootSeed);
        SoundGraphDefinition drip = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Water, MaterialB = SoundMaterial.Stone, Mass = 0.06f, Velocity = 0.28f, Environment = AcousticEnvironment.Cave }, ShortContext(context, "stream.cave-drip"));
        AddAsset(assets, scene, air, 1); AddAsset(assets, scene, drip, 10);
        scene.Layers.Add(new SoundLayerDefinition { Id = "air", SoundId = air.Id, BusId = "bed", Gain = 0.45f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "drips", SoundIds = [drip.Id], BusId = "space", MinimumIntervalSeconds = 0.8, MaximumIntervalSeconds = 8, MaximumConcurrentVoices = 6, Gain = 0.6f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 2, MaximumRadius = 25, MaximumDistance = 60 } });
        WindSceneGenerator.BindIntensity(scene, "air", "drips");
        return new GeneratedSoundScene(scene, assets);
    }
}

public sealed class SpaceshipInteriorGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.spaceship";
    public override string DisplayName => "Spaceship Interior";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context) =>
        SceneRecipes.Technological(context, description, "scene.spaceship", DisplayName, true);
}

public sealed class HauntedAmbienceGenerator : StreamingSceneGeneratorBase
{
    public override string Id => "playroma.scene.haunted";
    public override string DisplayName => "Haunted Ambience";
    public override GeneratedSoundScene Generate(StreamingSceneDescription description, StreamingSceneGenerationContext context)
    {
        SoundSceneDefinition scene = Scene(context.SceneId ?? "scene.haunted", context.DisplayName ?? DisplayName, context.RootSeed, 30);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition drone = LongGraphFactory.Drone("stream.haunted-drone", 46, 0.16f, context.RootSeed);
        SoundGraphDefinition whisper = new CreatureVoiceGenerator().Generate(new CreatureVoiceDescription { Call = CreatureCallKind.Hiss, Size = 0.4f, Aggression = 0.15f, Duration = 1.2f, Environment = AcousticEnvironment.StoneChamber }, ShortContext(context, "stream.haunted-whisper"));
        SoundGraphDefinition creak = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Wood, MaterialB = SoundMaterial.Wood, Mass = 0.45f, Velocity = 0.12f, Environment = AcousticEnvironment.StoneChamber }, ShortContext(context, "stream.haunted-creak"));
        AddAsset(assets, scene, drone, 1); AddAsset(assets, scene, whisper, 8); AddAsset(assets, scene, creak, 8);
        scene.Layers.Add(new SoundLayerDefinition { Id = "presence", SoundId = drone.Id, BusId = "space", Gain = 0.55f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "manifestations", SoundIds = [whisper.Id, creak.Id], BusId = "space", MinimumIntervalSeconds = 2, MaximumIntervalSeconds = 14, MaximumConcurrentVoices = 5, Gain = 0.55f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 1, MaximumRadius = 20, MaximumDistance = 50 } });
        WindSceneGenerator.BindIntensity(scene, "presence", "manifestations");
        return new GeneratedSoundScene(scene, assets);
    }
}

internal static class SceneRecipes
{
    public static GeneratedSoundScene MachineLike(StreamingSceneGenerationContext context, StreamingSceneDescription description, string fallbackId, string name, bool busy)
    {
        SoundSceneDefinition scene = StreamingSceneGeneratorBase.Scene(context.SceneId ?? fallbackId, context.DisplayName ?? name, context.RootSeed, busy ? 36 : 24);
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        SoundGraphDefinition motor = LongGraphFactory.EngineDrone($"stream.{fallbackId}.motor", context.RootSeed);
        SoundGraphDefinition impact = new ImpactGenerator().Generate(new ImpactDescription { MaterialA = SoundMaterial.Steel, MaterialB = SoundMaterial.Steel, Mass = busy ? 0.7f : 0.45f, Velocity = 0.4f, Environment = description.Environment }, StreamingSceneGeneratorBase.ShortContext(context, $"stream.{fallbackId}.impact"));
        StreamingSceneGeneratorBase.AddAsset(assets, scene, motor, busy ? 2 : 1);
        StreamingSceneGeneratorBase.AddAsset(assets, scene, impact, busy ? 12 : 8);
        scene.Layers.Add(new SoundLayerDefinition { Id = "machinery", SoundId = motor.Id, BusId = "bed", Gain = 0.72f, Protected = true });
        scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "clanks", SoundIds = [impact.Id], BusId = "space", MinimumIntervalSeconds = busy ? 0.18 : 0.8, MaximumIntervalSeconds = busy ? 2.5 : 5, MaximumBurstCount = busy ? 3 : 1, MaximumConcurrentVoices = busy ? 10 : 5, Gain = 0.5f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 1, MaximumRadius = busy ? 25 : 8, MaximumDistance = busy ? 60 : 25 } });
        WindSceneGenerator.BindIntensity(scene, "machinery", "clanks");
        scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "intensity", TargetKind = SceneParameterTargetKind.LayerMacro, TargetId = "machinery", TargetParameter = "throttle", OutputMinimum = 0.05f, OutputMaximum = 1f });
        return new GeneratedSoundScene(scene, assets);
    }

    public static GeneratedSoundScene Technological(StreamingSceneGenerationContext context, StreamingSceneDescription description, string fallbackId, string name, bool sparks)
    {
        GeneratedSoundScene baseScene = MachineLike(context, description, fallbackId, name, true);
        if (!sparks) return baseScene;
        var assets = new Dictionary<string, SoundGraphDefinition>(baseScene.Assets, StringComparer.Ordinal);
        SoundGraphDefinition spark = new MagicGenerator().Generate(new MagicDescription { Archetype = MagicArchetype.Lightning, Power = 0.35f, Stability = 0.2f, Environment = AcousticEnvironment.IndustrialHall }, StreamingSceneGeneratorBase.ShortContext(context, $"stream.{fallbackId}.spark"));
        StreamingSceneGeneratorBase.AddAsset(assets, baseScene.Scene, spark, 8);
        baseScene.Scene.Emitters.Add(new SoundEventEmitterDefinition { Id = "sparks", SoundIds = [spark.Id], BusId = "events", MinimumIntervalSeconds = 2, MaximumIntervalSeconds = 16, MaximumConcurrentVoices = 4, Gain = 0.4f, SpatialDistribution = new SpatialDistributionDefinition { MinimumRadius = 1, MaximumRadius = 18, MaximumDistance = 40 } });
        baseScene.Scene.ParameterBindings.Add(new SceneParameterBindingDefinition { ParameterId = "activity", TargetKind = SceneParameterTargetKind.EmitterRate, TargetId = "sparks", OutputMinimum = 0.2f, OutputMaximum = 2f });
        return new GeneratedSoundScene(baseScene.Scene, assets);
    }
}

internal static class LongGraphFactory
{
    public static SoundGraphDefinition NoiseBed(string id, NoiseColor color, float amplitude, float lowCut, float highCut, ulong seed)
    {
        var graph = Base(id, id, seed);
        string noise = Add(graph, "noise", "core.noise", ("amplitude", amplitude), ("color", color.ToString()), ("durationSeconds", 0));
        string high = Add(graph, "highpass", "dsp.biquad", ("filterType", "HighPass"), ("frequencyHz", lowCut), ("q", 0.707), ("gainDecibels", 0));
        string low = Add(graph, "lowpass", "dsp.biquad", ("filterType", "LowPass"), ("frequencyHz", highCut), ("q", 0.707), ("gainDecibels", 0));
        string limiter = Add(graph, "limiter", "dsp.limiter", ("ceilingDecibels", -1), ("releaseMilliseconds", 120));
        Connect(graph, noise, high); Connect(graph, high, low); Connect(graph, low, limiter);
        graph.Macros.Add(Macro("intensity", 0.5f, Bind(noise, "amplitude", amplitude * 0.2f, amplitude * 2.2f)));
        graph.Macros.Add(Macro("brightness", 0.5f, Bind(low, "frequencyHz", Math.Max(200, highCut * 0.2f), highCut)));
        return Finish(graph, limiter);
    }

    public static SoundGraphDefinition NoiseEvent(string id, NoiseColor color, double duration, float amplitude, float lowCut, float highCut, ulong seed)
    {
        var graph = Base(id, id, seed);
        string noise = Add(graph, "noise", "core.noise", ("amplitude", amplitude), ("color", color.ToString()), ("durationSeconds", duration), ("attackSeconds", duration * 0.2), ("releaseSeconds", duration * 0.5));
        string high = Add(graph, "highpass", "dsp.biquad", ("filterType", "HighPass"), ("frequencyHz", lowCut), ("q", 0.707), ("gainDecibels", 0));
        string low = Add(graph, "lowpass", "dsp.biquad", ("filterType", "LowPass"), ("frequencyHz", highCut), ("q", 0.707), ("gainDecibels", 0));
        Connect(graph, noise, high); Connect(graph, high, low);
        return Finish(graph, low);
    }

    public static SoundGraphDefinition Drone(string id, double frequency, float amplitude, ulong seed)
    {
        var graph = Base(id, id, seed);
        string one = Add(graph, "fundamental", "core.oscillator", ("waveform", Waveform.Sine.ToString()), ("frequencyHz", frequency), ("amplitude", amplitude), ("durationSeconds", 0));
        string two = Add(graph, "overtone", "core.oscillator", ("waveform", Waveform.Triangle.ToString()), ("frequencyHz", frequency * 1.503), ("amplitude", amplitude * 0.35f), ("durationSeconds", 0));
        string mixer = Add(graph, "mixer", "core.mixer", ("gain", 1));
        Connect(graph, one, mixer); Connect(graph, two, mixer);
        string reverb = Add(graph, "reverb", "dsp.reverb", ("preDelayMilliseconds", 30), ("roomSize", 0.85), ("damping", 0.6), ("wet", 0.25), ("dry", 1), ("width", 1), ("tailSeconds", 8));
        Connect(graph, mixer, reverb);
        graph.Macros.Add(Macro("intensity", 0.5f, Bind(one, "amplitude", amplitude * 0.25f, amplitude * 1.8f), Bind(two, "amplitude", amplitude * 0.05f, amplitude * 0.7f)));
        return Finish(graph, reverb);
    }

    public static SoundGraphDefinition EngineDrone(string id, ulong seed)
    {
        var graph = Base(id, id, seed);
        string fundamental = Add(graph, "fundamental", "core.oscillator", ("waveform", Waveform.Saw.ToString()), ("frequencyHz", 45), ("amplitude", 0.11), ("durationSeconds", 0));
        string sub = Add(graph, "sub", "core.oscillator", ("waveform", Waveform.Sine.ToString()), ("frequencyHz", 22.5), ("amplitude", 0.16), ("durationSeconds", 0));
        string texture = Add(graph, "texture", "core.noise", ("color", NoiseColor.Brown.ToString()), ("amplitude", 0.045), ("durationSeconds", 0));
        string mixer = Add(graph, "mixer", "core.mixer", ("gain", 1));
        Connect(graph, fundamental, mixer); Connect(graph, sub, mixer); Connect(graph, texture, mixer);
        string filter = Add(graph, "filter", "dsp.biquad", ("filterType", "LowPass"), ("frequencyHz", 1800), ("q", 1.1), ("gainDecibels", 0));
        string saturation = Add(graph, "saturation", "dsp.waveshaper", ("mode", "HyperbolicTangent"), ("driveDecibels", 6), ("bias", 0), ("mix", 0.65), ("outputDecibels", -4));
        string limiter = Add(graph, "limiter", "dsp.limiter", ("ceilingDecibels", -1), ("releaseMilliseconds", 80));
        Connect(graph, mixer, filter); Connect(graph, filter, saturation); Connect(graph, saturation, limiter);
        graph.Macros.Add(Macro("throttle", 0.35f,
            Bind(fundamental, "frequencyHz", 35, 190, MacroMappingCurve.Exponential),
            Bind(sub, "frequencyHz", 17.5f, 95, MacroMappingCurve.Exponential),
            Bind(filter, "frequencyHz", 700, 6500, MacroMappingCurve.Exponential),
            Bind(texture, "amplitude", 0.02f, 0.12f)));
        return Finish(graph, limiter);
    }

    private static SoundGraphDefinition Base(string id, string name, ulong seed) => new()
    {
        Id = id,
        DisplayName = name,
        RootSeed = seed,
        Output = new GraphOutputDefinition { MaximumTailSeconds = 12, EnableLatencyCompensation = true }
    };

    private static string Add(SoundGraphDefinition graph, string id, string type, params (string Key, GraphValue Value)[] values)
    {
        var node = new NodeDefinition { Id = id, TypeId = type };
        foreach ((string key, GraphValue value) in values) node.Parameters[key] = value;
        graph.Nodes.Add(node);
        return id;
    }

    private static void Connect(SoundGraphDefinition graph, string from, string to) => graph.Connections.Add(new ConnectionDefinition { FromNodeId = from, ToNodeId = to });

    private static SoundGraphDefinition Finish(SoundGraphDefinition graph, string input)
    {
        string output = Add(graph, "output", "core.output");
        Connect(graph, input, output);
        graph.Output.NodeId = output;
        graph.Metadata["streaming.compatible"] = "true";
        return graph;
    }

    private static MacroDefinition Macro(string id, float defaultValue, params MacroBinding[] bindings) => new()
    {
        Id = id,
        DisplayName = id,
        Minimum = 0,
        Maximum = 1,
        DefaultValue = defaultValue,
        Bindings = [.. bindings]
    };

    private static MacroBinding Bind(string node, string parameter, float minimum, float maximum, MacroMappingCurve curve = MacroMappingCurve.Linear) => new()
    {
        TargetPath = $"node:{node}/parameter:{parameter}",
        InputMinimum = 0,
        InputMaximum = 1,
        OutputMinimum = minimum,
        OutputMaximum = maximum,
        Curve = curve
    };
}

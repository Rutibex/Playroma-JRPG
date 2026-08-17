using System.Collections.ObjectModel;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Streaming.Assets;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Runtime;
using Playroma.Sound.Streaming.Serialization;
using Playroma.Sound.Streaming.Validation;

namespace Playroma.Sound.Streaming.Compilation;

public sealed class SoundSceneCompilationException : Exception
{
    public SoundSceneCompilationException(SceneValidationReport report)
        : base("The sound scene contains validation errors.") => Report = report;

    public SceneValidationReport Report { get; }
}

public sealed class SoundSceneCompileOptions
{
    public int CommandQueueCapacity { get; init; } = 1024;
    public int TelemetryIntervalBlocks { get; init; } = 8;
    public int VoiceFadeFrames { get; init; } = 128;
    public int VirtualCatchUpBudgetFrames { get; init; } = 8192;
    public double StopTailSeconds { get; init; } = 1.5;
    public bool EnableVirtualization { get; init; } = true;
}

/// <summary>
/// Small immutable description safe to expose to editors and hosts.
/// The mutable authoring definition used to compile the scene is never exposed.
/// </summary>
public sealed record SoundSceneMetadata(
    string Id,
    string DisplayName,
    int FormatVersion,
    ulong RootSeed,
    int MaximumVoices,
    int SoundCount,
    int BusCount,
    int LayerCount,
    int EmitterCount,
    int EventClusterCount,
    int ParameterCount,
    int StateMachineCount,
    int AcousticZoneCount);

/// <summary>
/// Owns a fully detached, validated scene and detached graph assets.
/// All mutable inputs are cloned during compilation so later editor changes cannot
/// invalidate the prepared routing order, indices, pools, or graph plans.
/// </summary>
public sealed class PreparedSoundScene
{
    private readonly SoundSceneSerializer _sceneSerializer = new(indented: false);

    internal PreparedSoundScene(
        SoundSceneDefinition definition,
        AudioFormat format,
        GraphNodeRegistry registry,
        IReadOnlyDictionary<string, SoundGraphDefinition> assets,
        int[] busProcessingOrder,
        SoundSceneCompileOptions options)
    {
        Definition = definition;
        Format = format;
        Registry = registry;
        Assets = assets;
        BusProcessingOrder = busProcessingOrder;
        Options = options;
        Metadata = new SoundSceneMetadata(
            definition.Id,
            definition.DisplayName,
            definition.FormatVersion,
            definition.RootSeed,
            definition.MaximumVoices,
            definition.Sounds.Count,
            definition.Buses.Count,
            definition.Layers.Count,
            definition.Emitters.Count,
            definition.EventClusters.Count,
            definition.Parameters.Count,
            definition.StateMachines.Count,
            definition.AcousticZones.Count);
        AssetIds = Array.AsReadOnly(definition.Sounds.Select(item => item.SoundId).ToArray());
    }

    public SoundSceneMetadata Metadata { get; }
    public AudioFormat Format { get; }
    public IReadOnlyList<string> AssetIds { get; }

    internal SoundSceneDefinition Definition { get; }
    internal GraphNodeRegistry Registry { get; }
    internal IReadOnlyDictionary<string, SoundGraphDefinition> Assets { get; }
    internal int[] BusProcessingOrder { get; }
    internal SoundSceneCompileOptions Options { get; }

    /// <summary>Returns a detached editable copy. Mutating it never changes this prepared scene.</summary>
    public SoundSceneDefinition CreateEditableCopy() => _sceneSerializer.Deserialize(_sceneSerializer.Serialize(Definition));

    public StreamingSceneInstance CreateInstance() => new(this);
}

public sealed class SoundSceneCompiler
{
    private readonly GraphNodeRegistry _registry;

    public SoundSceneCompiler(GraphNodeRegistry? registry = null) => _registry = registry ?? BuiltInGraphRegistry.CreateDefault();

    public PreparedSoundScene Compile(
        SoundSceneDefinition definition,
        AudioFormat format,
        ISoundAssetResolver resolver,
        SoundSceneCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolver);
        options ??= new SoundSceneCompileOptions();
        ValidateOptions(options);

        // Compilation owns a normalized deep copy. Authoring objects remain free to change
        // immediately after this method returns without corrupting the runtime plan.
        var sceneSerializer = new SoundSceneSerializer(indented: false);
        SoundSceneDefinition ownedDefinition = sceneSerializer.Deserialize(sceneSerializer.Serialize(definition));

        SceneValidationReport report = new SoundSceneValidator().Validate(ownedDefinition);
        if (report.HasErrors) throw new SoundSceneCompilationException(report);

        var graphSerializer = new SoundGraphSerializer();
        var assets = new Dictionary<string, SoundGraphDefinition>(StringComparer.Ordinal);
        var graphValidator = new Playroma.Sound.Graph.Validation.GraphValidator(_registry);
        foreach (SoundReferenceDefinition sound in ownedDefinition.Sounds)
        {
            SoundGraphDefinition resolved = resolver.Resolve(sound.SoundId);
            SoundGraphDefinition graph = graphSerializer.Load(graphSerializer.Serialize(resolved));
            var graphReport = graphValidator.Validate(graph);
            if (graphReport.HasErrors)
            {
                throw new InvalidDataException($"Referenced sound graph '{sound.SoundId}' is invalid: {string.Join("; ", graphReport.Diagnostics.Select(item => item.Message))}");
            }
            assets.Add(sound.SoundId, graph);
        }

        int[] busOrder = CalculateBusOrder(ownedDefinition);
        var readOnlyAssets = new ReadOnlyDictionary<string, SoundGraphDefinition>(assets);
        return new PreparedSoundScene(ownedDefinition, format, _registry, readOnlyAssets, busOrder, options);
    }

    private static void ValidateOptions(SoundSceneCompileOptions options)
    {
        if (options.CommandQueueCapacity < 2) throw new ArgumentOutOfRangeException(nameof(options), "CommandQueueCapacity must be at least two.");
        if (options.TelemetryIntervalBlocks < 1) throw new ArgumentOutOfRangeException(nameof(options), "TelemetryIntervalBlocks must be positive.");
        if (options.VoiceFadeFrames < 1) throw new ArgumentOutOfRangeException(nameof(options), "VoiceFadeFrames must be positive.");
        if (options.VirtualCatchUpBudgetFrames < 1) throw new ArgumentOutOfRangeException(nameof(options), "VirtualCatchUpBudgetFrames must be positive.");
        if (!double.IsFinite(options.StopTailSeconds) || options.StopTailSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "StopTailSeconds must be finite and non-negative.");
    }

    private static int[] CalculateBusOrder(SoundSceneDefinition definition)
    {
        var index = definition.Buses.Select((bus, i) => (bus.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var indegree = new int[definition.Buses.Count];
        var adjacency = Enumerable.Range(0, definition.Buses.Count).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < definition.Buses.Count; i++)
        {
            SoundBusDefinition bus = definition.Buses[i];
            if (!string.IsNullOrEmpty(bus.ParentId)) AddEdge(i, index[bus.ParentId]);
            foreach (SoundBusSendDefinition send in bus.Sends) AddEdge(i, index[send.TargetBusId]);
        }
        var ready = new PriorityQueue<int, int>();
        for (int i = 0; i < indegree.Length; i++) if (indegree[i] == 0) ready.Enqueue(i, i);
        var order = new int[definition.Buses.Count];
        int count = 0;
        while (ready.Count > 0)
        {
            int current = ready.Dequeue();
            order[count++] = current;
            foreach (int next in adjacency[current]) if (--indegree[next] == 0) ready.Enqueue(next, next);
        }
        if (count != order.Length) throw new InvalidOperationException("Bus routing is cyclic after validation.");
        return order;

        void AddEdge(int from, int to)
        {
            adjacency[from].Add(to);
            indegree[to]++;
        }
    }
}

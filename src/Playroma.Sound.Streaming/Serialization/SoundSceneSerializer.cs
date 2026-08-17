using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.Streaming.Serialization;

public sealed class SoundSceneSerializer
{
    private readonly SortedDictionary<int, ISoundSceneMigration> _migrations = [];
    private readonly JsonSerializerOptions _options;

    public SoundSceneSerializer(bool indented = true, IEnumerable<ISoundSceneMigration>? migrations = null)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        if (migrations is not null)
        {
            foreach (ISoundSceneMigration migration in migrations) RegisterMigration(migration);
        }
    }

    public JsonSerializerOptions Options => _options;

    public void RegisterMigration(ISoundSceneMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.ToVersion != migration.FromVersion + 1)
            throw new ArgumentException("Scene migrations must advance exactly one format version.", nameof(migration));
        if (migration.FromVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(migration), "Scene migration versions cannot be negative.");
        if (!_migrations.TryAdd(migration.FromVersion, migration))
            throw new InvalidOperationException($"A scene migration from format {migration.FromVersion} is already registered.");
    }

    public string Serialize(SoundSceneDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, _options);
    }

    public SoundSceneDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonObject document = JsonNode.Parse(
            json,
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject()
            ?? throw new InvalidDataException("The .playstream document contained no scene definition.");

        int version = ReadFormatVersion(document);
        while (version < SoundSceneDefinition.CurrentFormatVersion)
        {
            if (!_migrations.TryGetValue(version, out ISoundSceneMigration? migration) || migration is null)
                throw new NotSupportedException($"No .playstream migration is registered for scene format {version}.");
            document = migration.Migrate(document);
            version = migration.ToVersion;
            document["formatVersion"] = version;
        }

        if (version > SoundSceneDefinition.CurrentFormatVersion)
            throw new NotSupportedException($"Scene format {version} is newer than this runtime supports ({SoundSceneDefinition.CurrentFormatVersion}).");

        SoundSceneDefinition definition = document.Deserialize<SoundSceneDefinition>(_options)
            ?? throw new InvalidDataException("The .playstream document contained no scene definition.");
        Normalize(definition);
        return definition;
    }

    public void Save(string path, SoundSceneDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Serialize(definition));
    }

    public SoundSceneDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(path));
    }

    private static int ReadFormatVersion(JsonObject document)
    {
        if (document.TryGetPropertyValue("formatVersion", out JsonNode? node) && node is not null)
            return node.GetValue<int>();
        return 1;
    }

    internal static void Normalize(SoundSceneDefinition definition)
    {
        definition.Id ??= "untitled.scene";
        definition.DisplayName ??= "Untitled Sound Scene";
        definition.Sounds ??= [];
        definition.Buses ??= [];
        definition.AcousticZones ??= [];
        definition.AcousticPortals ??= [];
        definition.Parameters ??= [];
        definition.ParameterBindings ??= [];
        definition.Layers ??= [];
        definition.Emitters ??= [];
        definition.EventClusters ??= [];
        definition.StateMachines ??= [];
        definition.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SoundBusDefinition bus in definition.Buses)
        {
            bus.Effects ??= [];
            bus.Sends ??= [];
        }
        foreach (SoundLayerDefinition layer in definition.Layers)
        {
            layer.ActiveStates ??= [];
            layer.Spatial ??= new SoundEmitterStateDefinition();
        }
        foreach (SoundEventEmitterDefinition emitter in definition.Emitters)
        {
            emitter.SoundIds ??= [];
            emitter.ActiveStates ??= [];
            emitter.SpatialDistribution ??= new SpatialDistributionDefinition();
        }
        foreach (SoundEventClusterDefinition cluster in definition.EventClusters)
        {
            cluster.Events ??= [];
            foreach (SoundClusterEventDefinition item in cluster.Events) item.Spatial ??= new SoundEmitterStateDefinition();
        }
        foreach (SceneStateMachineDefinition machine in definition.StateMachines)
        {
            machine.States ??= [];
            machine.Transitions ??= [];
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Serialization;

public sealed class SoundGraphSerializer
{
    private readonly SortedDictionary<int, IGraphMigration> _migrations = [];
    private readonly JsonSerializerOptions _options;

    public SoundGraphSerializer(IEnumerable<IGraphMigration>? migrations = null)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        _options.Converters.Add(new GraphValueJsonConverter());

        RegisterMigration(new GraphMigrationV1ToV2());
        if (migrations is not null)
        {
            foreach (IGraphMigration migration in migrations)
            {
                RegisterMigration(migration);
            }
        }
    }

    public JsonSerializerOptions Options => _options;

    public void RegisterMigration(IGraphMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.ToVersion != migration.FromVersion + 1)
        {
            throw new ArgumentException("Graph migrations must advance exactly one format version.", nameof(migration));
        }

        _migrations[migration.FromVersion] = migration;
    }

    public string Serialize(SoundGraphDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, _options);
    }

    public void Save(string path, SoundGraphDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Serialize(definition));
    }

    public SoundGraphDefinition Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonObject document = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The graph document is empty.");
        int version = document["formatVersion"]?.GetValue<int>() ?? 1;

        while (version < SoundGraphDefinition.CurrentFormatVersion)
        {
            if (!_migrations.TryGetValue(version, out IGraphMigration? migration) || migration is null)
            {
                throw new JsonException($"No graph migration is registered for format version {version}.");
            }

            document = migration.Migrate(document);
            version = migration.ToVersion;
        }

        if (version > SoundGraphDefinition.CurrentFormatVersion)
        {
            throw new JsonException($"Graph format version {version} is newer than this runtime supports.");
        }

        SoundGraphDefinition result = document.Deserialize<SoundGraphDefinition>(_options)
            ?? throw new JsonException("The graph could not be deserialized.");
        NormalizeCollections(result);
        return result;
    }

    public SoundGraphDefinition LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static void NormalizeCollections(SoundGraphDefinition definition)
    {
        definition.Nodes ??= [];
        definition.Connections ??= [];
        definition.Macros ??= [];
        definition.Output ??= new GraphOutputDefinition();
        definition.Editor ??= new GraphEditorDocumentMetadata();
        definition.Editor.Groups ??= [];
        definition.Editor.Comments ??= [];
        definition.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (NodeDefinition node in definition.Nodes)
        {
            node.Parameters ??= new Dictionary<string, GraphValue>(StringComparer.Ordinal);
        }

        foreach (MacroDefinition macro in definition.Macros)
        {
            macro.Bindings ??= [];
        }
    }
}

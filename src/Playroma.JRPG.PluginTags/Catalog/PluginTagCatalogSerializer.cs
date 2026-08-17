using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.PluginTags;

public sealed record PluginTagCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<PluginProviderManifest> Providers,
    IReadOnlyList<PluginFeatureDefinition> Features);

public sealed class PluginTagCatalogSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(PluginTagCatalog catalog, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        PluginTagCatalogDocument document = new(CurrentSchemaVersion, catalog.Providers.ToArray(), catalog.Features);
        JsonSerializerOptions options = new(Options) { WriteIndented = indented };
        return JsonSerializer.Serialize(document, options);
    }

    public void Save(PluginTagCatalog catalog, string path, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(catalog, indented));
    }

    public PluginTagCatalogDocument DeserializeDocument(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        PluginTagCatalogDocument document = JsonSerializer.Deserialize<PluginTagCatalogDocument>(json, Options)
            ?? throw new PluginTagCatalogException("Catalog JSON did not contain a document.");
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new PluginTagCatalogException($"Unsupported catalog schema version {document.SchemaVersion}; expected {CurrentSchemaVersion}.");
        return document;
    }

    public PluginTagCatalog Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return BuildCatalog(DeserializeDocument(File.ReadAllText(path)));
    }

    public PluginTagCatalog Deserialize(string json) => BuildCatalog(DeserializeDocument(json));

    public PluginTagCatalog BuildCatalog(PluginTagCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PluginTagCatalogBuilder builder = new();
        foreach (PluginProviderManifest provider in document.Providers) builder.AddProvider(provider);
        foreach (PluginFeatureDefinition feature in document.Features) builder.AddFeature(feature);
        return builder.Build();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new ReadOnlySetJsonConverterFactory());
        return options;
    }
}

/// <summary>
/// System.Text.Json can serialize IReadOnlySet&lt;T&gt;, but it cannot instantiate that
/// interface when reading JSON. Catalog feature definitions intentionally expose
/// IReadOnlySet&lt;T&gt; to callers, so teach the serializer to materialize those values
/// as HashSet&lt;T&gt; during deserialization rather than weakening the public model.
/// </summary>
internal sealed class ReadOnlySetJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type elementType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(ReadOnlySetJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType, nonPublic: true)!;
    }

    private sealed class ReadOnlySetJsonConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            return JsonSerializer.Deserialize<HashSet<T>>(ref reader, options)
                ?? new HashSet<T>();
        }

        public override void Write(
            Utf8JsonWriter writer,
            IReadOnlySet<T> value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.ToArray(), options);
        }
    }
}

using System.Text.Json;

namespace Playroma.JRPG.Mapping;

public sealed record MapEditorMetadataEntry
{
    public bool Favorite { get; init; }
    public string[] Tags { get; init; } = [];
}

public sealed record MapEditorMetadataDocument
{
    public const string CurrentFormat = "playroma-map-metadata/1";

    public string Format { get; init; } = CurrentFormat;
    public Dictionary<int, MapEditorMetadataEntry> Maps { get; init; } = [];

    public MapEditorMetadataEntry Get(int mapId) =>
        Maps.TryGetValue(mapId, out MapEditorMetadataEntry? value) ? value : new MapEditorMetadataEntry();

    public MapEditorMetadataDocument Set(int mapId, MapEditorMetadataEntry value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Dictionary<int, MapEditorMetadataEntry> updated = Maps.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (!value.Favorite && value.Tags.Length == 0) updated.Remove(mapId);
        else updated[mapId] = value with
        {
            Tags = value.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        return this with { Maps = updated };
    }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static MapEditorMetadataDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        MapEditorMetadataDocument result = JsonSerializer.Deserialize<MapEditorMetadataDocument>(json, SerializerOptions)
            ?? new MapEditorMetadataDocument();
        if (!string.Equals(result.Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported map metadata format '{result.Format}'.");
        }
        return result;
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

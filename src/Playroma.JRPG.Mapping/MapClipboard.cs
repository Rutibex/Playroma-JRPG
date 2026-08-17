using System.Text.Json;

namespace Playroma.JRPG.Mapping;

public sealed record MapClipboardEvent(
    int SourceId,
    int X,
    int Y,
    string RawJson);

public sealed record MapClipboardPayload
{
    public const string CurrentFormat = "playroma-map-clipboard/1";

    public required string Format { get; init; }
    public required Guid SourceProjectId { get; init; }
    public required int SourceMapId { get; init; }
    public required int SourceTilesetId { get; init; }
    public required string TilesetFingerprint { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int AnchorX { get; init; }
    public required int AnchorY { get; init; }
    public required Dictionary<int, int[]> Layers { get; init; }
    public required List<MapClipboardEvent> Events { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static MapClipboardPayload Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        MapClipboardPayload payload = JsonSerializer.Deserialize<MapClipboardPayload>(json, SerializerOptions)
            ?? throw new InvalidDataException("The clipboard did not contain a map payload.");
        if (!string.Equals(payload.Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported map clipboard format '{payload.Format}'.");
        }
        if (payload.Width <= 0 || payload.Height <= 0) throw new InvalidDataException("The map clipboard dimensions are invalid.");
        int expected = checked(payload.Width * payload.Height);
        if (payload.Layers.Any(pair => pair.Value is null || pair.Value.Length != expected))
        {
            throw new InvalidDataException("A map clipboard layer has an invalid data length.");
        }
        return payload;
    }

    public MapSelectionData ToSelection() => new(
        new MapRect(0, 0, Width, Height),
        Layers);

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

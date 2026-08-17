using System.Text.Json.Serialization;

namespace Playroma.Tile.Core.Models;

public sealed class TileProject
{
    public int FormatVersion { get; set; } = 4;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Playroma Tile Project";
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TileSetDefinition> TileSets { get; set; } = new();
    public List<AutotileAssetReference> Autotiles { get; set; } = new();
    public List<MaterialAssetReference> Materials { get; set; } = new();
    public List<TilesetConstructionReference> TilesetConstructions { get; set; } = new();

    [JsonIgnore]
    public string ProjectFilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string ProjectDirectory => string.IsNullOrWhiteSpace(ProjectFilePath)
        ? string.Empty
        : Path.GetDirectoryName(Path.GetFullPath(ProjectFilePath)) ?? string.Empty;
}

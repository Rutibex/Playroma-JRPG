namespace Playroma.Tile.Core.Models;

public sealed class TilesetConstructionReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TileSetId { get; set; }
    public string Name { get; set; } = "Tileset Construction";
    public string RelativeDefinitionPath { get; set; } = string.Empty;
    public string RelativeThumbnailPath { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

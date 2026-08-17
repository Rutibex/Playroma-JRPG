namespace Playroma.Tile.Core.Models;

public sealed class TileRegionMetadata
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AlternateName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public bool IsLocked { get; set; }
    public Guid? SourceAutotileId { get; set; }
    public int SourceAutotileRevision { get; set; }
    public DateTimeOffset? CompiledUtc { get; set; }
    public string CompiledSourceSha256 { get; set; } = string.Empty;
    public string CompiledOutputSha256 { get; set; } = string.Empty;
}

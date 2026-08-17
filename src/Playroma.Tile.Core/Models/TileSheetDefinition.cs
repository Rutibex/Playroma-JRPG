namespace Playroma.Tile.Core.Models;

public sealed class TileSheetDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MvSheetSlot Slot { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativeImagePath { get; set; } = string.Empty;
    public string? RelativeCatalogPath { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public DateTimeOffset ImportedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TileRegionMetadata> Regions { get; set; } = new();
}

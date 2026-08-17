namespace Playroma.Tile.Core.Models;

public sealed class MaterialAssetReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Material";
    public string RelativeRecipePath { get; set; } = string.Empty;
    public string RelativeThumbnailPath { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

namespace Playroma.Tile.Core.Models;

public sealed class AutotileAssetReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Autotile";
    public string RelativeDefinitionPath { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

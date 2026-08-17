namespace Playroma.Tile.Core.Models;

public sealed class TileSetDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Tileset";
    public string Description { get; set; } = string.Empty;
    public Guid? ConstructionId { get; set; }

    /// <summary>
    /// RPG Maker MV database record from which this tileset was loaded. The value is retained so
    /// Playroma Tile can update the same data/Tilesets.json entry without asking the user to map it again.
    /// </summary>
    public int? MvDatabaseRecordId { get; set; }

    /// <summary>
    /// Normalized RPG Maker MV project root associated with <see cref="MvDatabaseRecordId"/>.
    /// </summary>
    public string MvDatabaseProjectPath { get; set; } = string.Empty;

    public List<TileSheetDefinition> Sheets { get; set; } = new();
}

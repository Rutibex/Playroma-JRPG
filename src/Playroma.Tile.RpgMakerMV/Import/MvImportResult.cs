using Playroma.Tile.Core.Models;

namespace Playroma.Tile.RpgMakerMV.Import;

public sealed class MvImportResult
{
    public List<TileSheetDefinition> ImportedSheets { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> SkippedFiles { get; } = new();
}

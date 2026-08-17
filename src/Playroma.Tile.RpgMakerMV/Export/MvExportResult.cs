namespace Playroma.Tile.RpgMakerMV.Export;

public sealed class MvExportResult
{
    public List<string> ExportedFiles { get; } = new();
    public List<string> Warnings { get; } = new();
}

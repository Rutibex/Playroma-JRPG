namespace Playroma.Tile.RpgMakerMV.Export;

public sealed class MvExportOptions
{
    public bool IncludeCatalogTextFiles { get; set; } = true;
    public bool IncludeExportReport { get; set; } = true;
    public bool OverwriteExistingFiles { get; set; }
    public bool StopOnValidationErrors { get; set; } = true;
}

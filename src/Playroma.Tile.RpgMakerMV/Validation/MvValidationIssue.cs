namespace Playroma.Tile.RpgMakerMV.Validation;

public sealed record MvValidationIssue(
    ValidationSeverity Severity,
    string Message,
    string TileSetName,
    Guid? SheetId = null,
    int? RegionIndex = null);

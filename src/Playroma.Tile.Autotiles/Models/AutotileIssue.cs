namespace Playroma.Tile.Autotiles.Models;

public enum AutotileIssueSeverity
{
    Information,
    Warning,
    Error
}

public sealed record AutotileIssue(
    AutotileIssueSeverity Severity,
    string Code,
    string Message);

namespace Playroma.Sound.Graph.Validation;

public enum GraphDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record GraphDiagnostic(
    GraphDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null,
    string? PortId = null,
    string? Connection = null);

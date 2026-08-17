namespace Playroma.Sound.Graph.Validation;

public sealed class GraphValidationReport
{
    private readonly List<GraphDiagnostic> _diagnostics = [];

    public IReadOnlyList<GraphDiagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(item => item.Severity == GraphDiagnosticSeverity.Error);
    public int ErrorCount => _diagnostics.Count(item => item.Severity == GraphDiagnosticSeverity.Error);
    public int WarningCount => _diagnostics.Count(item => item.Severity == GraphDiagnosticSeverity.Warning);

    public void Add(GraphDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnostics.Add(diagnostic);
    }

    public void Error(string code, string message, string? nodeId = null, string? portId = null, string? connection = null) =>
        Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, code, message, nodeId, portId, connection));

    public void Warning(string code, string message, string? nodeId = null, string? portId = null, string? connection = null) =>
        Add(new GraphDiagnostic(GraphDiagnosticSeverity.Warning, code, message, nodeId, portId, connection));

    public void Info(string code, string message, string? nodeId = null, string? portId = null) =>
        Add(new GraphDiagnostic(GraphDiagnosticSeverity.Info, code, message, nodeId, portId));
}

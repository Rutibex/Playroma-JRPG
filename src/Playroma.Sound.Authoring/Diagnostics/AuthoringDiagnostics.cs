namespace Playroma.Sound.Authoring.Diagnostics;

public enum AuthoringDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record AuthoringDiagnostic(
    string Code,
    AuthoringDiagnosticSeverity Severity,
    string Message,
    string DocumentId = "",
    string ObjectId = "",
    string PropertyPath = "",
    string? SourcePath = null);

public sealed class ValidationSummary
{
    public static ValidationSummary Empty { get; } = new([]);

    public ValidationSummary(IEnumerable<AuthoringDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<AuthoringDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(item => item.Severity == AuthoringDiagnosticSeverity.Error);
    public int ErrorCount => Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Warning);
    public int InformationCount => Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Information);

    public ValidationSummary Merge(ValidationSummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ValidationSummary(Diagnostics.Concat(other.Diagnostics));
    }
}

public sealed class ValidationAggregator
{
    private readonly Dictionary<string, ValidationSummary> _summaries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Set(string sourceId, ValidationSummary summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(summary);
        lock (_gate) _summaries[sourceId] = summary;
    }

    public bool Remove(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        lock (_gate) return _summaries.Remove(sourceId);
    }

    public ValidationSummary Capture()
    {
        lock (_gate) return new ValidationSummary(_summaries.Values.SelectMany(item => item.Diagnostics));
    }
}

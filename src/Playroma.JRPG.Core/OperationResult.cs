namespace Playroma.JRPG.Core;

public sealed record OperationResult<T>(
    T? Value,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public bool HasFatalErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Fatal);

    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);

    public static OperationResult<T> Success(T value, IEnumerable<ProjectDiagnostic>? diagnostics = null) =>
        new(value, diagnostics?.ToArray() ?? []);

    public static OperationResult<T> Failure(IEnumerable<ProjectDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new OperationResult<T>(default, diagnostics.ToArray());
    }
}

namespace Playroma.JRPG.Validation;

public sealed class SnapshotDiagnosticValidator : IProjectValidator
{
    public string Name => "Loader diagnostics";

    public ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ProjectValidationDiagnostic> diagnostics = context.Workspace.Snapshot.Diagnostics
            .Select(diagnostic => new ProjectValidationDiagnostic(
                diagnostic,
                DocumentationKey: "loader/" + diagnostic.Code.ToLowerInvariant()))
            .ToArray();
        return ValueTask.FromResult(diagnostics);
    }
}

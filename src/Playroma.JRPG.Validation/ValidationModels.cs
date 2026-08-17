using Playroma.JRPG.Core;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed record DiagnosticFix(
    string Id,
    string Title,
    IEditorCommand Command,
    bool IsSafe = true);

public sealed record ProjectValidationDiagnostic(
    ProjectDiagnostic Diagnostic,
    DiagnosticFix? Fix = null,
    string? DocumentationKey = null)
{
    public string Code => Diagnostic.Code;
    public DiagnosticSeverity Severity => Diagnostic.Severity;
}

public sealed class ProjectValidationContext
{
    public ProjectValidationContext(
        IMvProjectWorkspace workspace,
        ProjectReferenceIndex references,
        ProjectTargetCatalog catalog)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        References = references ?? throw new ArgumentNullException(nameof(references));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public IMvProjectWorkspace Workspace { get; }
    public ProjectReferenceIndex References { get; }
    public ProjectTargetCatalog Catalog { get; }
}

public interface IProjectValidator
{
    string Name { get; }

    ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ValidationProgress(
    int CompletedValidators,
    int TotalValidators,
    string? CurrentValidator,
    int DiagnosticCount);

public sealed record ProjectValidationResult(
    IReadOnlyList<ProjectValidationDiagnostic> Diagnostics,
    DateTimeOffset CompletedUtc)
{
    public int ErrorCount => Diagnostics.Count(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
    public int WarningCount => Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning);
    public bool IsValid => ErrorCount == 0;
}

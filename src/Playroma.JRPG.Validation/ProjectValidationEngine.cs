using Playroma.JRPG.Indexing;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class ProjectValidationEngine
{
    private readonly IReadOnlyList<IProjectValidator> _validators;

    public ProjectValidationEngine(IEnumerable<IProjectValidator>? validators = null)
    {
        _validators = (validators ?? CreateDefaultValidators()).ToArray();
    }

    public IReadOnlyList<IProjectValidator> Validators => _validators;

    public async ValueTask<ProjectValidationResult> ValidateAsync(
        IMvProjectWorkspace workspace,
        ProjectReferenceIndex? references = null,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        references ??= await new MvProjectReferenceIndexer().BuildAsync(
            workspace,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        ProjectTargetCatalog catalog = await ProjectTargetCatalog.BuildAsync(workspace, cancellationToken).ConfigureAwait(false);
        ProjectValidationContext context = new(workspace, references, catalog);
        List<ProjectValidationDiagnostic> diagnostics = [];
        for (int index = 0; index < _validators.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IProjectValidator validator = _validators[index];
            IReadOnlyList<ProjectValidationDiagnostic> results = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(results);
            progress?.Report(new ValidationProgress(index + 1, _validators.Count, validator.Name, diagnostics.Count));
        }

        ProjectValidationDiagnostic[] ordered = diagnostics
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Diagnostic.Location.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Diagnostic.Location.RecordId)
            .ThenBy(item => item.Diagnostic.Location.JsonPath, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
        return new ProjectValidationResult(ordered, DateTimeOffset.UtcNow);
    }

    private static IEnumerable<IProjectValidator> CreateDefaultValidators()
    {
        yield return new SnapshotDiagnosticValidator();
        yield return new MissingReferenceValidator();
        yield return new ProjectStructureValidator();
        yield return new ActorClassValidator();
        yield return new Phase9BDatabaseValidator();
        yield return new Phase9CEnemyTroopValidator();
        yield return new Phase9DAnimationValidator();
        yield return new Phase9ESystemTilesetValidator();
        yield return new Phase10EventValidator();
        yield return new EventStructureValidator();
    }
}

using Playroma.JRPG.Documents.Saving;

namespace Playroma.JRPG.Editing;

public interface IPreviewResourceCommand
{
    void ReleasePreviewResources();
}

public abstract class FileOverlayCommand : IEditorCommand, IPreviewResourceCommand
{
    private PendingFileMutation? _prepared;

    protected FileOverlayCommand(string description, params string[] relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
        Targets = relativePaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(EditorMutationTarget.ForFile)
            .Distinct()
            .ToArray();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        IProjectFileOverlay overlay = context.FileOverlay
            ?? throw new InvalidOperationException("The editing session has no project file overlay.");
        _prepared ??= await PrepareAsync(overlay, cancellationToken).ConfigureAwait(false);
        overlay.Stage(_prepared);
    }

    public ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IProjectFileOverlay overlay = context.FileOverlay
            ?? throw new InvalidOperationException("The editing session has no project file overlay.");
        if (_prepared is not null) overlay.Unstage(_prepared.Id);
        return ValueTask.CompletedTask;
    }

    public virtual void ReleasePreviewResources()
    {
        if (_prepared is { StagedContentPath: { Length: > 0 } staged })
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _prepared = null;
    }

    protected abstract ValueTask<PendingFileMutation> PrepareAsync(
        IProjectFileOverlay overlay,
        CancellationToken cancellationToken);
}

public sealed class ImportAssetFileCommand : FileOverlayCommand
{
    public ImportAssetFileCommand(string sourceFile, string destinationRelativePath)
        : base($"Import {destinationRelativePath}", destinationRelativePath)
    {
        SourceFile = Path.GetFullPath(sourceFile);
        DestinationRelativePath = destinationRelativePath.Replace('\\', '/');
    }

    public string SourceFile { get; }
    public string DestinationRelativePath { get; }

    protected override ValueTask<PendingFileMutation> PrepareAsync(IProjectFileOverlay overlay, CancellationToken cancellationToken) =>
        overlay.CreateStagedMutationAsync(
            FileMutationKind.Create,
            SourceFile,
            existingRelativePath: null,
            DestinationRelativePath,
            Description,
            cancellationToken);
}

public sealed class ReplaceAssetFileCommand : FileOverlayCommand
{
    public ReplaceAssetFileCommand(string sourceFile, string destinationRelativePath)
        : base($"Replace {destinationRelativePath}", destinationRelativePath)
    {
        SourceFile = Path.GetFullPath(sourceFile);
        DestinationRelativePath = destinationRelativePath.Replace('\\', '/');
    }

    public string SourceFile { get; }
    public string DestinationRelativePath { get; }

    protected override ValueTask<PendingFileMutation> PrepareAsync(IProjectFileOverlay overlay, CancellationToken cancellationToken) =>
        overlay.CreateStagedMutationAsync(
            FileMutationKind.Replace,
            SourceFile,
            DestinationRelativePath,
            DestinationRelativePath,
            Description,
            cancellationToken);
}

public sealed class MoveAssetFileCommand : FileOverlayCommand
{
    public MoveAssetFileCommand(string sourceRelativePath, string destinationRelativePath)
        : base($"Move {sourceRelativePath} to {destinationRelativePath}", sourceRelativePath, destinationRelativePath)
    {
        SourceRelativePath = sourceRelativePath.Replace('\\', '/');
        DestinationRelativePath = destinationRelativePath.Replace('\\', '/');
    }

    public string SourceRelativePath { get; }
    public string DestinationRelativePath { get; }

    protected override ValueTask<PendingFileMutation> PrepareAsync(IProjectFileOverlay overlay, CancellationToken cancellationToken) =>
        overlay.CreatePathMutationAsync(
            FileMutationKind.Move,
            SourceRelativePath,
            DestinationRelativePath,
            Description,
            cancellationToken);
}

public sealed class DeleteAssetFileCommand : FileOverlayCommand
{
    public DeleteAssetFileCommand(string sourceRelativePath)
        : base($"Delete {sourceRelativePath}", sourceRelativePath)
    {
        SourceRelativePath = sourceRelativePath.Replace('\\', '/');
    }

    public string SourceRelativePath { get; }

    protected override ValueTask<PendingFileMutation> PrepareAsync(IProjectFileOverlay overlay, CancellationToken cancellationToken) =>
        overlay.CreatePathMutationAsync(
            FileMutationKind.Delete,
            SourceRelativePath,
            destinationRelativePath: null,
            Description,
            cancellationToken);
}

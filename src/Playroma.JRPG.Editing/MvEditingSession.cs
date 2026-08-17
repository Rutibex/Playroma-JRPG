using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editing;

public sealed class MvEditingSession : IAsyncDisposable
{
    private MvEditingSession(MvProjectWorkspace workspace)
    {
        Workspace = workspace;
        Context = new EditorCommandContext(new WorkspaceEditorDocumentResolver(workspace), workspace.FileOverlay);
        Undo = new UndoManager(Context);
    }

    public MvProjectWorkspace Workspace { get; }
    public IProjectFileOverlay Files => Workspace.FileOverlay;
    public EditorCommandContext Context { get; }
    public UndoManager Undo { get; }

    public static async ValueTask<OperationResult<MvEditingSession>> OpenAsync(
        string projectPath,
        DocumentCacheOptions? cacheOptions = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(
            projectPath,
            cacheOptions,
            cancellationToken).ConfigureAwait(false);
        if (opened.Value is null)
        {
            return OperationResult<MvEditingSession>.Failure(opened.Diagnostics);
        }

        return OperationResult<MvEditingSession>.Success(new MvEditingSession(opened.Value), opened.Diagnostics);
    }

    public ProjectTransaction BeginTransaction(string description, bool allowHistoryMerge = false) =>
        new(description, Context, Undo, allowHistoryMerge);

    public async ValueTask<ProjectSaveResult> SaveAsync(
        ProjectSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ProjectSaveResult result = await Workspace.SaveAsync(
            options ?? new ProjectSaveOptions(),
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            // File commands are an overlay until save. Once their staged content has been
            // committed and securely removed, retaining those command objects would permit
            // an unsafe post-save redo. JSON-only saves keep normal history; a file save
            // starts a new safe history epoch.
            if (result.Files.Count > 0) Undo.Clear();
            else Undo.MarkSaved();
        }
        return result;
    }

    public ValueTask DisposeAsync() => Workspace.DisposeAsync();
}

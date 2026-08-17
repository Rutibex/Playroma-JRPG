namespace Playroma.JRPG.Editor.WinForms.Application;

public interface IEditorApplicationController
{
    EditorApplicationState State { get; }
    IProjectWorkspaceHost? CurrentWorkspaceHost { get; }
    event EventHandler<EditorApplicationStateChangedEventArgs>? StateChanged;

    Task<bool> OpenProjectAsync(
        string path,
        ProjectOpenOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> CloseProjectAsync(
        ProjectCloseReason reason,
        CancellationToken cancellationToken = default);

    Task<bool> ExitApplicationAsync(CancellationToken cancellationToken = default);
}

using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Application;

/// <summary>
/// Abstraction for the dedicated Map Editor which owns the primary project workspace.
/// Application lifetime depends on this narrow contract rather than on Form internals.
/// </summary>
public interface IProjectWorkspaceHost
{
    Form Form { get; }

    Task InitializeProjectAsync(CancellationToken cancellationToken = default);

    string? CapturePrimaryWorkspaceStateJson();

    Task RestorePrimaryWorkspaceStateJsonAsync(
        string? stateJson,
        CancellationToken cancellationToken = default);

    Task<bool> CanCloseProjectAsync(CancellationToken cancellationToken = default);

    Task CloseProjectContentForcedAsync(CancellationToken cancellationToken = default);

    Task OpenDocumentAsync(
        DocumentRequest request,
        CancellationToken cancellationToken = default);

    Task NavigateBackAsync(CancellationToken cancellationToken = default);

    Task NavigateForwardAsync(CancellationToken cancellationToken = default);

    void ShowCommandPalette();

    void ResetLayout();

    void ApplyLoadedSettings(bool resetLayout = false);

    void CloseFromController();
}

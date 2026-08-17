using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Windowing;


public interface IWinFormsOwnerProvider
{
    IWin32Window? CurrentOwner { get; }
}

public sealed class WindowCloseRequestedEventArgs : EventArgs
{
    public WindowCloseRequestedEventArgs(CloseReason closeReason) => CloseReason = closeReason;
    public CloseReason CloseReason { get; }
}

/// <summary>
/// WinForms-specific extension of the platform-neutral editor-window contract.
/// </summary>
public interface IWinFormsEditorWindow : IEditorWindow
{
    Form Form { get; }
    event EventHandler<WindowCloseRequestedEventArgs>? CloseRequested;

    /// <summary>Called by the registry after close validation succeeds or fails.</summary>
    void CompleteCloseRequest(bool closeWindow);
}

public interface IWinFormsWindowFactory
{
    EditorWindowKind Kind { get; }
    ValueTask<IWinFormsEditorWindow> CreateAsync(
        WindowRequest request,
        CancellationToken cancellationToken = default);
}

public interface IWinFormsWindowService : IWindowService, IActiveWindowService
{
    WinFormsWindowFactoryRegistry Factories { get; }
    ValueTask<DialogResult> ShowDialogAsync(
        WindowRequest request,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default);

    ValueTask<DialogResult> ShowDialogPreparedAsync(
        WindowRequest request,
        Func<IWinFormsEditorWindow, CancellationToken, ValueTask> prepare,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default);
}

public sealed class DelegateWinFormsWindowFactory : IWinFormsWindowFactory
{
    private readonly Func<WindowRequest, CancellationToken, ValueTask<IWinFormsEditorWindow>> _factory;

    public DelegateWinFormsWindowFactory(
        EditorWindowKind kind,
        Func<WindowRequest, CancellationToken, ValueTask<IWinFormsEditorWindow>> factory)
    {
        Kind = kind;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public EditorWindowKind Kind { get; }

    public ValueTask<IWinFormsEditorWindow> CreateAsync(
        WindowRequest request,
        CancellationToken cancellationToken = default) => _factory(request, cancellationToken);
}

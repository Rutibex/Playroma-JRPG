using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Forms;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

public sealed record CoordinatedShutdownPlan(
    IReadOnlyList<ShutdownContribution> Contributions)
{
    public bool HasLocalChanges => Contributions.Count > 0;
    public bool CanApplyAll => Contributions.All(item => item.CanApply);
    public bool CanDiscardAll => Contributions.All(item => item.CanDiscard);
    public bool CanRecoverAll => Contributions.All(item => item.CanRecover);
    public int PendingItemCount => Contributions.Sum(item => Math.Max(1, item.PendingItemCount));
}

public interface IWinFormsWorkspaceRecoveryCoordinator
{
    IReadOnlyList<PrivateSessionRecoveryState> PendingRecoveredSessions { get; }
    event EventHandler? PendingRecoveredSessionsChanged;

    ValueTask<ProjectWorkspaceSnapshot?> CaptureCurrentAsync(
        bool includePrivateSessions,
        CancellationToken cancellationToken = default);

    ValueTask SaveSuccessfulWorkspaceAsync(
        IProjectWorkspaceHost host,
        CancellationToken cancellationToken = default);

    ValueTask<ProjectWorkspaceSnapshot?> ReadSuccessfulWorkspaceAsync(
        string projectRoot,
        CancellationToken cancellationToken = default);

    ValueTask RestoreWorkspaceAsync(
        IProjectWorkspaceHost host,
        ProjectWorkspaceSnapshot snapshot,
        bool restoreWindows,
        bool restorePrivateSessions,
        CancellationToken cancellationToken = default);

    ValueTask RestoreLegacyDocumentRequestsAsync(
        IProjectWorkspaceHost host,
        IEnumerable<DocumentRequest> requests,
        CancellationToken cancellationToken = default);

    ValueTask<CoordinatedShutdownPlan> PrepareShutdownAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> CommitShutdownAsync(
        CoordinatedShutdownPlan plan,
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default);

    ValueTask<bool> OpenRecoveredSessionAsync(
        PrivateSessionRecoveryState state,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default);

    void DiscardRecoveredSession(PrivateSessionRecoveryState state);
}

/// <summary>
/// Captures and restores project-specific multi-window state. This coordinator is the only
/// component which knows both WinForms placement and the platform-neutral recovery contracts.
/// </summary>
public sealed class WinFormsWorkspaceRecoveryCoordinator :
    IWinFormsWorkspaceRecoveryCoordinator,
    IAsyncDisposable
{
    private readonly Control _dispatcher;
    private readonly EditorApplicationController _controller;
    private readonly IWorkspaceService _workspace;
    private readonly IProjectWorkspaceStateStore _workspaceState;
    private readonly WorkspaceRecoveryContextBroker _recoveryContext;
    private readonly IWinFormsWindowService _windows;
    private readonly IOutputService _output;
    private readonly List<PrivateSessionRecoveryState> _pendingRecoveredSessions = [];
    private readonly SemaphoreSlim _workspaceStateWriteGate = new(1, 1);
    private readonly object _autosaveSync = new();
    private readonly System.Threading.Timer _successfulWorkspaceTimer;
    private CancellationTokenSource? _autosaveCancellation;
    private Task _autosaveTask = Task.CompletedTask;
    private bool _disposed;

    public WinFormsWorkspaceRecoveryCoordinator(
        Control dispatcher,
        EditorApplicationController controller,
        IWorkspaceService workspace,
        IProjectWorkspaceStateStore workspaceState,
        WorkspaceRecoveryContextBroker recoveryContext,
        IWinFormsWindowService windows,
        IOutputService output)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        _recoveryContext = recoveryContext ?? throw new ArgumentNullException(nameof(recoveryContext));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _output = output ?? throw new ArgumentNullException(nameof(output));

        _windows.WindowOpened += OnWindowOpened;
        _windows.WindowClosed += OnWindowClosed;
        _windows.ActiveWindowChanged += OnActiveWindowChanged;
        foreach (IEditorWindow window in _windows.OpenWindows) window.StateChanged += OnWindowStateChanged;
        _recoveryContext.SetCaptureProvider(cancellationToken => CaptureCurrentAsync(
            includePrivateSessions: true,
            cancellationToken: cancellationToken));
        _successfulWorkspaceTimer = new System.Threading.Timer(
            static state => ((WinFormsWorkspaceRecoveryCoordinator)state!).ScheduleSuccessfulWorkspaceSave(),
            this,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public IReadOnlyList<PrivateSessionRecoveryState> PendingRecoveredSessions =>
        _pendingRecoveredSessions.ToArray();

    public event EventHandler? PendingRecoveredSessionsChanged;

    public ValueTask<ProjectWorkspaceSnapshot?> CaptureCurrentAsync(
        bool includePrivateSessions,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CaptureCurrentCoreAsync(includePrivateSessions, cancellationToken),
            cancellationToken);

    public async ValueTask SaveSuccessfulWorkspaceAsync(
        IProjectWorkspaceHost host,
        CancellationToken cancellationToken = default)
    {
        CancelScheduledSuccessfulWorkspaceSave();
        ProjectWorkspaceSnapshot? snapshot = await RunOnUiThreadAsync(
            () => CaptureCoreAsync(host, includePrivateSessions: false, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (snapshot is not null)
        {
            await WriteSuccessfulWorkspaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask<ProjectWorkspaceSnapshot?> ReadSuccessfulWorkspaceAsync(
        string projectRoot,
        CancellationToken cancellationToken = default) =>
        _workspaceState.ReadAsync(projectRoot, cancellationToken);

    public ValueTask RestoreWorkspaceAsync(
        IProjectWorkspaceHost host,
        ProjectWorkspaceSnapshot snapshot,
        bool restoreWindows,
        bool restorePrivateSessions,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => RestoreWorkspaceCoreAsync(
                host,
                snapshot,
                restoreWindows,
                restorePrivateSessions,
                cancellationToken),
            cancellationToken);

    public ValueTask RestoreLegacyDocumentRequestsAsync(
        IProjectWorkspaceHost host,
        IEnumerable<DocumentRequest> requests,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            async () =>
            {
                HashSet<string> restored = new(StringComparer.OrdinalIgnoreCase);
                foreach (DocumentRequest request in requests ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string key = $"{request.Kind}:{request.Identity}";
                    if (!restored.Add(key)) continue;
                    try
                    {
                        await host.OpenDocumentAsync(request, cancellationToken).ConfigureAwait(true);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
                    {
                        _output.Write(
                            "Recovery",
                            $"Skipped an unsupported legacy editor request: {key}",
                            Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                            exception.Message);
                    }
                }
            },
            cancellationToken);

    public ValueTask<CoordinatedShutdownPlan> PrepareShutdownAsync(
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => PrepareShutdownCoreAsync(cancellationToken),
            cancellationToken);

    public ValueTask<bool> CommitShutdownAsync(
        CoordinatedShutdownPlan plan,
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CommitShutdownCoreAsync(plan, action, cancellationToken),
            cancellationToken);

    public ValueTask<bool> OpenRecoveredSessionAsync(
        PrivateSessionRecoveryState state,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => OpenRecoveredSessionCoreAsync(state, owner, cancellationToken),
            cancellationToken);

    public void DiscardRecoveredSession(PrivateSessionRecoveryState state)
    {
        if (_dispatcher.InvokeRequired)
        {
            _dispatcher.BeginInvoke(new Action(() => DiscardRecoveredSession(state)));
            return;
        }
        RemovePending(state);
        _workspace.RequestRecoverySnapshot();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _successfulWorkspaceTimer.Dispose();
        Task pendingSave = CancelScheduledSuccessfulWorkspaceSave();
        try
        {
            await pendingSave.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when a debounced workspace save is cancelled during application shutdown.
        }
        catch (Exception exception)
        {
            _output.Write(
                "Workspace",
                "A pending workspace-state save did not finish cleanly during shutdown.",
                Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                exception.ToString());
        }
        _windows.WindowOpened -= OnWindowOpened;
        _windows.WindowClosed -= OnWindowClosed;
        _windows.ActiveWindowChanged -= OnActiveWindowChanged;
        foreach (IEditorWindow window in _windows.OpenWindows) window.StateChanged -= OnWindowStateChanged;
        _recoveryContext.ClearCaptureProvider();
        _pendingRecoveredSessions.Clear();
        _workspaceStateWriteGate.Dispose();
    }

    private ValueTask<ProjectWorkspaceSnapshot?> CaptureCurrentCoreAsync(
        bool includePrivateSessions,
        CancellationToken cancellationToken)
    {
        IProjectWorkspaceHost? host = _controller.CurrentWorkspaceHost;
        return host is null
            ? ValueTask.FromResult<ProjectWorkspaceSnapshot?>(null)
            : CaptureCoreAsync(host, includePrivateSessions, cancellationToken);
    }

    private async ValueTask<ProjectWorkspaceSnapshot?> CaptureCoreAsync(
        IProjectWorkspaceHost host,
        bool includePrivateSessions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _workspace.CurrentSession;
        if (session is null) return null;

        List<WindowWorkspaceState> windowStates = [];
        List<PrivateSessionRecoveryState> privateSessions = [];
        IEditorWindow[] openWindows = _windows.OpenWindows
            .Where(window => window.Request.Scope == WindowScope.Project)
            .ToArray();

        for (int index = 0; index < openWindows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEditorWindow window = openWindows[index];
            NavigationLocation? location = window.CaptureLocation();
            string? customState = window is IWorkspaceStateContributor contributor
                ? await contributor.CaptureWorkspaceStateJsonAsync(cancellationToken).ConfigureAwait(true)
                : null;

            ShellBounds? bounds = null;
            bool maximized = false;
            string? monitor = null;
            bool visible = window.IsVisible;
            if (window is IWinFormsEditorWindow winForms)
            {
                Rectangle normal = winForms.Form.WindowState == FormWindowState.Normal
                    ? winForms.Form.Bounds
                    : winForms.Form.RestoreBounds;
                if (normal.Width > 0 && normal.Height > 0)
                {
                    bounds = new ShellBounds(normal.X, normal.Y, normal.Width, normal.Height);
                }
                maximized = winForms.Form.WindowState == FormWindowState.Maximized;
                monitor = Screen.FromControl(winForms.Form).DeviceName;
                visible = winForms.Form.Visible;
            }

            windowStates.Add(new WindowWorkspaceState(
                window.Request,
                location,
                bounds,
                maximized,
                monitor,
                visible,
                index,
                customState));

            if (includePrivateSessions && window is IShutdownParticipant participant)
            {
                PrivateSessionRecoveryState? state = await participant
                    .CaptureRecoveryStateAsync(cancellationToken)
                    .ConfigureAwait(true);
                if (state is not null) privateSessions.Add(state);
            }
        }

        if (includePrivateSessions)
        {
            foreach (PrivateSessionRecoveryState pending in _pendingRecoveredSessions)
            {
                if (privateSessions.All(item => item.Key != pending.Key)) privateSessions.Add(pending);
            }
        }

        WindowIdentityState? active = Form.ActiveForm == host.Form
            ? new WindowIdentityState(EditorWindowKind.MapEditor, "map-editor")
            : _windows.ActiveWindow is { } activeWindow
                ? WindowIdentityState.From(activeWindow.Key)
                : null;

        return new ProjectWorkspaceSnapshot(
            session.Workspace.Snapshot.Identity.ProjectRoot,
            DateTimeOffset.UtcNow,
            host.CapturePrimaryWorkspaceStateJson(),
            windowStates,
            active,
            privateSessions,
            Version: 1);
    }

    private async ValueTask RestoreWorkspaceCoreAsync(
        IProjectWorkspaceHost host,
        ProjectWorkspaceSnapshot snapshot,
        bool restoreWindows,
        bool restorePrivateSessions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_workspace.CurrentSession is not { } session) return;
        if (!PathsEqual(session.Workspace.Snapshot.Identity.ProjectRoot, snapshot.ProjectRoot)) return;

        await host.RestorePrimaryWorkspaceStateJsonAsync(
            snapshot.PrimaryWorkspaceStateJson,
            cancellationToken).ConfigureAwait(true);

        Dictionary<WindowKey, IEditorWindow> restored = [];
        if (restoreWindows)
        {
            foreach (WindowWorkspaceState state in snapshot.EffectiveWindows
                         .OrderBy(item => item.ActivationOrder)
                         .DistinctBy(item => item.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!state.WasVisible || state.Request.Scope != WindowScope.Project) continue;
                if (state.Request.Kind is EditorWindowKind.EventEditor or EditorWindowKind.EventCommands
                    or EditorWindowKind.EventCommandParameters or EditorWindowKind.RecoveredSessions) continue;
                if (state.Request.Presentation == WindowPresentation.Modal) continue;

                WindowRequest request = state.Request with { Location = state.Location };
                try
                {
                    IEditorWindow window = await _windows.OpenAsync(request, cancellationToken).ConfigureAwait(true);
                    restored[window.Key] = window;
                    if (state.Location is not null)
                    {
                        await window.RestoreLocationAsync(state.Location, cancellationToken).ConfigureAwait(true);
                    }
                    if (window is IWorkspaceStateContributor contributor)
                    {
                        await contributor.RestoreWorkspaceStateJsonAsync(
                            state.CustomStateJson,
                            cancellationToken).ConfigureAwait(true);
                    }
                    if (window is IWinFormsEditorWindow winForms)
                    {
                        ApplySnapshotPlacement(winForms.Form, state);
                    }
                }
                catch (Exception exception)
                {
                    _output.Write(
                        "Recovery",
                        $"Could not restore window '{state.Request.DisplayTitle ?? state.Request.Identity}'.",
                        Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                        exception.ToString());
                }
            }
        }

        _pendingRecoveredSessions.Clear();
        if (restorePrivateSessions)
        {
            foreach (PrivateSessionRecoveryState privateState in snapshot.EffectivePrivateSessions
                         .DistinctBy(item => item.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool modalEvent = privateState.WindowRequest.Kind == EditorWindowKind.EventEditor
                    || privateState.WindowRequest.Presentation == WindowPresentation.Modal;
                if (modalEvent)
                {
                    _pendingRecoveredSessions.Add(privateState);
                    continue;
                }

                try
                {
                    IEditorWindow window;
                    if (!_windows.TryGet(privateState.Key, out IEditorWindow? existing) || existing is null)
                    {
                        WindowRequest request = privateState.WindowRequest with
                        {
                            Presentation = privateState.WindowRequest.Presentation == WindowPresentation.Modal
                                ? WindowPresentation.OwnedModeless
                                : privateState.WindowRequest.Presentation
                        };
                        window = await _windows.OpenAsync(request, cancellationToken).ConfigureAwait(true);
                    }
                    else
                    {
                        window = existing;
                    }

                    if (window is IShutdownParticipant participant)
                    {
                        await participant.RestoreRecoveryStateAsync(privateState, cancellationToken).ConfigureAwait(true);
                        restored[window.Key] = window;
                    }
                    else
                    {
                        _pendingRecoveredSessions.Add(privateState);
                    }
                }
                catch (Exception exception)
                {
                    _pendingRecoveredSessions.Add(privateState);
                    _output.Write(
                        "Recovery",
                        $"Could not automatically restore '{privateState.DisplayTitle}'.",
                        Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                        exception.ToString());
                }
            }
        }

        PendingRecoveredSessionsChanged?.Invoke(this, EventArgs.Empty);
        if (_pendingRecoveredSessions.Count > 0)
        {
            IEditorWindow review = await _windows.OpenAsync(new WindowRequest(
                EditorWindowKind.RecoveredSessions,
                "recovered-sessions",
                "Recovered Editing Sessions",
                Scope: WindowScope.Project,
                Presentation: WindowPresentation.OwnedModeless,
                OwnerKey: null,
                PlacementId: "RecoveredSessions"), cancellationToken).ConfigureAwait(true);
            _windows.Activate(review);
        }
        else if (snapshot.ActiveWindow is { } active)
        {
            if (active.Kind == EditorWindowKind.MapEditor)
            {
                host.Form.Activate();
            }
            else if (_windows.TryGet(active.ToWindowKey(), out IEditorWindow? window) && window is not null)
            {
                _windows.Activate(window);
            }
        }
    }

    private async ValueTask<CoordinatedShutdownPlan> PrepareShutdownCoreAsync(
        CancellationToken cancellationToken)
    {
        List<ShutdownContribution> contributions = [];
        foreach (IEditorWindow window in _windows.OpenWindows
                     .Where(window => window.Request.Scope == WindowScope.Project)
                     .Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (window is not IShutdownParticipant participant) continue;
            ShutdownContribution? contribution = await participant
                .PrepareShutdownAsync(cancellationToken)
                .ConfigureAwait(true);
            if (contribution is not null) contributions.Add(contribution);
        }

        foreach (PrivateSessionRecoveryState pending in _pendingRecoveredSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contributions.Add(new ShutdownContribution(
                pending.Key,
                pending.DisplayTitle,
                "This recovered editor session has not been opened or resolved.",
                CanApply: false,
                CanDiscard: true,
                CanRecover: true));
        }
        return new CoordinatedShutdownPlan(contributions);
    }

    private async ValueTask<bool> CommitShutdownCoreAsync(
        CoordinatedShutdownPlan plan,
        ShutdownLocalAction action,
        CancellationToken cancellationToken)
    {
        foreach (ShutdownContribution contribution in plan.Contributions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_windows.TryGet(contribution.WindowKey, out IEditorWindow? window)
                && window is IShutdownParticipant participant)
            {
                if (!await participant.CommitShutdownAsync(action, cancellationToken).ConfigureAwait(true))
                {
                    _windows.Activate(window);
                    return false;
                }
                continue;
            }

            PrivateSessionRecoveryState? pending = _pendingRecoveredSessions.FirstOrDefault(state =>
                state.Key == contribution.WindowKey
                && string.Equals(state.DisplayTitle, contribution.Title, StringComparison.Ordinal));
            if (pending is null) continue;
            switch (action)
            {
                case ShutdownLocalAction.Apply:
                    return false;
                case ShutdownLocalAction.Discard:
                    RemovePending(pending);
                    break;
                case ShutdownLocalAction.PreserveRecovery:
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private async ValueTask<bool> OpenRecoveredSessionCoreAsync(
        PrivateSessionRecoveryState state,
        IWin32Window? owner,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_windows.TryGet(state.Key, out IEditorWindow? existing) && existing is not null)
            {
                if (existing is not IShutdownParticipant existingParticipant) return false;
                await existingParticipant.RestoreRecoveryStateAsync(state, cancellationToken).ConfigureAwait(true);
                _windows.Activate(existing);
                RemovePending(state);
                _workspace.RequestRecoverySnapshot();
                return true;
            }

            bool useModalPresentation = state.WindowRequest.Kind == EditorWindowKind.EventEditor
                || state.WindowRequest.Presentation == WindowPresentation.Modal;
            if (useModalPresentation)
            {
                WindowRequest modalRequest = state.WindowRequest with
                {
                    Presentation = WindowPresentation.Modal,
                    Location = state.WindowRequest.Location
                };
                await _windows.ShowDialogPreparedAsync(
                    modalRequest,
                    async (window, token) =>
                    {
                        if (window is not IShutdownParticipant participant)
                        {
                            throw new InvalidOperationException(
                                $"The recovered window '{state.DisplayTitle}' does not support private-session recovery.");
                        }
                        await participant.RestoreRecoveryStateAsync(state, token).ConfigureAwait(true);
                    },
                    owner,
                    cancellationToken).ConfigureAwait(true);
                RemovePending(state);
                _workspace.RequestRecoverySnapshot();
                return true;
            }

            WindowRequest request = state.WindowRequest with
            {
                Presentation = state.WindowRequest.Presentation == WindowPresentation.Modal
                    ? WindowPresentation.OwnedModeless
                    : state.WindowRequest.Presentation
            };
            IEditorWindow window = await _windows.OpenAsync(request, cancellationToken).ConfigureAwait(true);
            if (window is not IShutdownParticipant participant) return false;
            await participant.RestoreRecoveryStateAsync(state, cancellationToken).ConfigureAwait(true);
            _windows.Activate(window);
            RemovePending(state);
            _workspace.RequestRecoverySnapshot();
            return true;
        }
        catch (Exception exception)
        {
            _output.Write(
                "Recovery",
                $"Could not open recovered session '{state.DisplayTitle}'.",
                Playroma.JRPG.Core.DiagnosticSeverity.Error,
                exception.ToString());
            return false;
        }
    }

    private static void ApplySnapshotPlacement(Form form, WindowWorkspaceState state)
    {
        if (state.NormalBounds is not { } bounds) return;
        Rectangle requested = new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (requested.Width < form.MinimumSize.Width) requested.Width = form.MinimumSize.Width;
        if (requested.Height < form.MinimumSize.Height) requested.Height = form.MinimumSize.Height;

        Screen screen = Screen.AllScreens.FirstOrDefault(item =>
                            string.Equals(item.DeviceName, state.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                        ?? Screen.FromRectangle(requested);
        Rectangle area = screen.WorkingArea;
        int width = Math.Min(Math.Max(form.MinimumSize.Width, requested.Width), area.Width);
        int height = Math.Min(Math.Max(form.MinimumSize.Height, requested.Height), area.Height);
        int x = Math.Clamp(requested.X, area.Left - width + 120, area.Right - 120);
        int y = Math.Clamp(requested.Y, area.Top, area.Bottom - Math.Min(48, height));
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(x, y, width, height);
        if (state.Maximized) form.WindowState = FormWindowState.Maximized;
    }

    private void OnWindowOpened(object? sender, EditorWindowEventArgs e)
    {
        e.Window.StateChanged += OnWindowStateChanged;
        _workspace.RequestRecoverySnapshot();
        ScheduleSuccessfulWorkspaceSave();
    }

    private void OnWindowClosed(object? sender, EditorWindowEventArgs e)
    {
        e.Window.StateChanged -= OnWindowStateChanged;
        _workspace.RequestRecoverySnapshot();
        ScheduleSuccessfulWorkspaceSave();
    }

    private void OnActiveWindowChanged(object? sender, ActiveEditorWindowChangedEventArgs e)
    {
        _workspace.RequestRecoverySnapshot();
        ScheduleSuccessfulWorkspaceSave();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        _workspace.RequestRecoverySnapshot();
        ScheduleSuccessfulWorkspaceSave();
    }

    private void ScheduleSuccessfulWorkspaceSave()
    {
        if (_disposed
            || _controller.State != EditorApplicationState.ProjectOpen
            || _controller.CurrentWorkspaceHost is null
            || _workspace.CurrentSession is null)
        {
            return;
        }

        lock (_autosaveSync)
        {
            _autosaveCancellation?.Cancel();
            _autosaveCancellation?.Dispose();
            _autosaveCancellation = new CancellationTokenSource();
            CancellationToken token = _autosaveCancellation.Token;
            _autosaveTask = PersistSuccessfulWorkspaceAfterDelayAsync(token);
        }
    }

    private async Task PersistSuccessfulWorkspaceAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
            if (_disposed || _controller.State != EditorApplicationState.ProjectOpen) return;
            ProjectWorkspaceSnapshot? snapshot = await CaptureCurrentAsync(
                includePrivateSessions: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                await WriteSuccessfulWorkspaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer workspace change superseded this debounced save.
        }
        catch (Exception exception)
        {
            _output.Write(
                "Workspace",
                "Could not update the automatic successful workspace snapshot.",
                Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                exception.ToString());
        }
    }

    private async ValueTask WriteSuccessfulWorkspaceSnapshotAsync(
        ProjectWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await _workspaceStateWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _workspaceState.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _workspaceStateWriteGate.Release();
        }
    }

    private Task CancelScheduledSuccessfulWorkspaceSave()
    {
        lock (_autosaveSync)
        {
            _autosaveCancellation?.Cancel();
            _autosaveCancellation?.Dispose();
            _autosaveCancellation = null;
            return _autosaveTask;
        }
    }

    private void RemovePending(PrivateSessionRecoveryState state)
    {
        int removed = _pendingRecoveredSessions.RemoveAll(item =>
            item.Key == state.Key && string.Equals(item.SessionType, state.SessionType, StringComparison.Ordinal));
        if (removed > 0) PendingRecoveredSessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private ValueTask RunOnUiThreadAsync(
        Func<ValueTask> operation,
        CancellationToken cancellationToken)
    {
        if (!_dispatcher.InvokeRequired) return operation();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.ThrowIfCancellationRequested();
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await operation().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }));
        return new ValueTask(completion.Task);
    }

    private ValueTask<T> RunOnUiThreadAsync<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!_dispatcher.InvokeRequired) return operation();
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.ThrowIfCancellationRequested();
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                completion.TrySetResult(await operation().ConfigureAwait(true));
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }));
        return new ValueTask<T>(completion.Task);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Forms;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Application;

/// <summary>
/// Serializes project open, close, switching, recovery, and process exit. Forms are views;
/// this controller is the only component that decides when a project session or the
/// application itself ends.
/// </summary>
public sealed class EditorApplicationController : IEditorApplicationController
{
    private readonly ShellServiceContainer _services;
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IRecoveryService _recovery;
    private readonly IProjectFileWatchService _fileWatcher;
    private readonly IWindowService _windows;
    private readonly IBackgroundTaskService _tasks;
    private readonly INotificationService _notifications;
    private readonly IOutputService _output;
    private readonly ICommandService _commands;
    private readonly WinFormsThemeService _theme;
    private readonly WinFormsWindowPlacementService _placement;
    private readonly WinFormsOwnerProvider _ownerProvider;
    private readonly Func<ValueTask> _completeApplicationExit;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _activeLifecycleOperation;
    private ProjectHubForm? _hub;
    private IWinFormsWorkspaceRecoveryCoordinator? _workspaceRecovery;

    public EditorApplicationController(
        ShellServiceContainer services,
        WinFormsThemeService theme,
        WinFormsWindowPlacementService placement,
        WinFormsOwnerProvider ownerProvider,
        Func<ValueTask> completeApplicationExit)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _workspace = services.GetRequired<IWorkspaceService>();
        _settings = services.GetRequired<ISettingsService>();
        _recovery = services.GetRequired<IRecoveryService>();
        _fileWatcher = services.GetRequired<IProjectFileWatchService>();
        _windows = services.GetRequired<IWindowService>();
        _tasks = services.GetRequired<IBackgroundTaskService>();
        _notifications = services.GetRequired<INotificationService>();
        _output = services.GetRequired<IOutputService>();
        _commands = services.GetRequired<ICommandService>();
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        _completeApplicationExit = completeApplicationExit ?? throw new ArgumentNullException(nameof(completeApplicationExit));
    }

    public EditorApplicationState State { get; private set; } = EditorApplicationState.Starting;
    public IProjectWorkspaceHost? CurrentWorkspaceHost { get; private set; }
    public event EventHandler<EditorApplicationStateChangedEventArgs>? StateChanged;

    public void AttachProjectHub(ProjectHubForm hub)
    {
        if (_hub is not null && !ReferenceEquals(_hub, hub))
        {
            throw new InvalidOperationException("A Project Hub is already attached.");
        }
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _ownerProvider.ProjectHub = hub;
    }

    public void MarkHubReady() => SetState(EditorApplicationState.HubVisible);

    public void AttachWorkspaceRecoveryCoordinator(IWinFormsWorkspaceRecoveryCoordinator coordinator)
    {
        if (_workspaceRecovery is not null && !ReferenceEquals(_workspaceRecovery, coordinator))
        {
            throw new InvalidOperationException("A workspace recovery coordinator is already attached.");
        }
        _workspaceRecovery = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async Task<bool> OpenProjectAsync(
        string path,
        ProjectOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State == EditorApplicationState.Exiting) return false;
            if (_workspace.CurrentSession is not null || CurrentWorkspaceHost is not null)
            {
                if (!await CloseProjectCoreAsync(
                    ProjectCloseReason.OpenAnotherProject,
                    showHubAfterClose: true,
                    cancellationToken).ConfigureAwait(true))
                {
                    return false;
                }
            }

            _tasks.ResumeAcceptingNewTasks();
            SetState(EditorApplicationState.OpeningProject);
            _hub?.ShowHub();
            _hub?.SetBusy(true, "Opening project…");

            using ProjectProgressForm progress = new("Opening Project");
            _theme.Apply(progress);
            IWin32Window? owner = _ownerProvider.CurrentOwner;
            if (owner is null) progress.Show(); else progress.Show(owner);
            progress.BringToFront();

            using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                progress.CancellationToken);
            ReplaceActiveOperation(operation);

            try
            {
                var result = await _workspace.OpenAsync(path, progress, operation.Token).ConfigureAwait(true);
                if (result.Value is null)
                {
                    string message = string.Join(
                        Environment.NewLine,
                        result.Diagnostics.Select(item => item.Message));
                    MessageBox.Show(
                        _hub,
                        string.IsNullOrWhiteSpace(message) ? "The project could not be opened." : message,
                        "Could not open project",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    await CleanupFailedOpenAsync().ConfigureAwait(true);
                    return false;
                }

                RecoveryCandidate? recoveryCandidate = options?.RecoveryCandidate;
                if (recoveryCandidate is not null)
                {
                    // Restore the in-memory session before constructing project controls so
                    // every first render observes the recovered state.
                    await _recovery.RestoreAsync(
                        result.Value,
                        recoveryCandidate,
                        operation.Token).ConfigureAwait(true);
                }

                MapEditorForm workspaceForm = new(
                    _services,
                    this,
                    _theme,
                    _placement);
                CurrentWorkspaceHost = workspaceForm;
                _ownerProvider.ProjectWorkspace = workspaceForm.Form;
                workspaceForm.ApplyLoadedSettings();
                workspaceForm.Form.Show();
                workspaceForm.Form.BringToFront();
                await workspaceForm.InitializeProjectAsync(operation.Token).ConfigureAwait(true);

                string projectRoot = result.Value.Workspace.Snapshot.Identity.ProjectRoot;
                if (recoveryCandidate is not null)
                {
                    ProjectWorkspaceSnapshot? recoveredWorkspace = _workspaceRecovery is null
                        ? null
                        : await _recovery.ReadWorkspaceAsync(
                            recoveryCandidate,
                            operation.Token).ConfigureAwait(true);
                    if (recoveredWorkspace is not null && _workspaceRecovery is not null)
                    {
                        bool restoreWindows = options!.RestoreDocuments
                            && _settings.Current.RestoreDocuments
                            && _settings.Current.EffectivePreferences.RestoreProjectWindows;
                        await _workspaceRecovery.RestoreWorkspaceAsync(
                            workspaceForm,
                            recoveredWorkspace,
                            restoreWindows,
                            restorePrivateSessions: true,
                            cancellationToken: operation.Token).ConfigureAwait(true);
                    }
                    else if (options!.RestoreDocuments && _settings.Current.RestoreDocuments)
                    {
                        // Read-only compatibility for recovery manifests written before Phase 10.
                        if (_workspaceRecovery is not null)
                        {
                            await _workspaceRecovery.RestoreLegacyDocumentRequestsAsync(
                                workspaceForm,
                                recoveryCandidate.Manifest.LegacyOpenDocuments,
                                operation.Token).ConfigureAwait(true);
                        }
                    }

                    _notifications.Warning(
                        "Recovered workspace",
                        "Recovered project changes and editor sessions are open in memory. Review and save them when ready.");
                }
                else if (_workspaceRecovery is not null
                         && _settings.Current.EffectivePreferences.RestoreProjectWindows)
                {
                    ProjectWorkspaceSnapshot? successfulWorkspace = await _workspaceRecovery
                        .ReadSuccessfulWorkspaceAsync(projectRoot, operation.Token)
                        .ConfigureAwait(true);
                    if (successfulWorkspace is not null)
                    {
                        await _workspaceRecovery.RestoreWorkspaceAsync(
                            workspaceForm,
                            successfulWorkspace,
                            restoreWindows: true,
                            restorePrivateSessions: false,
                            cancellationToken: operation.Token).ConfigureAwait(true);
                    }
                }

                _fileWatcher.Start(projectRoot);
                _settings.Update(settings => settings with
                {
                    LastProjectRoot = projectRoot,
                    LastOpenDirectory = projectRoot,
                    Version = EditorSettingsMigrator.CurrentVersion
                });
                _hub?.Hide();
                SetState(EditorApplicationState.ProjectOpen);
                workspaceForm.Form.Activate();
                return true;
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                _output.Write("Project", "Project opening cancelled.");
                await CleanupFailedOpenAsync().ConfigureAwait(true);
                return false;
            }
            catch (Exception exception)
            {
                _output.Write(
                    "Project",
                    "Could not open project.",
                    Playroma.JRPG.Core.DiagnosticSeverity.Error,
                    exception.ToString());
                MessageBox.Show(
                    _hub,
                    exception.Message,
                    "Could not open project",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                await CleanupFailedOpenAsync().ConfigureAwait(true);
                return false;
            }
            finally
            {
                if (!progress.IsDisposed) progress.Close();
                ClearActiveOperation(operation);
                _hub?.SetBusy(false);
                _commands.Invalidate();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> CloseProjectAsync(
        ProjectCloseReason reason,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            return await CloseProjectCoreAsync(
                reason,
                showHubAfterClose: reason != ProjectCloseReason.ApplicationExit,
                cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> ExitApplicationAsync(CancellationToken cancellationToken = default)
    {
        // Closing the Hub while a project is still opening must cancel that operation instead
        // of waiting indefinitely behind it. The opening operation performs its own cleanup
        // before this exit request acquires the lifecycle gate.
        if (State == EditorApplicationState.OpeningProject)
        {
            _activeLifecycleOperation?.Cancel();
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State == EditorApplicationState.Exiting) return true;
            if (!await CloseProjectCoreAsync(
                ProjectCloseReason.ApplicationExit,
                showHubAfterClose: false,
                cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            if (!await _windows.CanCloseScopeAsync(WindowScope.Application, cancellationToken).ConfigureAwait(true))
            {
                _tasks.ResumeAcceptingNewTasks();
                SetState(EditorApplicationState.HubVisible);
                _hub?.ShowHub();
                return false;
            }

            SetState(EditorApplicationState.Exiting);
            _activeLifecycleOperation?.Cancel();
            await _windows.CloseScopeForcedAsync(WindowScope.Application, cancellationToken).ConfigureAwait(true);
            _hub?.CloseFromController();
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
            await _completeApplicationExit().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            SetState(EditorApplicationState.Faulted);
            _notifications.Error("Could not exit Playroma", exception.Message, exception);
            MessageBox.Show(
                _ownerProvider.CurrentOwner,
                exception.Message,
                "Could not exit Playroma",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<bool> CloseProjectCoreAsync(
        ProjectCloseReason reason,
        bool showHubAfterClose,
        CancellationToken cancellationToken)
    {
        if (_workspace.CurrentSession is null && CurrentWorkspaceHost is null)
        {
            if (showHubAfterClose)
            {
                await ShowHubAsync(cancellationToken).ConfigureAwait(true);
                SetState(EditorApplicationState.HubVisible);
            }
            return true;
        }

        EditorApplicationState previousState = State;
        SetState(EditorApplicationState.ClosingProject);
        _activeLifecycleOperation?.Cancel();
        bool tasksStopped = false;
        try
        {
            IProjectWorkspaceHost? workspaceForm = CurrentWorkspaceHost;
            if (workspaceForm is not null
                && !await workspaceForm.CanCloseProjectAsync(cancellationToken).ConfigureAwait(true))
            {
                RestoreOpenState(previousState);
                return false;
            }

            if (!await CanCloseNonParticipantProjectWindowsAsync(cancellationToken).ConfigureAwait(true))
            {
                RestoreOpenState(previousState);
                return false;
            }

            Playroma.JRPG.Application.MvProjectSession? session = _workspace.CurrentSession;
            CoordinatedShutdownPlan plan = _workspaceRecovery is null
                ? new CoordinatedShutdownPlan([])
                : await _workspaceRecovery.PrepareShutdownAsync(cancellationToken).ConfigureAwait(true);
            bool hasSharedProjectChanges = session?.State.HasUnsavedChanges == true;
            ShutdownLocalAction localAction = ShutdownLocalAction.Apply;
            ShutdownProjectAction projectAction = ShutdownProjectAction.Save;

            if (plan.HasLocalChanges || hasSharedProjectChanges)
            {
                string title = session?.Workspace.Snapshot.Identity.ProjectTitle
                    ?? (session is null
                        ? "Playroma project"
                        : Path.GetFileName(session.Workspace.Snapshot.Identity.ProjectRoot));
                using CoordinatedShutdownDialog dialog = new(
                    title,
                    plan,
                    hasSharedProjectChanges,
                    exitingApplication: reason == ProjectCloseReason.ApplicationExit);
                _theme.Apply(dialog);
                IWin32Window? owner = _ownerProvider.CurrentOwner;
                DialogResult result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result != DialogResult.OK)
                {
                    RestoreOpenState(previousState);
                    return false;
                }
                localAction = dialog.LocalAction;
                projectAction = dialog.ProjectAction;
            }

            _tasks.StopAcceptingNewTasks();
            tasksStopped = true;
            _tasks.CancelAll(task => task.Definition.ShutdownBehavior != TaskShutdownBehavior.FinishBeforeShutdown);
            bool idle = await _tasks.AwaitIdleAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(true);
            if (!idle)
            {
                _output.Write(
                    "Tasks",
                    "Project shutdown was paused because one or more background operations did not finish safely.",
                    Playroma.JRPG.Core.DiagnosticSeverity.Warning);
                _notifications.Warning(
                    "Project is still busy",
                    "One or more background operations did not finish within 15 seconds. The project remains open so its services are not disposed while those operations are still running.");
                RestoreOpenState(previousState);
                return false;
            }

            if (_workspaceRecovery is not null && plan.HasLocalChanges)
            {
                if (!await _workspaceRecovery.CommitShutdownAsync(
                        plan,
                        localAction,
                        cancellationToken).ConfigureAwait(true))
                {
                    RestoreOpenState(previousState);
                    return false;
                }
            }

            session = _workspace.CurrentSession;
            if (session is not null)
            {
                switch (projectAction)
                {
                    case ShutdownProjectAction.Save when session.State.HasUnsavedChanges:
                    {
                        _fileWatcher.Suppress(TimeSpan.FromSeconds(3));
                        ProjectSaveResult save = await _workspace
                            .SaveAsync(cancellationToken: cancellationToken)
                            .ConfigureAwait(true);
                        if (!save.Succeeded)
                        {
                            RestoreOpenState(previousState);
                            return false;
                        }
                        break;
                    }
                    case ShutdownProjectAction.Discard when session.State.HasUnsavedChanges:
                        await _workspace.DiscardAllChangesAsync(cancellationToken).ConfigureAwait(true);
                        break;
                }

                bool preserveRecovery = localAction == ShutdownLocalAction.PreserveRecovery
                    || projectAction == ShutdownProjectAction.PreserveRecovery;
                if (preserveRecovery)
                {
                    ProjectWorkspaceSnapshot? snapshot = _workspaceRecovery is null
                        ? null
                        : await _workspaceRecovery.CaptureCurrentAsync(
                            includePrivateSessions: true,
                            cancellationToken: cancellationToken).ConfigureAwait(true);
                    RecoveryCandidate? preserved = await _recovery.WriteAsync(
                        session,
                        workspaceSnapshot: snapshot,
                        cancellationToken: cancellationToken).ConfigureAwait(true);
                    if (preserved is null)
                    {
                        _notifications.Error(
                            "Could not preserve recovery",
                            "Playroma did not create a recovery snapshot, so the project will remain open.");
                        RestoreOpenState(previousState);
                        return false;
                    }
                }
                else
                {
                    await _recovery.DeleteForProjectAsync(
                        session.Workspace.Snapshot.Identity.ProjectRoot,
                        cancellationToken).ConfigureAwait(true);
                }
            }

            if (_workspaceRecovery is not null && workspaceForm is not null)
            {
                try
                {
                    await _workspaceRecovery.SaveSuccessfulWorkspaceAsync(
                        workspaceForm,
                        cancellationToken: cancellationToken).ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    _output.Write(
                        "Workspace",
                        "The project was saved, but Playroma could not persist the successful workspace layout.",
                        Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                        exception.ToString());
                }
            }

            _fileWatcher.Stop();
            await _windows.CloseScopeForcedAsync(WindowScope.Project, cancellationToken).ConfigureAwait(true);
            if (workspaceForm is not null)
            {
                await workspaceForm.CloseProjectContentForcedAsync(cancellationToken).ConfigureAwait(true);
                workspaceForm.CloseFromController();
            }
            CurrentWorkspaceHost = null;
            _ownerProvider.ProjectWorkspace = null;
            await _workspace.CloseAsync(discardUnsaved: true, cancellationToken: cancellationToken).ConfigureAwait(true);
            _services.GetRequired<INavigationService>().Clear();
            _services.GetRequired<ISelectionService>().Select(new EmptySelection());

            if (showHubAfterClose)
            {
                _tasks.ResumeAcceptingNewTasks();
                tasksStopped = false;
                await ShowHubAsync(cancellationToken).ConfigureAwait(true);
                SetState(EditorApplicationState.HubVisible);
            }
            _commands.Invalidate();
            if (!showHubAfterClose) tasksStopped = false;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOpenState(previousState);
            return false;
        }
        catch (Exception exception)
        {
            _output.Write(
                "Project",
                "Could not close the project cleanly.",
                Playroma.JRPG.Core.DiagnosticSeverity.Error,
                exception.ToString());
            _notifications.Error("Could not close project", exception.Message, exception);
            RestoreOpenState(previousState);
            return false;
        }
        finally
        {
            if (tasksStopped && State != EditorApplicationState.Exiting)
            {
                try { _tasks.ResumeAcceptingNewTasks(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task<bool> CanCloseNonParticipantProjectWindowsAsync(
        CancellationToken cancellationToken)
    {
        foreach (IEditorWindow window in _windows.OpenWindows
                     .Where(window => window.Request.Scope == WindowScope.Project)
                     .Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (window is IShutdownParticipant) continue;
            if (!await window.CanCloseAsync(cancellationToken).ConfigureAwait(true))
            {
                _windows.Activate(window);
                return false;
            }
        }
        return true;
    }

    private void RestoreOpenState(EditorApplicationState previousState)
    {
        bool projectStillOpen = _workspace.CurrentSession is not null || CurrentWorkspaceHost is not null;
        if (!projectStillOpen)
        {
            SetState(EditorApplicationState.HubVisible);
            _hub?.ShowHub();
            return;
        }

        SetState(previousState == EditorApplicationState.OpeningProject
            ? EditorApplicationState.HubVisible
            : EditorApplicationState.ProjectOpen);
    }

    private async Task CleanupFailedOpenAsync()
    {
        _fileWatcher.Stop();
        try
        {
            await _windows.CloseScopeForcedAsync(WindowScope.Project).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _output.Write(
                "Windows",
                "Could not close project windows after a failed open.",
                Playroma.JRPG.Core.DiagnosticSeverity.Warning,
                exception.ToString());
        }

        if (CurrentWorkspaceHost is { } workspaceForm)
        {
            try
            {
                await workspaceForm.CloseProjectContentForcedAsync().ConfigureAwait(true);
            }
            catch
            {
                // Continue cleanup; the project session must not remain half-open.
            }
            workspaceForm.CloseFromController();
        }
        CurrentWorkspaceHost = null;
        _ownerProvider.ProjectWorkspace = null;
        if (_workspace.CurrentSession is not null)
        {
            await _workspace.CloseAsync(discardUnsaved: true).ConfigureAwait(true);
        }
        _services.GetRequired<INavigationService>().Clear();
        _services.GetRequired<ISelectionService>().Select(new EmptySelection());
        _tasks.ResumeAcceptingNewTasks();
        await ShowHubAsync(CancellationToken.None).ConfigureAwait(true);
        SetState(EditorApplicationState.HubVisible);
    }

    private async Task ShowHubAsync(CancellationToken cancellationToken)
    {
        if (_hub is null) return;
        _hub.ShowHub();
        await _hub.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private void ReplaceActiveOperation(CancellationTokenSource operation)
    {
        CancellationTokenSource? previous = Interlocked.Exchange(ref _activeLifecycleOperation, operation);
        if (previous is null || ReferenceEquals(previous, operation)) return;
        previous.Cancel();
        previous.Dispose();
    }

    private void ClearActiveOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _activeLifecycleOperation, null, operation), operation))
        {
            return;
        }
    }

    private void SetState(EditorApplicationState state)
    {
        if (State == state) return;
        EditorApplicationState previous = State;
        State = state;
        StateChanged?.Invoke(this, new EditorApplicationStateChangedEventArgs(previous, state));
        _commands.Invalidate();
    }
}

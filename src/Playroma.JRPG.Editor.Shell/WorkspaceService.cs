using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;

namespace Playroma.JRPG.Editor.Shell;

public interface IWorkspaceService : IAsyncDisposable
{
    MvProjectSession? CurrentSession { get; }
    ProjectSessionState? State { get; }
    ProjectValidationResult? LastValidation { get; }
    event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;
    event EventHandler<ValidationChangedEventArgs>? ValidationChanged;
    ValueTask<OperationResult<MvProjectSession>> OpenAsync(
        string path,
        IProgress<ProjectOpenProgress>? progress = null,
        CancellationToken cancellationToken = default);
    ValueTask<ProjectSaveResult> SaveAsync(ProjectSaveOptions? options = null, CancellationToken cancellationToken = default);
    ValueTask<ProjectValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
    ValueTask<int> RefreshReferencesAsync(CancellationToken cancellationToken = default);
    ValueTask DiscardAllChangesAsync(CancellationToken cancellationToken = default);
    void RequestRecoverySnapshot();
    ValueTask CloseAsync(bool discardUnsaved, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IBackgroundTaskService _tasks;
    private readonly IRecentProjectService _recent;
    private readonly INotificationService _notifications;
    private readonly IOutputService _output;
    private readonly IRecoveryService _recovery;
    private readonly WorkspaceRecoveryContextBroker _recoveryContext;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _projectCancellation;
    private CancellationTokenSource? _recoveryDebounce;
    private MvProjectSession? _session;
    private bool _disposed;
    private SynchronizationContext? _eventContext;
    private WorkspaceStateChangedEventArgs? _pendingStateEvent;
    private ValidationChangedEventArgs? _pendingValidationEvent;
    private int _stateEventQueued;
    private int _validationEventQueued;

    public WorkspaceService(
        IBackgroundTaskService tasks,
        IRecentProjectService recent,
        INotificationService notifications,
        IOutputService output,
        IRecoveryService recovery,
        WorkspaceRecoveryContextBroker recoveryContext)
    {
        _tasks = tasks;
        _recent = recent;
        _notifications = notifications;
        _output = output;
        _recovery = recovery;
        _recoveryContext = recoveryContext ?? throw new ArgumentNullException(nameof(recoveryContext));
    }

    public MvProjectSession? CurrentSession => _session;
    public ProjectSessionState? State => _session?.State;
    public ProjectValidationResult? LastValidation => _session?.LastValidation;
    public event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;
    public event EventHandler<ValidationChangedEventArgs>? ValidationChanged;

    public async ValueTask<OperationResult<MvProjectSession>> OpenAsync(
        string path,
        IProgress<ProjectOpenProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null) await CloseCoreAsync(discardUnsaved: false, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProjectOpenProgress(ProjectOpenStage.Locating, 0, 1, path, "Locating RPG Maker MV project"));
            _projectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            OperationResult<MvProjectSession> opened = await _tasks.RunAsync(
                new BackgroundTaskDefinition("Open project", "Project"),
                async (taskProgress, token) =>
                {
                    taskProgress.Report(new TaskProgress(0, 1, path, "Loading project inventory"));
                    OperationResult<MvProjectSession> result = await MvProjectSession.OpenAsync(
                        path,
                        cancellationToken: token,
                        analysisLoadMode: ProjectAnalysisLoadMode.Deferred).ConfigureAwait(false);
                    taskProgress.Report(new TaskProgress(1, 1, path, result.Value is null ? "Project load failed" : "Project loaded"));
                    return result;
                },
                _projectCancellation.Token).ConfigureAwait(false);
            if (opened.Value is null) return opened;
            _session = opened.Value;
            Subscribe(_session);
            MvProjectSnapshot snapshot = _session.Workspace.Snapshot;
            await _recent.RecordAsync(new RecentProject(
                snapshot.Identity.ProjectRoot,
                snapshot.Identity.ProjectTitle,
                DateTimeOffset.UtcNow,
                snapshot.Layout.Kind,
                true), cancellationToken).ConfigureAwait(false);
            _output.Write("Project", $"Opened {snapshot.Identity.ProjectRoot}");
            PublishState();
            progress?.Report(new ProjectOpenProgress(ProjectOpenStage.Ready, 1, 1, snapshot.Identity.ProjectRoot, "Workspace ready"));
            _ = RunDeferredAnalysisAsync(_session, _projectCancellation.Token);
            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ProjectSaveResult> SaveAsync(ProjectSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        MvProjectSession session = RequireSession();
        CancelRecoveryDebounce();
        ProjectSaveResult result = await _tasks.RunAsync(
            new BackgroundTaskDefinition(
                "Save project",
                "Project",
                CanCancel: false,
                ShutdownBehavior: TaskShutdownBehavior.FinishBeforeShutdown),
            async (progress, token) =>
            {
                progress.Report(new TaskProgress(0, 1, null, "Preparing verified save"));
                ProjectSaveResult save = await session.SaveAsync(options, token).ConfigureAwait(false);
                progress.Report(new TaskProgress(1, 1, null, save.Succeeded ? "Save complete" : "Save blocked"));
                return save;
            }, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await _recovery.DeleteForProjectAsync(session.Workspace.Snapshot.Identity.ProjectRoot, cancellationToken).ConfigureAwait(false);
            _notifications.Success("Project saved", $"Saved {result.Documents.Count(item => item.Status == DocumentSaveStatus.Saved)} modified files.");
        }
        else
        {
            _notifications.Warning("Save not completed", string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message).Take(6)));
        }
        foreach (DocumentSaveResult document in result.Documents) _output.Write("Save", $"{document.Status}: {document.RelativePath}");
        PublishState();
        return result;
    }

    public async ValueTask<ProjectValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        MvProjectSession session = RequireSession();
        ProjectValidationResult result = await _tasks.RunAsync(
            new BackgroundTaskDefinition("Validate project", "Validation"),
            async (progress, token) => await session.ValidateAsync(
                new Progress<ValidationProgress>(value => progress.Report(new TaskProgress(
                    value.CompletedValidators,
                    value.TotalValidators,
                    value.CurrentValidator,
                    $"{value.DiagnosticCount} diagnostics"))),
                token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        PublishValidation(result);
        _output.Write("Validation", $"Completed with {result.ErrorCount} errors and {result.WarningCount} warnings.");
        return result;
    }

    public async ValueTask<int> RefreshReferencesAsync(CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        MvProjectSession session = RequireSession();
        int count = await _tasks.RunAsync(
            new BackgroundTaskDefinition("Refresh references", "Indexing"),
            async (progress, token) =>
            {
                progress.Report(new TaskProgress(0, 1, null, "Refreshing changed documents"));
                int refreshed = await session.RefreshReferencesAsync(token).ConfigureAwait(false);
                progress.Report(new TaskProgress(1, 1, null, $"Refreshed {refreshed} documents"));
                return refreshed;
            }, cancellationToken).ConfigureAwait(false);
        PublishState();
        return count;
    }

    public async ValueTask DiscardAllChangesAsync(CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        MvProjectSession session = RequireSession();
        await session.DiscardAllChangesAsync(cancellationToken).ConfigureAwait(false);
        await _recovery.DeleteForProjectAsync(session.Workspace.Snapshot.Identity.ProjectRoot, cancellationToken).ConfigureAwait(false);
        PublishValidation(null);
        PublishState();
    }

    public void RequestRecoverySnapshot()
    {
        if (_session is { } session) ScheduleRecovery(session);
    }

    public async ValueTask CloseAsync(bool discardUnsaved, CancellationToken cancellationToken = default)
    {
        CaptureEventContext();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CloseCoreAsync(discardUnsaved, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await CloseAsync(discardUnsaved: true).ConfigureAwait(false);
        _gate.Dispose();
        _disposed = true;
    }

    private async Task RunDeferredAnalysisAsync(MvProjectSession session, CancellationToken cancellationToken)
    {
        try
        {
            // Let the open-project continuation install the summary document and finish its
            // first layout before CPU-heavy indexing begins.
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            await _tasks.RunAsync(
                new BackgroundTaskDefinition("Build reference index", "Indexing"),
                async (progress, token) =>
                {
                    await session.InitializeReferencesAsync(
                        new Progress<ReferenceIndexProgress>(value => progress.Report(new TaskProgress(
                            value.CompletedDocuments,
                            value.TotalDocuments,
                            value.CurrentDocument,
                            $"{value.ReferenceCount} references"))), token).ConfigureAwait(false);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
            PublishState();
            _output.Write("Project", "Reference index ready. Run Validate Project (F8) when you want a full validation pass.");
        }
        catch (OperationCanceledException)
        {
            _output.Write("Project", "Background project analysis cancelled.");
        }
        catch (Exception exception)
        {
            _output.Write("Project", "Background project analysis failed.", DiagnosticSeverity.Error, exception.ToString());
            _notifications.Error("Project analysis failed", exception.Message, exception);
        }
    }

    private async ValueTask CloseCoreAsync(bool discardUnsaved, CancellationToken cancellationToken)
    {
        if (_session is null) return;
        if (_session.State.HasUnsavedChanges && !discardUnsaved)
        {
            throw new InvalidOperationException("The project contains unsaved changes.");
        }
        CancelRecoveryDebounce();
        _projectCancellation?.Cancel();
        _projectCancellation?.Dispose();
        _projectCancellation = null;
        MvProjectSession closing = _session;
        _session = null;
        Unsubscribe(closing);
        await closing.DisposeAsync().ConfigureAwait(false);
        PublishValidation(null);
        PublishState();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Subscribe(MvProjectSession session)
    {
        session.StateChanged += OnSessionStateChanged;
        session.ValidationCompleted += OnValidationCompleted;
        session.DocumentChanged += OnDocumentChanged;
    }

    private void Unsubscribe(MvProjectSession session)
    {
        session.StateChanged -= OnSessionStateChanged;
        session.ValidationCompleted -= OnValidationCompleted;
        session.DocumentChanged -= OnDocumentChanged;
    }

    private void OnDocumentChanged(object? sender, Playroma.JRPG.Editing.EditorCommandEvent e)
    {
        if (sender is MvProjectSession session) ScheduleRecovery(session);
    }

    private void ScheduleRecovery(MvProjectSession session)
    {
        CancellationToken projectToken = _projectCancellation?.Token ?? CancellationToken.None;
        CancellationTokenSource next = CancellationTokenSource.CreateLinkedTokenSource(projectToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _recoveryDebounce, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = WriteRecoveryAfterDelayAsync(session, next);
    }

    private async Task WriteRecoveryAfterDelayAsync(MvProjectSession session, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), owner.Token).ConfigureAwait(false);
            if (!ReferenceEquals(_session, session)) return;
            ProjectWorkspaceSnapshot? workspaceSnapshot = await _recoveryContext
                .CaptureAsync(owner.Token)
                .ConfigureAwait(false);
            bool hasPrivateSessions = workspaceSnapshot?.EffectivePrivateSessions.Count > 0;
            if (session.State.HasUnsavedChanges || hasPrivateSessions)
            {
                RecoveryCandidate? candidate = await _recovery.WriteAsync(
                    session,
                    workspaceSnapshot: workspaceSnapshot,
                    cancellationToken: owner.Token).ConfigureAwait(false);
                if (candidate is not null) _output.Write("Recovery", "Updated project and multi-window recovery snapshot.");
            }
            else
            {
                await _recovery.DeleteForProjectAsync(session.Workspace.Snapshot.Identity.ProjectRoot, owner.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // A newer edit, save, or project close superseded this recovery write.
        }
        catch (Exception exception)
        {
            _output.Write("Recovery", "Could not update the recovery snapshot.", DiagnosticSeverity.Warning, exception.ToString());
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _recoveryDebounce, null, owner), owner)) owner.Dispose();
        }
    }

    private void CancelRecoveryDebounce()
    {
        CancellationTokenSource? pending = Interlocked.Exchange(ref _recoveryDebounce, null);
        if (pending is null) return;
        pending.Cancel();
        pending.Dispose();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) => PublishState();
    private void OnValidationCompleted(object? sender, ProjectValidationResult e) => PublishValidation(e);

    private void CaptureEventContext()
    {
        SynchronizationContext? current = SynchronizationContext.Current;
        if (current is not null) Interlocked.CompareExchange(ref _eventContext, current, null);
    }

    private void DispatchEvent(Action callback)
    {
        SynchronizationContext? context = Volatile.Read(ref _eventContext);
        if (context is null)
        {
            callback();
            return;
        }

        // Always post instead of invoking synchronously. This prevents re-entrant WinForms
        // layout and control rebuilding when a session raises several state changes while
        // an open/index/save operation is completing.
        context.Post(static state => ((Action)state!).Invoke(), callback);
    }

    private void PublishValidation(ProjectValidationResult? result)
    {
        Interlocked.Exchange(ref _pendingValidationEvent, new ValidationChangedEventArgs(result));
        QueueValidationEvent();
    }

    private void QueueValidationEvent()
    {
        if (Interlocked.CompareExchange(ref _validationEventQueued, 1, 0) != 0) return;
        DispatchEvent(DrainValidationEvent);
    }

    private void DrainValidationEvent()
    {
        ValidationChangedEventArgs? args = Interlocked.Exchange(ref _pendingValidationEvent, null);
        try
        {
            if (args is not null) ValidationChanged?.Invoke(this, args);
        }
        finally
        {
            Volatile.Write(ref _validationEventQueued, 0);
            if (Volatile.Read(ref _pendingValidationEvent) is not null) QueueValidationEvent();
        }
    }

    private void PublishState()
    {
        Interlocked.Exchange(ref _pendingStateEvent, new WorkspaceStateChangedEventArgs(_session, _session?.State));
        QueueStateEvent();
    }

    private void QueueStateEvent()
    {
        if (Interlocked.CompareExchange(ref _stateEventQueued, 1, 0) != 0) return;
        DispatchEvent(DrainStateEvent);
    }

    private void DrainStateEvent()
    {
        WorkspaceStateChangedEventArgs? args = Interlocked.Exchange(ref _pendingStateEvent, null);
        try
        {
            if (args is not null) StateChanged?.Invoke(this, args);
        }
        finally
        {
            Volatile.Write(ref _stateEventQueued, 0);
            if (Volatile.Read(ref _pendingStateEvent) is not null) QueueStateEvent();
        }
    }
    private MvProjectSession RequireSession() => _session ?? throw new InvalidOperationException("No project is open.");
}

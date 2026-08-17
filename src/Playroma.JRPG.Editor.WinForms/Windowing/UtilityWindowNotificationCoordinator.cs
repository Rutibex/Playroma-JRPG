using Playroma.JRPG.Core;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Validation;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

/// <summary>
/// Applies the opt-in utility-window preferences without coupling background services to WinForms.
/// It opens Output after a newly failed task and Validation after a newly completed validation run
/// containing errors. Existing failures/results are remembered so ordinary progress notifications do
/// not repeatedly steal focus.
/// </summary>
public sealed class UtilityWindowNotificationCoordinator : IDisposable
{
    private readonly IWorkspaceService _workspace;
    private readonly IBackgroundTaskService _tasks;
    private readonly IWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly HashSet<Guid> _knownFailedTasks = [];
    private ProjectValidationResult? _lastHandledValidation;
    private bool _disposed;
    private int _openingOutput;
    private int _openingValidation;

    public UtilityWindowNotificationCoordinator(
        IWorkspaceService workspace,
        IBackgroundTaskService tasks,
        IWindowService windows,
        ISettingsService settings,
        INotificationService notifications)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        foreach (BackgroundTaskInfo task in _tasks.Tasks.Where(task => task.State == BackgroundTaskState.Failed))
            _knownFailedTasks.Add(task.Id);

        _tasks.TasksChanged += OnTasksChanged;
        _workspace.ValidationChanged += OnValidationChanged;
        _workspace.StateChanged += OnWorkspaceStateChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tasks.TasksChanged -= OnTasksChanged;
        _workspace.ValidationChanged -= OnValidationChanged;
        _workspace.StateChanged -= OnWorkspaceStateChanged;
        _knownFailedTasks.Clear();
    }

    private void OnTasksChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        BackgroundTaskInfo[] failed = _tasks.Tasks
            .Where(task => task.State == BackgroundTaskState.Failed)
            .ToArray();
        BackgroundTaskInfo[] newlyFailed = failed
            .Where(task => _knownFailedTasks.Add(task.Id))
            .ToArray();

        HashSet<Guid> retained = failed.Select(task => task.Id).ToHashSet();
        _knownFailedTasks.RemoveWhere(id => !retained.Contains(id));

        if (newlyFailed.Length == 0
            || !_settings.Current.EffectivePreferences.OpenOutputOnTaskFailure
            || _workspace.CurrentSession is null)
        {
            return;
        }

        _ = OpenOutputAsync(newlyFailed[^1]);
    }

    private void OnValidationChanged(object? sender, ValidationChangedEventArgs e)
    {
        if (_disposed || e.Result is null || ReferenceEquals(_lastHandledValidation, e.Result)) return;
        _lastHandledValidation = e.Result;
        if (e.Result.ErrorCount <= 0
            || !_settings.Current.EffectivePreferences.OpenValidationWhenErrorsFound
            || _workspace.CurrentSession is null)
        {
            return;
        }

        _ = OpenValidationAsync(e.Result);
    }

    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (e.Session is null)
        {
            _lastHandledValidation = null;
            _knownFailedTasks.Clear();
        }
    }

    private async Task OpenOutputAsync(BackgroundTaskInfo task)
    {
        if (Interlocked.CompareExchange(ref _openingOutput, 1, 0) != 0) return;
        try
        {
            await _windows.OpenAsync(new WindowRequest(
                EditorWindowKind.Output,
                "output",
                "Output",
                Scope: WindowScope.Project,
                PlacementId: "Output")).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Warning(
                "Output window could not open automatically",
                $"Background task '{task.Definition.Name}' failed, but the Output window could not be opened: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _openingOutput, 0);
        }
    }

    private async Task OpenValidationAsync(ProjectValidationResult result)
    {
        if (Interlocked.CompareExchange(ref _openingValidation, 1, 0) != 0) return;
        try
        {
            await _windows.OpenAsync(new WindowRequest(
                EditorWindowKind.Validation,
                "validation",
                "Validation",
                Scope: WindowScope.Project,
                PlacementId: "Validation",
                Document: new DocumentRequest(
                    EditorDocumentKind.Validation,
                    "validation",
                    DisplayTitle: "Validation"))).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Warning(
                "Validation window could not open automatically",
                $"Validation found {result.ErrorCount:N0} error(s), but the Validation window could not be opened: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _openingValidation, 0);
        }
    }
}

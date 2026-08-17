using System.Collections.Concurrent;

namespace Playroma.JRPG.Editor.Shell;

public enum BackgroundTaskState
{
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed
}

public enum TaskShutdownBehavior
{
    CancelImmediately,
    CancelAndAwait,
    FinishBeforeShutdown
}

public sealed record TaskProgress(int Completed, int Total, string? CurrentItem, string? Message)
{
    public double? Percentage => Total > 0 ? Math.Clamp((double)Completed / Total * 100.0, 0, 100) : null;
}

public sealed record BackgroundTaskDefinition(
    string Name,
    string Category,
    bool CanCancel = true,
    TaskShutdownBehavior ShutdownBehavior = TaskShutdownBehavior.CancelAndAwait);

public sealed class BackgroundTaskInfo
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<object?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal BackgroundTaskInfo(Guid id, BackgroundTaskDefinition definition)
    {
        Id = id;
        Definition = definition;
        State = BackgroundTaskState.Queued;
    }

    public Guid Id { get; }
    public BackgroundTaskDefinition Definition { get; }
    public BackgroundTaskState State { get; internal set; }
    public TaskProgress? Progress { get; internal set; }
    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedUtc { get; internal set; }
    public DateTimeOffset? CompletedUtc { get; internal set; }
    public Exception? Error { get; internal set; }
    public TimeSpan Elapsed => (CompletedUtc ?? DateTimeOffset.UtcNow) - (StartedUtc ?? CreatedUtc);
    internal CancellationToken Token => _cancellation.Token;
    internal Task Completion => _completion.Task;

    public void Cancel()
    {
        if (Definition.CanCancel && State is BackgroundTaskState.Queued or BackgroundTaskState.Running) _cancellation.Cancel();
    }

    internal void Complete() => _completion.TrySetResult(null);
}

public interface IBackgroundTaskService
{
    IReadOnlyList<BackgroundTaskInfo> Tasks { get; }
    bool AcceptingNewTasks { get; }
    event EventHandler? TasksChanged;
    ValueTask<T> RunAsync<T>(
        BackgroundTaskDefinition definition,
        Func<IProgress<TaskProgress>, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);
    void Cancel(Guid taskId);
    void CancelAll(Func<BackgroundTaskInfo, bool>? predicate = null);
    void StopAcceptingNewTasks();
    void ResumeAcceptingNewTasks();
    ValueTask<bool> AwaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    void ClearCompleted();
    void ClearFinished(bool includeFailed);
}

public sealed class BackgroundTaskService : IBackgroundTaskService, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, BackgroundTaskInfo> _tasks = [];
    private volatile bool _acceptingNewTasks = true;
    private bool _disposed;

    public event EventHandler? TasksChanged;
    public IReadOnlyList<BackgroundTaskInfo> Tasks => _tasks.Values.OrderByDescending(task => task.CreatedUtc).ToArray();
    public bool AcceptingNewTasks => _acceptingNewTasks && !_disposed;

    public async ValueTask<T> RunAsync<T>(
        BackgroundTaskDefinition definition,
        Func<IProgress<TaskProgress>, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(operation);
        if (!_acceptingNewTasks
            && definition.ShutdownBehavior != TaskShutdownBehavior.FinishBeforeShutdown)
        {
            throw new InvalidOperationException("Playroma is closing the current workspace and is not accepting new background tasks.");
        }

        Guid id = Guid.NewGuid();
        BackgroundTaskInfo info = new(id, definition);
        _tasks[id] = info;
        Publish();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, info.Token);
        Progress<TaskProgress> progress = new(value =>
        {
            info.Progress = value;
            Publish();
        });
        info.State = BackgroundTaskState.Running;
        info.StartedUtc = DateTimeOffset.UtcNow;
        Publish();
        try
        {
            T result = await operation(progress, linked.Token).ConfigureAwait(false);
            info.State = BackgroundTaskState.Completed;
            return result;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            info.State = BackgroundTaskState.Cancelled;
            throw;
        }
        catch (Exception exception)
        {
            info.State = BackgroundTaskState.Failed;
            info.Error = exception;
            throw;
        }
        finally
        {
            info.CompletedUtc = DateTimeOffset.UtcNow;
            info.Complete();
            Publish();
        }
    }

    public void Cancel(Guid taskId)
    {
        if (_tasks.TryGetValue(taskId, out BackgroundTaskInfo? task)) task.Cancel();
    }

    public void CancelAll(Func<BackgroundTaskInfo, bool>? predicate = null)
    {
        foreach (BackgroundTaskInfo task in _tasks.Values)
        {
            if (task.State is not (BackgroundTaskState.Queued or BackgroundTaskState.Running)) continue;
            if (predicate is not null && !predicate(task)) continue;
            task.Cancel();
        }
        Publish();
    }

    public void StopAcceptingNewTasks()
    {
        _acceptingNewTasks = false;
        Publish();
    }

    public void ResumeAcceptingNewTasks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _acceptingNewTasks = true;
        Publish();
    }

    public async ValueTask<bool> AwaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Task[] pending = _tasks.Values
            .Where(task => task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running)
            .Select(task => task.Completion)
            .ToArray();
        if (pending.Length == 0) return true;

        Task all = Task.WhenAll(pending);
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            await all.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        try
        {
            await all.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void ClearCompleted() => ClearFinished(includeFailed: false);

    public void ClearFinished(bool includeFailed)
    {
        foreach (BackgroundTaskInfo task in _tasks.Values.Where(task =>
                     task.State is BackgroundTaskState.Completed or BackgroundTaskState.Cancelled
                     || (includeFailed && task.State == BackgroundTaskState.Failed)))
        {
            _tasks.TryRemove(task.Id, out _);
        }
        Publish();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _acceptingNewTasks = false;
        foreach (BackgroundTaskInfo task in _tasks.Values) task.Cancel();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _acceptingNewTasks = false;
        foreach (BackgroundTaskInfo task in _tasks.Values) task.Cancel();
        try
        {
            await AwaitIdleAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // Process shutdown must continue even if a third-party task ignores cancellation.
        }
        _disposed = true;
    }

    private void Publish() => TasksChanged?.Invoke(this, EventArgs.Empty);
}

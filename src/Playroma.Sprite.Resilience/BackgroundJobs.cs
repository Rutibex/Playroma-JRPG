using System.Collections.Concurrent;

namespace Playroma.Sprite.Resilience;

public enum SpriteBackgroundJobStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed class SpriteBackgroundJobInfo
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Background job";
    public SpriteBackgroundJobStatus Status { get; internal set; } = SpriteBackgroundJobStatus.Queued;
    public int ProgressPercent { get; internal set; }
    public string Message { get; internal set; } = "Queued";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; internal set; }
    public DateTimeOffset? FinishedAt { get; internal set; }
    public int Attempt { get; internal set; }
    public string? Error { get; internal set; }
}

public readonly record struct SpriteBackgroundProgress(int Percent, string Message);

public sealed class ResilientBackgroundJobQueue : IAsyncDisposable
{
    private readonly SemaphoreSlim concurrency;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<Guid, SpriteBackgroundJobInfo> jobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> jobCancellation = new();
    private readonly ConcurrentBag<Task> tasks = [];
    private bool disposed;

    public ResilientBackgroundJobQueue(int maximumConcurrency = 2) => concurrency = new(Math.Clamp(maximumConcurrency, 1, 8));
    public event EventHandler? Changed;
    public IReadOnlyList<SpriteBackgroundJobInfo> Jobs => jobs.Values.OrderByDescending(item => item.CreatedAt).ToArray();

    public Guid Enqueue(string name, Func<IProgress<SpriteBackgroundProgress>, CancellationToken, Task> operation, int retryCount = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(operation);
        SpriteBackgroundJobInfo info = new() { Name = name.Trim() };
        jobs[info.Id] = info;
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        jobCancellation[info.Id] = linked;
        Task task = Task.Run(() => RunAsync(info, operation, Math.Clamp(retryCount, 0, 5), linked.Token));
        tasks.Add(task);
        RaiseChanged();
        return info.Id;
    }

    public bool Cancel(Guid id)
    {
        if (!jobCancellation.TryGetValue(id, out CancellationTokenSource? cancellation)) return false;
        cancellation.Cancel(); return true;
    }

    public int CancelAll()
    {
        int cancelled = 0;
        foreach (CancellationTokenSource cancellation in jobCancellation.Values)
        {
            if (cancellation.IsCancellationRequested) continue;
            cancellation.Cancel();
            cancelled++;
        }
        return cancelled;
    }

    public void ClearFinished()
    {
        foreach ((Guid id, SpriteBackgroundJobInfo info) in jobs)
            if (info.Status is SpriteBackgroundJobStatus.Completed or SpriteBackgroundJobStatus.Failed or SpriteBackgroundJobStatus.Cancelled) jobs.TryRemove(id, out _);
        RaiseChanged();
    }

    private async Task RunAsync(SpriteBackgroundJobInfo info, Func<IProgress<SpriteBackgroundProgress>, CancellationToken, Task> operation, int retryCount, CancellationToken cancellationToken)
    {
        try
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                info.Status = SpriteBackgroundJobStatus.Running; info.StartedAt = DateTimeOffset.UtcNow; info.Message = "Running"; RaiseChanged();
                Progress<SpriteBackgroundProgress> progress = new(value => { info.ProgressPercent = Math.Clamp(value.Percent, 0, 100); info.Message = value.Message; RaiseChanged(); });
                for (int attempt = 0; ; attempt++)
                {
                    info.Attempt = attempt + 1;
                    try { await operation(progress, cancellationToken); break; }
                    catch (Exception exception) when (exception is not OperationCanceledException && attempt < retryCount)
                    {
                        info.Error = exception.Message; info.Message = $"Retrying after attempt {attempt + 1}"; RaiseChanged();
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                    }
                }
                info.Status = SpriteBackgroundJobStatus.Completed; info.ProgressPercent = 100; info.Message = "Completed";
            }
            finally { concurrency.Release(); }
        }
        catch (OperationCanceledException) { info.Status = SpriteBackgroundJobStatus.Cancelled; info.Message = "Cancelled"; }
        catch (Exception exception) { info.Status = SpriteBackgroundJobStatus.Failed; info.Error = exception.ToString(); info.Message = exception.Message; }
        finally
        {
            info.FinishedAt = DateTimeOffset.UtcNow;
            if (jobCancellation.TryRemove(info.Id, out CancellationTokenSource? cancellation)) cancellation.Dispose();
            RaiseChanged();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        try { await Task.WhenAll(tasks.ToArray()); } catch { }
        foreach (CancellationTokenSource cancellation in jobCancellation.Values) cancellation.Dispose();
        lifetime.Dispose(); concurrency.Dispose();
    }
}

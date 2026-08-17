using System.Collections.Concurrent;

namespace Playroma.JRPG.Editor.Shell;

public sealed class ProjectFileChangedEventArgs : EventArgs
{
    public ProjectFileChangedEventArgs(string relativePath, WatcherChangeTypes changeType)
    {
        RelativePath = relativePath;
        ChangeType = changeType;
    }

    public string RelativePath { get; }
    public WatcherChangeTypes ChangeType { get; }
}

public interface IProjectFileWatchService : IDisposable
{
    string? ProjectRoot { get; }
    event EventHandler<ProjectFileChangedEventArgs>? FileChanged;
    void Start(string projectRoot);
    void Stop();
    void Suppress(TimeSpan duration);
}

public sealed class ProjectFileWatchService : IProjectFileWatchService
{
    private readonly ConcurrentDictionary<string, PendingChange> _pending = new(PathComparer);
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private DateTimeOffset _suppressedUntil;
    private bool _disposed;

    public ProjectFileWatchService()
    {
        _timer = new Timer(Flush, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public string? ProjectRoot { get; private set; }
    public event EventHandler<ProjectFileChangedEventArgs>? FileChanged;

    public void Start(string projectRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        Stop();
        ProjectRoot = Path.GetFullPath(projectRoot);
        _watcher = new FileSystemWatcher(ProjectRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _timer.Change(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }
        _pending.Clear();
        ProjectRoot = null;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Suppress(TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _suppressedUntil = DateTimeOffset.UtcNow.Add(duration < TimeSpan.Zero ? TimeSpan.Zero : duration);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _timer.Dispose();
        _disposed = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Queue(e.FullPath, e.ChangeType);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Queue(e.OldFullPath, WatcherChangeTypes.Deleted);
        Queue(e.FullPath, WatcherChangeTypes.Created);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // The watcher is advisory. Phase 2 fingerprints remain authoritative at save time.
        _pending.Clear();
    }

    private void Queue(string fullPath, WatcherChangeTypes changeType)
    {
        if (DateTimeOffset.UtcNow < _suppressedUntil || ProjectRoot is null) return;
        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(ProjectRoot, fullPath).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return;
        }
        if (relativePath.StartsWith(".playroma/", StringComparison.OrdinalIgnoreCase)) return;
        _pending[relativePath] = new PendingChange(changeType, DateTimeOffset.UtcNow);
    }

    private void Flush(object? state)
    {
        if (DateTimeOffset.UtcNow < _suppressedUntil) return;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMilliseconds(-300);
        foreach ((string path, PendingChange change) in _pending.ToArray())
        {
            if (change.QueuedUtc > cutoff) continue;
            if (_pending.TryRemove(path, out PendingChange removed))
            {
                FileChanged?.Invoke(this, new ProjectFileChangedEventArgs(path, removed.ChangeType));
            }
        }
    }

    private sealed record PendingChange(WatcherChangeTypes ChangeType, DateTimeOffset QueuedUtc);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

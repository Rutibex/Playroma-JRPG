namespace Playroma.JRPG.Editor.Shell;

public interface INotificationService
{
    event EventHandler<ShellNotification>? Published;
    void Publish(ShellNotification notification);
    void Information(string title, string message);
    void Success(string title, string message);
    void Warning(string title, string message);
    void Error(string title, string message, Exception? exception = null);
}

public sealed class NotificationService : INotificationService
{
    public event EventHandler<ShellNotification>? Published;
    public void Publish(ShellNotification notification) => Published?.Invoke(this, notification);
    public void Information(string title, string message) => Publish(new(ShellNotificationKind.Information, title, message, DateTimeOffset.UtcNow));
    public void Success(string title, string message) => Publish(new(ShellNotificationKind.Success, title, message, DateTimeOffset.UtcNow));
    public void Warning(string title, string message) => Publish(new(ShellNotificationKind.Warning, title, message, DateTimeOffset.UtcNow));
    public void Error(string title, string message, Exception? exception = null) => Publish(new(ShellNotificationKind.Error, title, message, DateTimeOffset.UtcNow, exception));
}

public interface IOutputService
{
    IReadOnlyList<OutputEntry> Entries { get; }
    event EventHandler? Changed;
    void Write(
        string category,
        string message,
        Playroma.JRPG.Core.DiagnosticSeverity? severity = null,
        string? detail = null,
        NavigationLocation? location = null,
        Guid? relatedTaskId = null);
    void Clear();
}

public sealed class OutputService : IOutputService
{
    private readonly object _sync = new();
    private readonly List<OutputEntry> _entries = [];
    public event EventHandler? Changed;
    public IReadOnlyList<OutputEntry> Entries { get { lock (_sync) return _entries.ToArray(); } }

    public void Write(
        string category,
        string message,
        Playroma.JRPG.Core.DiagnosticSeverity? severity = null,
        string? detail = null,
        NavigationLocation? location = null,
        Guid? relatedTaskId = null)
    {
        lock (_sync)
        {
            _entries.Add(new OutputEntry(DateTimeOffset.Now, category, message, severity, detail, location, relatedTaskId));
            if (_entries.Count > 10_000) _entries.RemoveRange(0, _entries.Count - 10_000);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_sync) _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

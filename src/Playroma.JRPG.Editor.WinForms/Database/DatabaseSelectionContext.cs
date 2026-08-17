using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>
/// Gives every lazily cached Database category its own selection service and forwards only the
/// active category's selection to the application-wide selection service.
/// </summary>
public sealed class DatabaseSelectionContext
{
    private readonly ISelectionService _global;
    private readonly Dictionary<string, LocalSelectionService> _locals = new(StringComparer.OrdinalIgnoreCase);
    private string? _activePaneKey;

    public DatabaseSelectionContext(ISelectionService global) =>
        _global = global ?? throw new ArgumentNullException(nameof(global));

    public ISelectionService GetOrCreate(string paneKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneKey);
        if (_locals.TryGetValue(paneKey, out LocalSelectionService? existing)) return existing;
        LocalSelectionService created = new(this, paneKey);
        _locals.Add(paneKey, created);
        return created;
    }

    public void Activate(string paneKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneKey);
        _activePaneKey = paneKey;
        if (_locals.TryGetValue(paneKey, out LocalSelectionService? local))
        {
            _global.Select(local.Current);
        }
    }

    public void Suspend() => _activePaneKey = null;

    public void Clear()
    {
        // Stop forwarding cached pane selections. Do not overwrite the application selection:
        // the Map Editor or another window may already be restoring its own active context.
        _activePaneKey = null;
        _locals.Clear();
    }

    private void Publish(string paneKey, EditorSelection selection)
    {
        if (string.Equals(_activePaneKey, paneKey, StringComparison.OrdinalIgnoreCase))
        {
            _global.Select(selection);
        }
    }

    private sealed class LocalSelectionService : ISelectionService
    {
        private readonly DatabaseSelectionContext _owner;
        private readonly string _paneKey;

        public LocalSelectionService(DatabaseSelectionContext owner, string paneKey)
        {
            _owner = owner;
            _paneKey = paneKey;
        }

        public EditorSelection Current { get; private set; } = new EmptySelection();
        public event EventHandler? SelectionChanged;

        public void Select(EditorSelection selection)
        {
            ArgumentNullException.ThrowIfNull(selection);
            if (Equals(Current, selection)) return;
            Current = selection;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            _owner.Publish(_paneKey, selection);
        }
    }
}

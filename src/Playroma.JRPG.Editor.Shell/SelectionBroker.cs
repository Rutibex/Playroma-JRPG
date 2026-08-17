namespace Playroma.JRPG.Editor.Shell;

public interface IWindowSelectionContext : ISelectionService
{
    WindowKey WindowKey { get; }
    bool IsActive { get; }
    void Activate();
    void Suspend();
}

public interface ISelectionBroker : ISelectionService
{
    IWindowSelectionContext CreateContext(WindowKey key);
    IWindowSelectionContext? ActiveContext { get; }
}

public sealed class SelectionBroker : ISelectionBroker
{
    private readonly object _gate = new();
    private readonly Dictionary<WindowKey, WindowContext> _contexts = [];
    private EditorSelection _selection = new EmptySelection();
    public event EventHandler? SelectionChanged;
    public EditorSelection Current => _selection;
    public IWindowSelectionContext? ActiveContext { get; private set; }

    public void Select(EditorSelection selection)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public IWindowSelectionContext CreateContext(WindowKey key)
    {
        lock (_gate)
        {
            if (_contexts.TryGetValue(key, out WindowContext? existing)) return existing;
            WindowContext created = new(this, key);
            _contexts.Add(key, created);
            return created;
        }
    }

    private sealed class WindowContext(SelectionBroker owner, WindowKey key) : IWindowSelectionContext
    {
        private EditorSelection _selection = new EmptySelection();
        public WindowKey WindowKey { get; } = key;
        public bool IsActive => ReferenceEquals(owner.ActiveContext, this);
        public EditorSelection Current => _selection;
        public event EventHandler? SelectionChanged;
        public void Select(EditorSelection selection)
        {
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (IsActive) owner.Select(_selection);
        }
        public void Activate()
        {
            owner.ActiveContext = this;
            owner.Select(_selection);
        }
        public void Suspend()
        {
            if (IsActive) owner.ActiveContext = null;
        }
    }
}

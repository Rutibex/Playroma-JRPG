namespace Playroma.JRPG.Editor.Shell;

public interface ISelectionService
{
    EditorSelection Current { get; }
    event EventHandler? SelectionChanged;
    void Select(EditorSelection selection);
}

public sealed class SelectionService : ISelectionService
{
    public EditorSelection Current { get; private set; } = new EmptySelection();
    public event EventHandler? SelectionChanged;
    public void Select(EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (Equals(Current, selection)) return;
        Current = selection;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public interface INavigationService
{
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    event EventHandler? Changed;
    void Navigate(NavigationLocation location, bool recordHistory = true);
    NavigationLocation? GoBack();
    NavigationLocation? GoForward();
    void Clear();
    NavigationLocation? Current { get; }
}

public sealed class NavigationService : INavigationService
{
    private readonly List<NavigationLocation> _history = [];
    private int _position = -1;

    public bool CanGoBack => _position > 0;
    public bool CanGoForward => _position >= 0 && _position < _history.Count - 1;
    public NavigationLocation? Current => _position >= 0 && _position < _history.Count ? _history[_position] : null;
    public event EventHandler? Changed;

    public void Navigate(NavigationLocation location, bool recordHistory = true)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (recordHistory)
        {
            if (_position < _history.Count - 1) _history.RemoveRange(_position + 1, _history.Count - _position - 1);
            if (!Equals(Current, location))
            {
                _history.Add(location);
                _position = _history.Count - 1;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public NavigationLocation? GoBack()
    {
        if (!CanGoBack) return Current;
        _position--;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public NavigationLocation? GoForward()
    {
        if (!CanGoForward) return Current;
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public void Clear()
    {
        _history.Clear();
        _position = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

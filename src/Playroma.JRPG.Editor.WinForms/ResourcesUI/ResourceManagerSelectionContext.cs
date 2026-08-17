using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.ResourcesUI;

/// <summary>
/// Keeps asynchronous catalogue and preview activity local to Resource Manager unless the window
/// is active. This prevents a delayed thumbnail or metadata result from stealing another window's
/// application selection.
/// </summary>
public sealed class ResourceManagerSelectionContext : ISelectionService
{
    private readonly ISelectionService _global;
    private bool _active;

    public ResourceManagerSelectionContext(ISelectionService global) =>
        _global = global ?? throw new ArgumentNullException(nameof(global));

    public EditorSelection Current { get; private set; } = new EmptySelection();
    public event EventHandler? SelectionChanged;

    public void Select(EditorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (Equals(Current, selection)) return;
        Current = selection;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (_active) _global.Select(selection);
    }

    public void Activate()
    {
        _active = true;
        _global.Select(Current);
    }

    public void Suspend() => _active = false;

    public void Clear()
    {
        _active = false;
        Current = new EmptySelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

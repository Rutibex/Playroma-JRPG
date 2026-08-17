using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Core.Persistence;

public sealed class ProjectHistory
{
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private readonly int _capacity;

    public ProjectHistory(int capacity = 64)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoLabel => _undo.TryPeek(out HistoryEntry? entry) ? entry.Label : null;
    public string? NextRedoLabel => _redo.TryPeek(out HistoryEntry? entry) ? entry.Label : null;

    public void Capture(ProjectDocument current, string label)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _undo.Push(new HistoryEntry(label, ProjectSerializer.ToJson(current, indented: false)));
        _redo.Clear();
        TrimUndo();
    }

    public ProjectDocument Undo(ProjectDocument current)
    {
        if (!CanUndo)
        {
            return current;
        }

        HistoryEntry entry = _undo.Pop();
        _redo.Push(new HistoryEntry(entry.Label, ProjectSerializer.ToJson(current, indented: false)));
        return ProjectSerializer.FromJson(entry.Json);
    }

    public ProjectDocument Redo(ProjectDocument current)
    {
        if (!CanRedo)
        {
            return current;
        }

        HistoryEntry entry = _redo.Pop();
        _undo.Push(new HistoryEntry(entry.Label, ProjectSerializer.ToJson(current, indented: false)));
        TrimUndo();
        return ProjectSerializer.FromJson(entry.Json);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void TrimUndo()
    {
        if (_undo.Count <= _capacity)
        {
            return;
        }

        HistoryEntry[] entries = _undo.ToArray();
        _undo.Clear();
        for (int index = Math.Min(_capacity, entries.Length) - 1; index >= 0; index--)
        {
            _undo.Push(entries[index]);
        }
    }

    private sealed record HistoryEntry(string Label, string Json);
}

namespace Playroma.Sound.Authoring.History;

public interface IAuthoringEdit
{
    string Description { get; }
    string MergeKey { get; }
    void Apply();
    void Revert();
    bool TryMerge(IAuthoringEdit newerEdit);
}

public sealed class DelegateAuthoringEdit : IAuthoringEdit
{
    private readonly Action _apply;
    private readonly Action _revert;

    public DelegateAuthoringEdit(string description, Action apply, Action revert, string mergeKey = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _revert = revert ?? throw new ArgumentNullException(nameof(revert));
        Description = description;
        MergeKey = mergeKey ?? "";
    }

    public string Description { get; }
    public string MergeKey { get; }
    public void Apply() => _apply();
    public void Revert() => _revert();
    public bool TryMerge(IAuthoringEdit newerEdit) => false;
}

public sealed class CompositeAuthoringEdit : IAuthoringEdit
{
    private readonly IAuthoringEdit[] _edits;

    public CompositeAuthoringEdit(string description, IEnumerable<IAuthoringEdit> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(edits);
        Description = description;
        _edits = edits.ToArray();
        if (_edits.Length == 0) throw new ArgumentException("A composite edit requires at least one child edit.", nameof(edits));
    }

    public string Description { get; }
    public string MergeKey => "";

    public void Apply()
    {
        foreach (IAuthoringEdit edit in _edits) edit.Apply();
    }

    public void Revert()
    {
        for (int i = _edits.Length - 1; i >= 0; i--) _edits[i].Revert();
    }

    public bool TryMerge(IAuthoringEdit newerEdit) => false;
}

public sealed class UndoRedoHistory
{
    private readonly List<IAuthoringEdit> _undo = [];
    private readonly List<IAuthoringEdit> _redo = [];
    private readonly int _capacity;

    public UndoRedoHistory(int capacity = 256)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoDescription => CanUndo ? _undo[^1].Description : "";
    public string RedoDescription => CanRedo ? _redo[^1].Description : "";
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Execute(IAuthoringEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Apply();
        if (_undo.Count > 0 && !string.IsNullOrEmpty(edit.MergeKey) && StringComparer.Ordinal.Equals(_undo[^1].MergeKey, edit.MergeKey) && _undo[^1].TryMerge(edit))
        {
            _redo.Clear();
            return;
        }

        _undo.Add(edit);
        _redo.Clear();
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        IAuthoringEdit edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        edit.Revert();
        _redo.Add(edit);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        IAuthoringEdit edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        edit.Apply();
        _undo.Add(edit);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

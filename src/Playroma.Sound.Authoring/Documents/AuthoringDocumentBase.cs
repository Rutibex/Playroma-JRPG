using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.History;
using Playroma.Sound.Authoring.Projects;

namespace Playroma.Sound.Authoring.Documents;

public abstract class AuthoringDocumentBase<TModel> : IAuthoringDocument where TModel : class
{
    private readonly UndoRedoHistory _history;
    private TModel _model;
    private long _revision;
    private long _savedRevision;
    private string? _savedContent;

    protected AuthoringDocumentBase(string filePath, TModel model, int historyCapacity = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _model = model ?? throw new ArgumentNullException(nameof(model));
        FilePath = Path.GetFullPath(filePath);
        _history = new UndoRedoHistory(historyCapacity);
    }

    public abstract AuthoringDocumentKind Kind { get; }
    public abstract string DocumentId { get; }
    public abstract string DisplayName { get; }
    public string FilePath { get; private set; }
    public bool IsDirty => !StringComparer.Ordinal.Equals(GetSavedContent(), SerializeModel(_model));
    public long Revision => Interlocked.Read(ref _revision);
    public long SavedRevision => Interlocked.Read(ref _savedRevision);
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public string UndoDescription => _history.UndoDescription;
    public string RedoDescription => _history.RedoDescription;
    protected TModel CurrentModel => _model;

    /// <summary>Returns a detached snapshot. Mutating it never changes this document.</summary>
    public TModel CreateEditableCopy() => DeserializeModel(SerializeModel(_model));

    public event EventHandler<AuthoringDocumentChangedEventArgs>? Changed;

    public void Mutate(string description, Action<TModel> mutation, string mergeKey = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(mutation);
        string before = SerializeModel(_model);
        TModel working = DeserializeModel(before);
        mutation(working);
        string after = SerializeModel(working);
        if (StringComparer.Ordinal.Equals(before, after)) return;
        _history.Execute(new SnapshotEdit(this, description, before, after, mergeKey));
    }

    public void ReplaceModel(TModel model, string description)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReplaceSerializedContent(SerializeModel(model), description);
    }

    public void ReplaceSerializedContent(string serializedContent, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        string before = SerializeModel(_model);
        TModel parsed = DeserializeModel(serializedContent);
        string after = SerializeModel(parsed);
        if (StringComparer.Ordinal.Equals(before, after)) return;
        _history.Execute(new SnapshotEdit(this, description, before, after, ""));
    }

    public bool Undo() => _history.Undo();

    public bool Redo() => _history.Redo();

    public string SerializeContent() => SerializeModel(_model);

    public void Save() => SaveAs(FilePath);

    public void SaveAs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        AtomicTextFile.Write(fullPath, SerializeModel(_model));
        FilePath = fullPath;
        _savedRevision = _revision;
        _savedContent = SerializeModel(_model);
        OnChanged("Saved");
    }

    public void MarkSaved()
    {
        _savedRevision = _revision;
        _savedContent = SerializeModel(_model);
        OnChanged("Marked saved");
    }

    public abstract ValidationSummary Validate();
    public abstract Task<AuthoringCompilationResult> CompileAsync(AuthoringCompilationContext context, CancellationToken cancellationToken = default);
    protected abstract string SerializeModel(TModel model);
    protected abstract TModel DeserializeModel(string content);

    protected void InitializeSavedState()
    {
        _savedRevision = _revision;
        _savedContent = SerializeModel(_model);
    }

    protected void ReplaceWithoutHistory(TModel model, string reason)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _revision++;
        OnChanged(reason);
    }

    protected void OnChanged(string reason) => Changed?.Invoke(this, new AuthoringDocumentChangedEventArgs(DocumentId, _revision, reason));

    private string GetSavedContent() => _savedContent ?? throw new InvalidOperationException("Document saved state was not initialized by the concrete document constructor.");

    private void ApplySnapshot(string content, string reason)
    {
        _model = DeserializeModel(content);
        _revision++;
        OnChanged(reason);
    }

    private sealed class SnapshotEdit : IAuthoringEdit
    {
        private readonly AuthoringDocumentBase<TModel> _owner;
        private readonly string _before;
        private string _after;

        public SnapshotEdit(AuthoringDocumentBase<TModel> owner, string description, string before, string after, string mergeKey)
        {
            _owner = owner;
            Description = description;
            _before = before;
            _after = after;
            MergeKey = mergeKey ?? "";
        }

        public string Description { get; }
        public string MergeKey { get; }
        public void Apply() => _owner.ApplySnapshot(_after, Description);
        public void Revert() => _owner.ApplySnapshot(_before, "Undo " + Description);

        public bool TryMerge(IAuthoringEdit newerEdit)
        {
            if (newerEdit is not SnapshotEdit newer || !ReferenceEquals(_owner, newer._owner) || string.IsNullOrEmpty(MergeKey) || !StringComparer.Ordinal.Equals(MergeKey, newer.MergeKey)) return false;
            _after = newer._after;
            return true;
        }
    }
}

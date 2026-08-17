using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

public interface IUndoManager
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool IsAtSavedState { get; }
    string? UndoDescription { get; }
    string? RedoDescription { get; }
    int UndoCount { get; }
    int RedoCount { get; }

    ValueTask ExecuteAsync(IEditorCommand command, CancellationToken cancellationToken = default);
    ValueTask UndoAsync(CancellationToken cancellationToken = default);
    ValueTask RedoAsync(CancellationToken cancellationToken = default);
    void MarkSaved();
    void Clear();
}

public sealed class UndoManager : IUndoManager
{
    private readonly EditorCommandContext _context;
    private readonly List<IEditorCommand> _history = [];
    private int _position;
    private int? _savedPosition = 0;

    public UndoManager(EditorCommandContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public event EventHandler? HistoryChanged;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _history.Count;
    public bool IsAtSavedState => _savedPosition.HasValue && _savedPosition.Value == _position;
    public string? UndoDescription => CanUndo ? _history[_position - 1].Description : null;
    public string? RedoDescription => CanRedo ? _history[_position].Description : null;
    public int UndoCount => _position;
    public int RedoCount => _history.Count - _position;
    public IReadOnlyList<IEditorCommand> History => _history;

    internal void RecordExecuted(IEditorCommand command, bool allowMerge)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_position < _history.Count)
        {
            if (_savedPosition.HasValue && _savedPosition.Value > _position)
            {
                _savedPosition = null;
            }

            _history.RemoveRange(_position, _history.Count - _position);
        }

        if (allowMerge
            && _position > 0
            && _history[_position - 1] is IMergeableEditorCommand mergeable
            && mergeable.TryMergeWith(command, out IEditorCommand merged))
        {
            // A merge replaces the last entry without moving the history cursor. If the
            // cursor had been marked saved, the replacement still represents a new edit.
            if (_savedPosition == _position)
            {
                _savedPosition = null;
            }

            _history[_position - 1] = merged;
        }
        else
        {
            _history.Add(command);
            _position++;
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask ExecuteAsync(IEditorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using ProjectTransaction transaction = new(command.Description, _context, this);
        transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UndoAsync(CancellationToken cancellationToken = default)
    {
        await _context.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CanUndo) return;
            IEditorCommand command = _history[_position - 1];
            await ExecuteHistoryOperationAsync(
                command,
                static (candidate, context, token) => candidate.UndoAsync(context, token),
                EditorCommandExecutionKind.Undo,
                cancellationToken).ConfigureAwait(false);
            _position--;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _context.OperationGate.Release();
        }
    }

    public async ValueTask RedoAsync(CancellationToken cancellationToken = default)
    {
        await _context.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CanRedo) return;
            IEditorCommand command = _history[_position];
            await ExecuteHistoryOperationAsync(
                command,
                static (candidate, context, token) => candidate.ExecuteAsync(context, token),
                EditorCommandExecutionKind.Redo,
                cancellationToken).ConfigureAwait(false);
            _position++;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _context.OperationGate.Release();
        }
    }

    public void MarkSaved()
    {
        _savedPosition = _position;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _history.Clear();
        _position = 0;
        _savedPosition = 0;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask ExecuteHistoryOperationAsync(
        IEditorCommand command,
        Func<IEditorCommand, EditorCommandContext, CancellationToken, ValueTask> operation,
        EditorCommandExecutionKind executionKind,
        CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> baselines = await CaptureBaselinesAsync(command, cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(command, _context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            IReadOnlyList<Exception> restoreFailures = await RestoreBaselinesAsync(baselines).ConfigureAwait(false);
            if (cause is OperationCanceledException && restoreFailures.Count == 0)
            {
                throw;
            }

            throw new EditorTransactionException(
                $"Could not {executionKind.ToString().ToLowerInvariant()} '{command.Description}'. The previous document state was restored.",
                cause,
                restoreFailures);
        }

        _context.Publish(command, executionKind);
    }

    private async ValueTask<Dictionary<string, byte[]>> CaptureBaselinesAsync(
        IEditorCommand command,
        CancellationToken cancellationToken)
    {
        Dictionary<string, byte[]> baselines = new(PathComparer);
        foreach (string relativePath in command.Targets
                     .Where(target => target.Kind == EditorMutationTargetKind.Json)
                     .Select(target => target.NormalizedRelativePath)
                     .Distinct(PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await _context.OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (document is ITransactionalDocumentSnapshot snapshot)
            {
                baselines[relativePath] = snapshot.CaptureGeneratedBytes();
            }
        }

        return baselines;
    }

    private async ValueTask<IReadOnlyList<Exception>> RestoreBaselinesAsync(
        IReadOnlyDictionary<string, byte[]> baselines)
    {
        List<Exception> failures = [];
        foreach ((string relativePath, byte[] bytes) in baselines)
        {
            try
            {
                IMutableProjectDocument document = await _context
                    .OpenDocumentAsync(relativePath, CancellationToken.None)
                    .ConfigureAwait(false);
                ITransactionalDocumentSnapshot snapshot = document as ITransactionalDocumentSnapshot
                    ?? throw new NotSupportedException(
                        $"Document '{relativePath}' does not support transactional snapshots.");
                snapshot.RestoreGeneratedBytes(bytes);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

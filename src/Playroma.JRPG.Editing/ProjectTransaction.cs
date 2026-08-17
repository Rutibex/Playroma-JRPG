using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

public interface IProjectTransaction : IAsyncDisposable
{
    Guid Id { get; }
    string Description { get; }
    IReadOnlyList<IEditorCommand> Commands { get; }
    bool IsCommitted { get; }
    bool IsRolledBack { get; }

    void Add(IEditorCommand command);
    ValueTask<TransactionPreview> PreviewAsync(CancellationToken cancellationToken = default);
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class ProjectTransaction : IProjectTransaction
{
    private readonly EditorCommandContext _context;
    private readonly UndoManager _undoManager;
    private readonly List<IEditorCommand> _commands = [];
    private readonly List<IEditorCommand> _executed = [];
    private readonly Dictionary<string, byte[]> _documentBaselines = new(PathComparer);
    private readonly bool _allowHistoryMerge;
    private bool _disposed;

    public ProjectTransaction(
        string description,
        EditorCommandContext context,
        UndoManager undoManager,
        bool allowHistoryMerge = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
        _allowHistoryMerge = allowHistoryMerge;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Description { get; }
    public IReadOnlyList<IEditorCommand> Commands => _commands;
    public bool IsCommitted { get; private set; }
    public bool IsRolledBack { get; private set; }

    public void Add(IEditorCommand command)
    {
        ThrowIfDisposed();
        if (IsCommitted || IsRolledBack)
        {
            throw new InvalidOperationException("Commands cannot be added after a transaction has completed.");
        }

        _commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
    }

    public async ValueTask<TransactionPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsCommitted) throw new InvalidOperationException("A committed transaction cannot be previewed.");
        if (IsRolledBack) throw new InvalidOperationException("A rolled-back transaction cannot be previewed.");
        if (_commands.Count == 0) throw new InvalidOperationException("The transaction contains no commands.");

        await _context.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CaptureBaselinesAsync(cancellationToken).ConfigureAwait(false);
            HashSet<Guid> fileBaseline = _context.FileOverlay?.PendingMutations
                .Select(mutation => mutation.Id)
                .ToHashSet() ?? [];
            TransactionPreview? preview = null;
            Exception? operationFailure = null;
            try
            {
                foreach (IEditorCommand command in _commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await command.ExecuteAsync(_context, cancellationToken).ConfigureAwait(false);
                }

                List<TransactionDocumentPreview> documents = [];
                foreach ((string relativePath, byte[] before) in _documentBaselines.OrderBy(item => item.Key, PathComparer))
                {
                    IMutableProjectDocument mutable = await _context
                        .OpenDocumentAsync(relativePath, cancellationToken)
                        .ConfigureAwait(false);
                    ITransactionalDocumentSnapshot document = mutable as ITransactionalDocumentSnapshot
                        ?? throw new NotSupportedException(
                            $"Document '{relativePath}' does not support transactional snapshots.");
                    byte[] after = document.CaptureGeneratedBytes();
                    IReadOnlyList<Playroma.JRPG.Documents.Json.JsonSemanticDifference> differences =
                        Playroma.JRPG.Documents.Json.JsonSemanticDiffer.Compare(before, after);
                    if (differences.Count == 0) continue;
                    documents.Add(new TransactionDocumentPreview(
                        relativePath,
                        differences,
                        ComputeSha256(before),
                        ComputeSha256(after)));
                }

                TransactionFilePreview[] files = _context.FileOverlay?.PendingMutations
                    .Where(mutation => !fileBaseline.Contains(mutation.Id))
                    .Select(mutation => new TransactionFilePreview(
                        mutation.Kind.ToString(),
                        mutation.SourceRelativePath,
                        mutation.DestinationRelativePath,
                        mutation.StagedLength))
                    .ToArray() ?? [];
                preview = new TransactionPreview(Id, Description, documents) { Files = files };
            }
            catch (Exception exception)
            {
                operationFailure = exception;
            }

            List<Exception> previewFailures = [];
            previewFailures.AddRange(await UndoFileCommandsAsync(_commands, CancellationToken.None).ConfigureAwait(false));
            foreach (IPreviewResourceCommand command in _commands.OfType<IPreviewResourceCommand>()) command.ReleasePreviewResources();
            previewFailures.AddRange(await RestoreBaselinesAsync(CancellationToken.None).ConfigureAwait(false));
            IReadOnlyList<Exception> restoreFailures = previewFailures;
            if (operationFailure is not null)
            {
                if (operationFailure is OperationCanceledException && restoreFailures.Count == 0)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operationFailure).Throw();
                }

                throw new EditorTransactionException(
                    $"Transaction preview '{Description}' failed. The previous document state was restored where possible.",
                    operationFailure,
                    restoreFailures);
            }

            if (restoreFailures.Count > 0)
            {
                throw new AggregateException(
                    "The transaction preview completed, but one or more documents could not be restored.",
                    restoreFailures);
            }

            return preview ?? throw new InvalidOperationException("The transaction preview did not produce a result.");
        }
        finally
        {
            _context.OperationGate.Release();
        }
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsCommitted) throw new InvalidOperationException("The transaction is already committed.");
        if (IsRolledBack) throw new InvalidOperationException("The transaction has already been rolled back.");
        if (_commands.Count == 0) throw new InvalidOperationException("The transaction contains no commands.");

        await _context.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CaptureBaselinesAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (IEditorCommand command in _commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await command.ExecuteAsync(_context, cancellationToken).ConfigureAwait(false);
                        _executed.Add(command);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        throw new EditorCommandException(
                            $"Editor command '{command.Description}' failed.",
                            command,
                            exception);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                List<Exception> failures = [];
                failures.AddRange(await UndoFileCommandsAsync(_executed, CancellationToken.None).ConfigureAwait(false));
                failures.AddRange(await RestoreBaselinesAsync(CancellationToken.None).ConfigureAwait(false));
                IReadOnlyList<Exception> rollbackFailures = failures;
                _executed.Clear();
                IsRolledBack = true;
                if (rollbackFailures.Count > 0)
                {
                    throw new EditorTransactionException(
                        $"Transaction '{Description}' was cancelled, but one or more documents could not be restored.",
                        new OperationCanceledException(cancellationToken),
                        rollbackFailures);
                }

                throw;
            }
            catch (Exception cause)
            {
                List<Exception> failures = [];
                failures.AddRange(await UndoFileCommandsAsync(_executed, CancellationToken.None).ConfigureAwait(false));
                failures.AddRange(await RestoreBaselinesAsync(CancellationToken.None).ConfigureAwait(false));
                IReadOnlyList<Exception> rollbackFailures = failures;
                _executed.Clear();
                IsRolledBack = true;
                throw new EditorTransactionException(
                    $"Transaction '{Description}' failed and was rolled back.",
                    cause,
                    rollbackFailures);
            }

            IEditorCommand historyCommand = _commands.Count == 1
                ? _commands[0]
                : new CompositeEditorCommand(Description, _commands);
            _undoManager.RecordExecuted(historyCommand, _allowHistoryMerge);
            IsCommitted = true;
            _context.Publish(historyCommand, EditorCommandExecutionKind.Execute);
        }
        finally
        {
            _context.OperationGate.Release();
        }
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsCommitted)
        {
            throw new InvalidOperationException("A committed transaction must be reverted through the undo manager.");
        }

        if (IsRolledBack) return;
        await _context.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Exception> rollbackFailures = [];
            rollbackFailures.AddRange(await UndoFileCommandsAsync(_executed, cancellationToken).ConfigureAwait(false));
            rollbackFailures.AddRange(_documentBaselines.Count > 0
                ? await RestoreBaselinesAsync(cancellationToken).ConfigureAwait(false)
                : await UndoExecutedAsync(cancellationToken).ConfigureAwait(false));
            IReadOnlyList<Exception> failures = rollbackFailures;
            _executed.Clear();
            IsRolledBack = true;
            if (failures.Count > 0)
            {
                throw new AggregateException("One or more commands failed while rolling back the transaction.", failures);
            }

            if (_commands.Count > 0)
            {
                IEditorCommand rollbackCommand = _commands.Count == 1
                    ? _commands[0]
                    : new CompositeEditorCommand(Description, _commands);
                _context.Publish(rollbackCommand, EditorCommandExecutionKind.Rollback);
            }
        }
        finally
        {
            _context.OperationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!IsCommitted && !IsRolledBack && _executed.Count > 0)
        {
            await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _disposed = true;
    }

    private async ValueTask CaptureBaselinesAsync(CancellationToken cancellationToken)
    {
        _documentBaselines.Clear();
        foreach (string relativePath in _commands
                     .SelectMany(command => command.Targets)
                     .Where(target => target.Kind == EditorMutationTargetKind.Json)
                     .Select(target => target.NormalizedRelativePath)
                     .Distinct(PathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document =
                await _context.OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (document is not ITransactionalDocumentSnapshot snapshot)
            {
                throw new NotSupportedException(
                    $"Transactional editing requires snapshot support; '{relativePath}' is not supported.");
            }

            _documentBaselines[relativePath] = snapshot.CaptureGeneratedBytes();
        }
    }

    private async ValueTask<IReadOnlyList<Exception>> RestoreBaselinesAsync(CancellationToken cancellationToken)
    {
        List<Exception> failures = [];
        foreach ((string relativePath, byte[] bytes) in _documentBaselines)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMutableProjectDocument document =
                    await _context.OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
                if (document is not ITransactionalDocumentSnapshot snapshot)
                {
                    throw new NotSupportedException(
                        $"Document '{relativePath}' does not support transactional snapshots.");
                }
                snapshot.RestoreGeneratedBytes(bytes);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private async ValueTask<IReadOnlyList<Exception>> UndoExecutedAsync(CancellationToken cancellationToken)
    {
        List<Exception> failures = [];
        for (int index = _executed.Count - 1; index >= 0; index--)
        {
            try
            {
                await _executed[index].UndoAsync(_context, cancellationToken).ConfigureAwait(false);
                _context.Publish(_executed[index], EditorCommandExecutionKind.Rollback);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        _executed.Clear();
        return failures;
    }

    private async ValueTask<IReadOnlyList<Exception>> UndoFileCommandsAsync(
        IEnumerable<IEditorCommand> commands,
        CancellationToken cancellationToken)
    {
        List<Exception> failures = [];
        IEditorCommand[] materialized = commands
            .Where(command => command.Targets.Any(target => target.Kind == EditorMutationTargetKind.File))
            .ToArray();
        for (int index = materialized.Length - 1; index >= 0; index--)
        {
            try
            {
                await materialized[index].UndoAsync(_context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

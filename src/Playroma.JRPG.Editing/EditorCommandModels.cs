using Playroma.JRPG.Documents.Json;

namespace Playroma.JRPG.Editing;

public enum EditorMutationTargetKind
{
    Json,
    File
}

public sealed record EditorMutationTarget(string RelativePath, JsonPath Path)
{
    public string NormalizedRelativePath { get; } = Normalize(RelativePath);
    public EditorMutationTargetKind Kind { get; init; } = EditorMutationTargetKind.Json;

    public static EditorMutationTarget ForFile(string relativePath) =>
        new(relativePath, JsonPath.Root) { Kind = EditorMutationTargetKind.File };

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Replace('\\', '/');
    }
}

public enum EditorCommandExecutionKind
{
    Execute,
    Undo,
    Redo,
    Rollback
}

public sealed record EditorCommandEvent(
    Guid CommandId,
    string Description,
    EditorCommandExecutionKind Kind,
    IReadOnlyCollection<EditorMutationTarget> Targets,
    DateTimeOffset TimestampUtc);

public sealed class EditorCommandNotificationFailureEventArgs : EventArgs
{
    public EditorCommandNotificationFailureEventArgs(EditorCommandEvent notification, Exception exception)
    {
        Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public EditorCommandEvent Notification { get; }
    public Exception Exception { get; }
}

public interface IEditorCommand
{
    Guid Id { get; }
    string Description { get; }
    IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default);

    ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default);
}

public interface IMergeableEditorCommand : IEditorCommand
{
    bool TryMergeWith(IEditorCommand newerCommand, out IEditorCommand mergedCommand);
}


public sealed record TransactionDocumentPreview(
    string RelativePath,
    IReadOnlyList<JsonSemanticDifference> Differences,
    string BeforeSha256,
    string AfterSha256);

public sealed record TransactionFilePreview(
    string Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    long GeneratedLength);

public sealed record TransactionPreview(
    Guid TransactionId,
    string Description,
    IReadOnlyList<TransactionDocumentPreview> Documents)
{
    public IReadOnlyList<TransactionFilePreview> Files { get; init; } = [];
    public int ChangedDocumentCount => Documents.Count;
    public int ChangedFileCount => Files.Count;
    public int DifferenceCount => Documents.Sum(document => document.Differences.Count);
}

public sealed class EditorCommandException : Exception
{
    public EditorCommandException(string message, IEditorCommand command, Exception innerException)
        : base(message, innerException)
    {
        Command = command;
    }

    public IEditorCommand Command { get; }
}

public sealed class EditorTransactionException : Exception
{
    public EditorTransactionException(
        string message,
        Exception cause,
        IReadOnlyList<Exception> rollbackFailures)
        : base(message, cause)
    {
        RollbackFailures = rollbackFailures;
    }

    public IReadOnlyList<Exception> RollbackFailures { get; }
}

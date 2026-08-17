using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editing;

public interface IEditorDocumentResolver
{
    ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceEditorDocumentResolver : IEditorDocumentResolver
{
    private readonly IMvProjectWorkspace _workspace;

    public WorkspaceEditorDocumentResolver(IMvProjectWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default) =>
        _workspace.OpenDocumentAsync(relativePath, cancellationToken);
}

public sealed class EditorCommandContext
{
    private readonly IEditorDocumentResolver _resolver;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public EditorCommandContext(IEditorDocumentResolver resolver, IProjectFileOverlay? fileOverlay = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        FileOverlay = fileOverlay;
    }

    public IProjectFileOverlay? FileOverlay { get; }

    public event EventHandler<EditorCommandEvent>? CommandApplied;
    public event EventHandler<EditorCommandNotificationFailureEventArgs>? CommandNotificationFailed;

    public async ValueTask<IMutableProjectDocument> OpenDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default) =>
        await _resolver.OpenDocumentAsync(Normalize(relativePath), cancellationToken).ConfigureAwait(false);

    public async ValueTask<MutableJsonProjectDocument> OpenJsonDocumentAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        IMutableProjectDocument document = await OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
        return document as MutableJsonProjectDocument
            ?? throw new InvalidOperationException($"Document '{relativePath}' is not a mutable JSON document.");
    }

    internal SemaphoreSlim OperationGate => _operationGate;

    internal void Publish(IEditorCommand command, EditorCommandExecutionKind kind)
    {
        EditorCommandEvent notification = new(
            command.Id,
            command.Description,
            kind,
            command.Targets,
            DateTimeOffset.UtcNow);
        EventHandler<EditorCommandEvent>? handlers = CommandApplied;
        if (handlers is null) return;

        foreach (EventHandler<EditorCommandEvent> handler in handlers.GetInvocationList().Cast<EventHandler<EditorCommandEvent>>())
        {
            try
            {
                handler(this, notification);
            }
            catch (Exception exception)
            {
                PublishNotificationFailure(notification, exception);
            }
        }
    }

    private void PublishNotificationFailure(EditorCommandEvent notification, Exception exception)
    {
        EventHandler<EditorCommandNotificationFailureEventArgs>? handlers = CommandNotificationFailed;
        if (handlers is null) return;
        EditorCommandNotificationFailureEventArgs arguments = new(notification, exception);
        foreach (EventHandler<EditorCommandNotificationFailureEventArgs> handler in handlers.GetInvocationList()
                     .Cast<EventHandler<EditorCommandNotificationFailureEventArgs>>())
        {
            try
            {
                handler(this, arguments);
            }
            catch
            {
                // Notification error reporting must never corrupt a completed edit.
            }
        }
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

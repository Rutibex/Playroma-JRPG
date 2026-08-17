using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

/// <summary>
/// Replaces the complete transactional representation of a mutable document. For ordinary
/// JSON documents this is the full JSON root; for plugins.js it is the extracted $plugins array.
/// The surrounding JavaScript wrapper remains owned by MvPluginManifestDocument.
/// </summary>
public sealed class ReplaceDocumentContentCommand : IEditorCommand
{
    private readonly byte[] _newBytes;
    private byte[]? _oldBytes;

    public ReplaceDocumentContentCommand(
        string relativePath,
        ReadOnlySpan<byte> newBytes,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (newBytes.IsEmpty) throw new ArgumentException("Replacement content cannot be empty.", nameof(newBytes));
        RelativePath = relativePath.Replace('\\', '/');
        _newBytes = newBytes.ToArray();
        Description = description ?? $"Replace {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, JsonPath.Root)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        IMutableProjectDocument document = await context
            .OpenDocumentAsync(RelativePath, cancellationToken)
            .ConfigureAwait(false);
        ITransactionalDocumentSnapshot snapshot = document as ITransactionalDocumentSnapshot
            ?? throw new NotSupportedException(
                $"Document '{RelativePath}' does not support complete-content replacement.");
        _oldBytes ??= snapshot.CaptureGeneratedBytes();
        snapshot.RestoreGeneratedBytes(_newBytes);
    }

    public async ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        byte[] oldBytes = _oldBytes
            ?? throw new InvalidOperationException("The replacement command has not been executed.");
        IMutableProjectDocument document = await context
            .OpenDocumentAsync(RelativePath, cancellationToken)
            .ConfigureAwait(false);
        ITransactionalDocumentSnapshot snapshot = document as ITransactionalDocumentSnapshot
            ?? throw new NotSupportedException(
                $"Document '{RelativePath}' does not support complete-content replacement.");
        snapshot.RestoreGeneratedBytes(oldBytes);
    }
}

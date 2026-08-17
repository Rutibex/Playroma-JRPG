using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Mapping;

namespace Playroma.JRPG.Editing;

public sealed class ApplyMapDataPatchCommand : IEditorCommand
{
    public ApplyMapDataPatchCommand(
        string relativePath,
        MapDataPatch patch,
        string description = "Edit map")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        RelativePath = relativePath.Replace('\\', '/');
        Patch = patch ?? throw new ArgumentNullException(nameof(patch));
        Description = description;
        Targets = [new EditorMutationTarget(RelativePath, JsonPath.Root.Append("data"))];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public MapDataPatch Patch { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.SetInt32ArrayValues(
            JsonPath.Root.Append("data"),
            Patch.Expand()
                .Select(change => new IndexedInt32Value(change.Index, change.After, change.Before))
                .ToArray(),
            Description);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.SetInt32ArrayValues(
            JsonPath.Root.Append("data"),
            Patch.Expand()
                .Select(change => new IndexedInt32Value(change.Index, change.Before, change.After))
                .ToArray(),
            "Undo " + Description);
    }
}

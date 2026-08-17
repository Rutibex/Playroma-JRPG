using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Mapping;

namespace Playroma.JRPG.Editing;

public sealed class ResizeMapCommand : IEditorCommand
{
    private byte[]? _beforeBytes;

    public ResizeMapCommand(
        string relativePath,
        MapResizePlan plan,
        string description = "Resize map")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        RelativePath = relativePath.Replace('\\', '/');
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Description = description;
        Targets = [new EditorMutationTarget(RelativePath, JsonPath.Root)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public MapResizePlan Plan { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        _beforeBytes ??= document.CaptureGeneratedBytes();
        document.SetInt32(JsonPath.Root.Append("width"), Plan.NewWidth);
        document.SetInt32(JsonPath.Root.Append("height"), Plan.NewHeight);
        document.SetRawJson(JsonPath.Root.Append("data"), JsonSerializer.SerializeToUtf8Bytes(Plan.NewData));
        foreach (MapEventRelocation relocation in Plan.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonPath eventPath = JsonPath.Root.Append("events").Append(relocation.Id);
            if (!document.ContainsPath(eventPath)) continue;
            if (relocation.IsDeleted)
            {
                document.SetNull(eventPath);
            }
            else if (relocation.After is MapPoint after)
            {
                document.SetInt32(eventPath.Append("x"), after.X);
                document.SetInt32(eventPath.Append("y"), after.Y);
            }
        }
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_beforeBytes is null) throw new InvalidOperationException("The resize command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.RestoreGeneratedBytes(_beforeBytes);
    }
}

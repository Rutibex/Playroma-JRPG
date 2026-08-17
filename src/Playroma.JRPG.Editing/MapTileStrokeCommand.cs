using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

public readonly record struct TileDelta(int Index, int Before, int After)
{
    public TileDelta(int index, int after) : this(index, int.MinValue, after)
    {
    }
}

public sealed class MapTileStrokeCommand : IEditorCommand
{
    private readonly TileDelta[] _requestedDeltas;
    private TileDelta[]? _capturedDeltas;

    public MapTileStrokeCommand(
        string relativePath,
        IEnumerable<TileDelta> deltas,
        string description = "Paint map tiles")
    {
        ArgumentNullException.ThrowIfNull(deltas);
        RelativePath = relativePath.Replace('\\', '/');
        _requestedDeltas = deltas
            .GroupBy(delta => delta.Index)
            .Select(group => group.Last())
            .OrderBy(delta => delta.Index)
            .ToArray();
        if (_requestedDeltas.Length == 0)
        {
            throw new ArgumentException("A map tile stroke must contain at least one tile delta.", nameof(deltas));
        }

        if (_requestedDeltas.Any(delta => delta.Index < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(deltas), "Tile indexes cannot be negative.");
        }

        Description = description;
        Targets = _requestedDeltas
            .Select(delta => new EditorMutationTarget(RelativePath, JsonPath.Root.Append("data").Append(delta.Index)))
            .ToArray();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }
    public IReadOnlyList<TileDelta> Deltas => _capturedDeltas ?? _requestedDeltas;

    public async ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (_capturedDeltas is null)
        {
            _capturedDeltas = new TileDelta[_requestedDeltas.Length];
            for (int index = 0; index < _requestedDeltas.Length; index++)
            {
                TileDelta requested = _requestedDeltas[index];
                int before = document.GetInt32(JsonPath.Root.Append("data").Append(requested.Index))
                    ?? throw new InvalidDataException($"Map data index {requested.Index} is missing or is not an integer.");
                _capturedDeltas[index] = new TileDelta(requested.Index, before, requested.After);
            }
        }

        foreach (TileDelta delta in _capturedDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            document.SetInt32(JsonPath.Root.Append("data").Append(delta.Index), delta.After);
        }

    }

    public async ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (_capturedDeltas is null)
        {
            throw new InvalidOperationException("The map tile stroke has not been executed.");
        }

        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        foreach (TileDelta delta in _capturedDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            document.SetInt32(JsonPath.Root.Append("data").Append(delta.Index), delta.Before);
        }

    }
}

using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Mapping;

namespace Playroma.JRPG.Editing;

public sealed class MoveMapEventsCommand : IEditorCommand
{
    private readonly MapEventMove[] _moves;
    private MapEventMove[]? _captured;

    public MoveMapEventsCommand(string relativePath, IEnumerable<MapEventMove> moves, string description = "Move map events")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(moves);
        RelativePath = relativePath.Replace('\\', '/');
        _moves = moves.GroupBy(move => move.EventId).Select(group => group.Last()).OrderBy(move => move.EventId).ToArray();
        if (_moves.Length == 0) throw new ArgumentException("At least one event move is required.", nameof(moves));
        if (_moves.Any(move => move.EventId <= 0 || move.After.X < 0 || move.After.Y < 0)) throw new ArgumentOutOfRangeException(nameof(moves));
        Description = description;
        Targets = _moves.Select(move => new EditorMutationTarget(RelativePath, JsonPath.Root.Append("events").Append(move.EventId))).ToArray();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (_captured is null)
        {
            _captured = _moves.Select(move =>
            {
                JsonPath path = JsonPath.Root.Append("events").Append(move.EventId);
                int beforeX = document.GetInt32(path.Append("x")) ?? throw new InvalidDataException($"Event {move.EventId} has no X coordinate.");
                int beforeY = document.GetInt32(path.Append("y")) ?? throw new InvalidDataException($"Event {move.EventId} has no Y coordinate.");
                return move with { Before = new MapPoint(beforeX, beforeY) };
            }).ToArray();
        }
        Apply(document, _captured, useAfter: true);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_captured is null) throw new InvalidOperationException("The event move has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        Apply(document, _captured, useAfter: false);
    }

    private static void Apply(MutableJsonProjectDocument document, IEnumerable<MapEventMove> moves, bool useAfter)
    {
        foreach (MapEventMove move in moves)
        {
            MapPoint value = useAfter ? move.After : move.Before;
            JsonPath path = JsonPath.Root.Append("events").Append(move.EventId);
            document.SetInt32(path.Append("x"), value.X);
            document.SetInt32(path.Append("y"), value.Y);
        }
    }
}

public readonly record struct MapEventMove(int EventId, MapPoint Before, MapPoint After)
{
    public MapEventMove(int eventId, MapPoint after) : this(eventId, default, after) { }
}

public sealed class ReplaceMapEventsCommand : IEditorCommand
{
    private readonly byte[] _newEvents;
    private string? _oldEvents;

    public ReplaceMapEventsCommand(string relativePath, string rawEventsJson, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawEventsJson);
        RelativePath = relativePath.Replace('\\', '/');
        using JsonDocument parsed = JsonDocument.Parse(rawEventsJson);
        if (parsed.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Map events must be a JSON array.", nameof(rawEventsJson));
        _newEvents = JsonSerializer.SerializeToUtf8Bytes(parsed.RootElement);
        Description = description;
        Targets = [new EditorMutationTarget(RelativePath, JsonPath.Root.Append("events"))];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        _oldEvents ??= document.GetRawJson(JsonPath.Root.Append("events")) ?? "[null]";
        document.SetRawJson(JsonPath.Root.Append("events"), _newEvents);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (_oldEvents is null) throw new InvalidOperationException("The event replacement has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.SetRawJson(JsonPath.Root.Append("events"), System.Text.Encoding.UTF8.GetBytes(_oldEvents));
    }
}

using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public sealed record RemovedDatabaseSlot(int Id, bool IsPopulated, string RawJson, int UnknownPropertyCount);

public sealed record DatabaseMaximumChangePlan(
    MvDocumentKind Kind,
    string RelativePath,
    int CurrentMaximum,
    int RequestedMaximum,
    IReadOnlyList<int> AddedSlots,
    IReadOnlyList<RemovedDatabaseSlot> RemovedSlots,
    IReadOnlyList<ProjectReference> ExactReferences,
    IReadOnlyList<ProjectReference> HeuristicReferences)
{
    public bool IsExpansion => RequestedMaximum > CurrentMaximum;
    public bool IsReduction => RequestedMaximum < CurrentMaximum;
    public bool HasPopulatedRemovals => RemovedSlots.Any(slot => slot.IsPopulated);
    public bool HasBlockingReferences => ExactReferences.Count > 0;
    public bool CanApplySafely => !IsReduction || (!HasPopulatedRemovals && !HasBlockingReferences);
}

public sealed class DatabaseMaximumChangePlanner
{
    public DatabaseMaximumChangePlan CreatePlan(
        MutableJsonProjectDocument document,
        MvDocumentKind kind,
        int requestedMaximum,
        ProjectReferenceIndex? references = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (requestedMaximum < 0) throw new ArgumentOutOfRangeException(nameof(requestedMaximum));
        JsonArraySyntax root = document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        int current = Math.Max(0, root.Elements.Count - 1);
        int[] added = requestedMaximum > current ? Enumerable.Range(current + 1, requestedMaximum - current).ToArray() : [];
        List<RemovedDatabaseSlot> removed = [];
        List<ProjectReference> exact = [];
        List<ProjectReference> heuristic = [];
        if (requestedMaximum < current)
        {
            ReferenceTargetKind targetKind = ToReferenceTargetKind(kind);
            for (int id = requestedMaximum + 1; id <= current; id++)
            {
                JsonSyntaxNode node = root.Elements[id];
                string raw = document.GetRawJson(JsonPath.Root.Append(id)) ?? "null";
                int unknown = node is JsonObjectSyntax obj ? CountUnknownProperties(kind, obj) : 0;
                removed.Add(new RemovedDatabaseSlot(id, node.Kind == JsonSyntaxKind.Object, raw, unknown));
                if (references is not null && targetKind != ReferenceTargetKind.Unknown)
                {
                    IReadOnlyList<ProjectReference> inbound = references.FindInbound(new ReferenceTarget(targetKind, id));
                    exact.AddRange(inbound.Where(reference => reference.Confidence == ReferenceConfidence.Exact));
                    heuristic.AddRange(inbound.Where(reference => reference.Confidence == ReferenceConfidence.Heuristic));
                }
            }
        }
        return new DatabaseMaximumChangePlan(kind, document.RelativePath, current, requestedMaximum, added, removed, exact.Distinct().ToArray(), heuristic.Distinct().ToArray());
    }

    private static int CountUnknownProperties(MvDocumentKind kind, JsonObjectSyntax record)
    {
        HashSet<string> known = BuiltInDatabaseDefinitions.CreateRegistry().TryGet(kind, out IDatabaseEditorDefinition? definition)
            ? definition.RecordSchema.KnownTopLevelProperties.ToHashSet(StringComparer.Ordinal)
            : [];
        known.Add("id");
        return record.Properties.Count(property => !known.Contains(property.Name));
    }

    public static ReferenceTargetKind ToReferenceTargetKind(MvDocumentKind kind) => kind switch
    {
        MvDocumentKind.Actors => ReferenceTargetKind.Actor,
        MvDocumentKind.Classes => ReferenceTargetKind.Class,
        MvDocumentKind.Skills => ReferenceTargetKind.Skill,
        MvDocumentKind.Items => ReferenceTargetKind.Item,
        MvDocumentKind.Weapons => ReferenceTargetKind.Weapon,
        MvDocumentKind.Armors => ReferenceTargetKind.Armor,
        MvDocumentKind.Enemies => ReferenceTargetKind.Enemy,
        MvDocumentKind.Troops => ReferenceTargetKind.Troop,
        MvDocumentKind.States => ReferenceTargetKind.State,
        MvDocumentKind.Animations => ReferenceTargetKind.Animation,
        MvDocumentKind.Tilesets => ReferenceTargetKind.Tileset,
        MvDocumentKind.CommonEvents => ReferenceTargetKind.CommonEvent,
        _ => ReferenceTargetKind.Unknown
    };
}

public sealed class ResizeDatabaseArrayCommand : IEditorCommand
{
    private readonly int _requestedMaximum;
    private List<string>? _removedTail;
    private int _originalMaximum;
    private bool _captured;

    public ResizeDatabaseArrayCommand(string relativePath, int requestedMaximum, string? description = null)
    {
        if (requestedMaximum < 0) throw new ArgumentOutOfRangeException(nameof(requestedMaximum));
        RelativePath = relativePath.Replace('\\', '/');
        _requestedMaximum = requestedMaximum;
        Description = description ?? $"Change database maximum to {requestedMaximum}";
        Targets = [new EditorMutationTarget(RelativePath, JsonPath.Root)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        JsonArraySyntax root = document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        int currentMaximum = Math.Max(0, root.Elements.Count - 1);
        if (!_captured)
        {
            _originalMaximum = currentMaximum;
            _removedTail = [];
            if (_requestedMaximum < currentMaximum)
            {
                for (int id = _requestedMaximum + 1; id <= currentMaximum; id++)
                {
                    _removedTail.Add(document.GetRawJson(JsonPath.Root.Append(id)) ?? "null");
                }
            }
            _captured = true;
        }
        ApplyResize(document, currentMaximum, _requestedMaximum);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!_captured) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        JsonArraySyntax root = document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        int currentMaximum = Math.Max(0, root.Elements.Count - 1);
        if (_originalMaximum > currentMaximum && _removedTail is { Count: > 0 })
        {
            document.AppendArrayElements(JsonPath.Root, _removedTail.Select(raw => System.Text.Encoding.UTF8.GetBytes(raw)).ToArray());
        }
        else if (_originalMaximum < currentMaximum)
        {
            for (int index = currentMaximum; index > _originalMaximum; index--) document.RemoveArrayElement(JsonPath.Root, index);
        }
    }

    private static void ApplyResize(MutableJsonProjectDocument document, int currentMaximum, int targetMaximum)
    {
        if (targetMaximum > currentMaximum)
        {
            int count = targetMaximum - currentMaximum;
            document.AppendArrayElements(JsonPath.Root, Enumerable.Range(0, count).Select(_ => "null"u8.ToArray()).ToArray());
        }
        else if (targetMaximum < currentMaximum)
        {
            for (int index = currentMaximum; index > targetMaximum; index--) document.RemoveArrayElement(JsonPath.Root, index);
        }
    }
}

namespace Playroma.Sprite.Core;

public static class SpriteBlendModes
{
    public const string Normal = "normal";
    public const string Multiply = "multiply";
    public const string Screen = "screen";
    public const string Overlay = "overlay";
    public const string Darken = "darken";
    public const string Lighten = "lighten";
    public const string Add = "add";
    public const string Subtract = "subtract";
    public const string Difference = "difference";
    public const string HardLight = "hard-light";
    public const string SoftLight = "soft-light";

    public static IReadOnlyList<string> All { get; } =
    [Normal, Multiply, Screen, Overlay, Darken, Lighten, Add, Subtract, Difference, HardLight, SoftLight];

    public static bool IsSupported(string? value) => All.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}

public static class SpriteLayerTree
{
    public static IReadOnlyList<SpriteLayer> Children(SpriteDocument document, Guid? parentId) =>
        document.Layers.Where(layer => layer.ParentId == parentId).ToArray();

    public static SpriteLayer GetRequired(SpriteDocument document, Guid layerId) =>
        document.Layers.FirstOrDefault(layer => layer.Id == layerId) ?? throw new KeyNotFoundException($"Layer '{layerId}' does not exist.");

    public static int IndexOf(SpriteDocument document, Guid layerId) => document.Layers.FindIndex(layer => layer.Id == layerId);

    public static bool IsDescendant(SpriteDocument document, Guid possibleDescendantId, Guid ancestorId)
    {
        SpriteLayer? current = document.Layers.FirstOrDefault(layer => layer.Id == possibleDescendantId);
        HashSet<Guid> visited = [];
        while (current?.ParentId is Guid parent)
        {
            if (!visited.Add(parent)) return false;
            if (parent == ancestorId) return true;
            current = document.Layers.FirstOrDefault(layer => layer.Id == parent);
        }
        return false;
    }

    public static void Reparent(SpriteDocument document, Guid layerId, Guid? parentId)
    {
        SpriteLayer layer = GetRequired(document, layerId);
        if (parentId == layerId || (parentId is Guid parent && IsDescendant(document, parent, layerId)))
            throw new InvalidOperationException("A layer cannot be parented to itself or one of its descendants.");
        if (parentId is Guid requestedParent && GetRequired(document, requestedParent).Kind != SpriteLayerKind.Group)
            throw new InvalidOperationException("Only group layers can contain child layers.");
        layer.ParentId = parentId;
    }

    public static IReadOnlyList<Guid> SubtreeIds(SpriteDocument document, Guid layerId)
    {
        List<Guid> result = [];
        void Visit(Guid id)
        {
            result.Add(id);
            foreach (SpriteLayer child in document.Layers.Where(layer => layer.ParentId == id)) Visit(child.Id);
        }
        Visit(layerId);
        return result;
    }

    public static void Move(SpriteDocument document, Guid layerId, int targetIndex)
    {
        int source = IndexOf(document, layerId);
        if (source < 0) throw new KeyNotFoundException($"Layer '{layerId}' does not exist.");
        SpriteLayer layer = document.Layers[source];
        document.Layers.RemoveAt(source);
        targetIndex = Math.Clamp(targetIndex, 0, document.Layers.Count);
        document.Layers.Insert(targetIndex, layer);
    }


    public static bool MoveSubtreeAmongSiblings(SpriteDocument document, Guid layerId, int direction)
    {
        if (direction == 0) return false;
        SpriteLayer layer = GetRequired(document, layerId);
        List<SpriteLayer> siblings = document.Layers.Where(candidate => candidate.ParentId == layer.ParentId).ToList();
        int siblingIndex = siblings.FindIndex(candidate => candidate.Id == layerId);
        int targetSiblingIndex = siblingIndex + Math.Sign(direction);
        if (siblingIndex < 0 || targetSiblingIndex < 0 || targetSiblingIndex >= siblings.Count) return false;
        SpriteLayer target = siblings[targetSiblingIndex];
        HashSet<Guid> selectedIds = SubtreeIds(document, layerId).ToHashSet();
        HashSet<Guid> targetIds = SubtreeIds(document, target.Id).ToHashSet();
        List<SpriteLayer> selected = document.Layers.Where(candidate => selectedIds.Contains(candidate.Id)).ToList();
        document.Layers.RemoveAll(candidate => selectedIds.Contains(candidate.Id));
        int insertionIndex = direction > 0
            ? document.Layers.FindLastIndex(candidate => targetIds.Contains(candidate.Id)) + 1
            : document.Layers.FindIndex(candidate => candidate.Id == target.Id);
        if (insertionIndex < 0) insertionIndex = document.Layers.Count;
        document.Layers.InsertRange(insertionIndex, selected);
        return true;
    }

    public static IEnumerable<SpriteLayer> EnumeratePaintOrder(SpriteDocument document)
    {
        // The document list stores bottom-to-top paint order. Groups are metadata containers;
        // descendants retain their explicit list order so reordering remains deterministic.
        return document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group);
    }
}

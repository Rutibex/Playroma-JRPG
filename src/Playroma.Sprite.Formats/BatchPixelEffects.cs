using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public enum BatchEffectScope { ActiveCel, ActiveLayerAllFrames, SelectedFramesAllLayers, SelectedLayersAllFrames, EntireDocument }

public sealed record BatchEffectSelection(
    BatchEffectScope Scope,
    Guid ActiveLayerId,
    Guid ActiveFrameId,
    IReadOnlyCollection<Guid>? SelectedLayerIds = null,
    IReadOnlyCollection<Guid>? SelectedFrameIds = null,
    bool IncludeReferenceLayers = false,
    bool IncludeLockedLayers = false);

public static class BatchPixelEffects
{
    public static int Apply(SpritePackageData package, BatchEffectSelection selection, Action<PixelBuffer> effect)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(effect);
        HashSet<Guid> layerIds = ResolveLayers(package.Document, selection);
        HashSet<Guid> frameIds = ResolveFrames(package.Document, selection);
        HashSet<Guid> processedContent = [];
        foreach (SpriteLayer layer in package.Document.Layers)
        {
            if (!layerIds.Contains(layer.Id) || layer.Kind == SpriteLayerKind.Group || (!selection.IncludeReferenceLayers && layer.Kind == SpriteLayerKind.Reference) || (layer.Locked && !selection.IncludeLockedLayers)) continue;
            foreach (SpriteCel cel in layer.Cels)
            {
                if (!frameIds.Contains(cel.FrameId) || !processedContent.Add(cel.PixelContentId)) continue;
                effect(package.GetRequired(cel.PixelContentId));
            }
        }
        return processedContent.Count;
    }

    public static IReadOnlyList<Guid> ResolvePixelContentIds(SpritePackageData package, BatchEffectSelection selection)
    {
        HashSet<Guid> layerIds = ResolveLayers(package.Document, selection), frameIds = ResolveFrames(package.Document, selection), content = [];
        foreach (SpriteLayer layer in package.Document.Layers)
        {
            if (!layerIds.Contains(layer.Id) || layer.Kind == SpriteLayerKind.Group || (!selection.IncludeReferenceLayers && layer.Kind == SpriteLayerKind.Reference) || (layer.Locked && !selection.IncludeLockedLayers)) continue;
            foreach (SpriteCel cel in layer.Cels.Where(cel => frameIds.Contains(cel.FrameId))) content.Add(cel.PixelContentId);
        }
        return content.ToArray();
    }

    private static HashSet<Guid> ResolveLayers(SpriteDocument document, BatchEffectSelection selection)
    {
        HashSet<Guid> requested = selection.Scope switch
        {
            BatchEffectScope.ActiveCel or BatchEffectScope.ActiveLayerAllFrames => [selection.ActiveLayerId],
            BatchEffectScope.SelectedLayersAllFrames => (selection.SelectedLayerIds ?? [selection.ActiveLayerId]).ToHashSet(),
            _ => document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group).Select(layer => layer.Id).ToHashSet()
        };
        foreach (Guid groupId in requested.Where(id => document.Layers.FirstOrDefault(layer => layer.Id == id)?.Kind == SpriteLayerKind.Group).ToArray())
            foreach (SpriteLayer descendant in document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group && SpriteLayerTree.IsDescendant(document, layer.Id, groupId))) requested.Add(descendant.Id);
        return requested;
    }

    private static HashSet<Guid> ResolveFrames(SpriteDocument document, BatchEffectSelection selection) => selection.Scope switch
    {
        BatchEffectScope.ActiveCel => [selection.ActiveFrameId],
        BatchEffectScope.SelectedFramesAllLayers => (selection.SelectedFrameIds ?? [selection.ActiveFrameId]).ToHashSet(),
        _ => document.Frames.Select(frame => frame.Id).ToHashSet()
    };
}

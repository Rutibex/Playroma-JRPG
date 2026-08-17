using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Animation;

public enum FrameInsertionMode
{
    Empty,
    CopyIndependent,
    CopyLinked
}

public sealed record CelSelection(Guid LayerId, Guid FrameId);

public static class SpriteAnimationOperations
{
    public static SpriteFrame InsertFrame(SpritePackageData package, int insertionIndex, FrameInsertionMode mode, int? sourceFrameIndex = null, int durationMilliseconds = 100)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocument document = package.Document;
        if (insertionIndex < 0 || insertionIndex > document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(insertionIndex));
        if (durationMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));

        SpriteFrame? source = sourceFrameIndex.HasValue ? FrameAt(document, sourceFrameIndex.Value) : null;
        SpriteFrame frame = new() { Number = insertionIndex, DurationMilliseconds = source?.DurationMilliseconds ?? durationMilliseconds };
        document.Frames.Insert(insertionIndex, frame);
        ShiftSliceKeysForInsertion(document, insertionIndex);
        NormalizeFrameNumbers(document);

        foreach (SpriteLayer layer in document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group))
        {
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                SpriteTilemapCel? sourceTilemapCel = source is null ? null : layer.TilemapCels.FirstOrDefault(cel => cel.FrameId == source.Id);
                if (mode == FrameInsertionMode.Empty)
                {
                    if (layer.Continuous)
                    {
                        SpriteTilemapCel? previous = FindPreviousTilemapCel(document, layer, insertionIndex);
                        if (previous is not null) layer.TilemapCels.Add(CloneTilemapCel(previous, frame.Id, previous.TilemapContentId));
                    }
                    continue;
                }
                if (sourceTilemapCel is null) continue;
                Guid tilemapContentId = mode == FrameInsertionMode.CopyLinked ? sourceTilemapCel.TilemapContentId : CloneTilemapContent(document, sourceTilemapCel.TilemapContentId);
                layer.TilemapCels.Add(CloneTilemapCel(sourceTilemapCel, frame.Id, tilemapContentId));
                continue;
            }
            SpriteCel? sourceCel = source is null ? null : layer.Cels.FirstOrDefault(cel => cel.FrameId == source.Id);
            if (mode == FrameInsertionMode.Empty)
            {
                if (layer.Continuous)
                {
                    SpriteCel? previous = FindPreviousCel(document, layer, insertionIndex);
                    if (previous is not null) layer.Cels.Add(CloneCel(previous, frame.Id, previous.PixelContentId));
                }
                continue;
            }
            if (sourceCel is null) continue;
            Guid contentId = mode == FrameInsertionMode.CopyLinked
                ? sourceCel.PixelContentId
                : ClonePixelContent(package, sourceCel.PixelContentId);
            layer.Cels.Add(CloneCel(sourceCel, frame.Id, contentId));
        }
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return frame;
    }

    public static SpriteFrame DuplicateFrame(SpritePackageData package, int frameIndex, bool linked = false) =>
        InsertFrame(package, frameIndex + 1, linked ? FrameInsertionMode.CopyLinked : FrameInsertionMode.CopyIndependent, frameIndex);

    public static Guid RemoveFrames(SpritePackageData package, IReadOnlyCollection<int> frameIndexes)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (frameIndexes.Count == 0) throw new ArgumentException("At least one frame must be selected.", nameof(frameIndexes));
        SpriteDocument document = package.Document;
        int[] indexes = frameIndexes.Distinct().OrderBy(index => index).ToArray();
        if (indexes.Any(index => index < 0 || index >= document.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
        if (indexes.Length >= document.Frames.Count) throw new InvalidOperationException("An animation must retain at least one frame.");

        HashSet<Guid> removedIds = indexes.Select(index => FrameAt(document, index).Id).ToHashSet();
        UpdateSliceKeysForRemoval(document, indexes);
        document.Frames.RemoveAll(frame => removedIds.Contains(frame.Id));
        foreach (SpriteLayer layer in document.Layers)
        {
            layer.Cels.RemoveAll(cel => removedIds.Contains(cel.FrameId));
            layer.TilemapCels.RemoveAll(cel => removedIds.Contains(cel.FrameId));
        }
        NormalizeFrameNumbers(document);
        RemoveOrphanContents(package);
        ClampTags(document);
        int nextIndex = Math.Min(indexes[0], document.Frames.Count - 1);
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return document.Frames[nextIndex].Id;
    }

    public static void MoveFrames(SpritePackageData package, IReadOnlyCollection<int> frameIndexes, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocument document = package.Document;
        int[] indexes = frameIndexes.Distinct().OrderBy(index => index).ToArray();
        if (indexes.Length == 0) return;
        if (indexes.Any(index => index < 0 || index >= document.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
        if (destinationIndex < 0 || destinationIndex > document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));

        Dictionary<SpriteSliceKey, Guid> sliceKeyFrames = CaptureSliceKeyFrameIds(document);
        List<SpriteFrame> moving = indexes.Select(index => document.Frames[index]).ToList();
        document.Frames.RemoveAll(frame => moving.Contains(frame));
        int removedBefore = indexes.Count(index => index < destinationIndex);
        int adjusted = Math.Clamp(destinationIndex - removedBefore, 0, document.Frames.Count);
        document.Frames.InsertRange(adjusted, moving);
        NormalizeFrameNumbers(document);
        RemapSliceKeysByFrameIdentity(document, sliceKeyFrames);
        RemapTagsByFrameIdentity(document);
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void ReverseFrames(SpritePackageData package, int fromFrame, int toFrame)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocument document = package.Document;
        ValidateRange(document, fromFrame, toFrame);
        Dictionary<SpriteSliceKey, Guid> sliceKeyFrames = CaptureSliceKeyFrameIds(document);
        List<SpriteFrame> reversed = document.Frames.GetRange(fromFrame, toFrame - fromFrame + 1);
        reversed.Reverse();
        for (int index = 0; index < reversed.Count; index++) document.Frames[fromFrame + index] = reversed[index];
        NormalizeFrameNumbers(document);
        RemapSliceKeysByFrameIdentity(document, sliceKeyFrames);
        RemapTagsByFrameIdentity(document);
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void SetFrameDurations(SpriteDocument document, IEnumerable<int> frameIndexes, int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (durationMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        foreach (int index in frameIndexes.Distinct()) FrameAt(document, index).DurationMilliseconds = durationMilliseconds;
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void CopyCels(SpritePackageData package, IReadOnlyCollection<Guid> layerIds, IReadOnlyCollection<int> sourceFrameIndexes, int destinationStartIndex, bool linked, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocument document = package.Document;
        int[] sources = sourceFrameIndexes.Distinct().OrderBy(index => index).ToArray();
        if (sources.Length == 0 || layerIds.Count == 0) return;
        if (destinationStartIndex < 0 || destinationStartIndex + sources.Length > document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(destinationStartIndex));
        HashSet<Guid> wantedLayers = layerIds.ToHashSet();

        foreach (SpriteLayer layer in document.Layers.Where(layer => wantedLayers.Contains(layer.Id) && layer.Kind != SpriteLayerKind.Group))
        {
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                for (int offset = 0; offset < sources.Length; offset++)
                {
                    SpriteFrame sourceFrame = FrameAt(document, sources[offset]); SpriteFrame targetFrame = FrameAt(document, destinationStartIndex + offset);
                    SpriteTilemapCel? sourceCel = layer.TilemapCels.FirstOrDefault(cel => cel.FrameId == sourceFrame.Id); if (sourceCel is null) continue;
                    SpriteTilemapCel? target = layer.TilemapCels.FirstOrDefault(cel => cel.FrameId == targetFrame.Id); if (target is not null && !overwrite) continue; if (target is not null) layer.TilemapCels.Remove(target);
                    Guid contentId = linked ? sourceCel.TilemapContentId : CloneTilemapContent(document, sourceCel.TilemapContentId);
                    layer.TilemapCels.Add(CloneTilemapCel(sourceCel, targetFrame.Id, contentId));
                }
                continue;
            }
            for (int offset = 0; offset < sources.Length; offset++)
            {
                SpriteFrame sourceFrame = FrameAt(document, sources[offset]);
                SpriteFrame targetFrame = FrameAt(document, destinationStartIndex + offset);
                SpriteCel? sourceCel = layer.Cels.FirstOrDefault(cel => cel.FrameId == sourceFrame.Id);
                if (sourceCel is null) continue;
                SpriteCel? target = layer.Cels.FirstOrDefault(cel => cel.FrameId == targetFrame.Id);
                if (target is not null && !overwrite) continue;
                if (target is not null) layer.Cels.Remove(target);
                Guid contentId = linked ? sourceCel.PixelContentId : ClonePixelContent(package, sourceCel.PixelContentId);
                layer.Cels.Add(CloneCel(sourceCel, targetFrame.Id, contentId));
            }
        }
        RemoveOrphanContents(package);
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void MoveCels(SpritePackageData package, IReadOnlyCollection<Guid> layerIds, IReadOnlyCollection<int> sourceFrameIndexes, int destinationStartIndex, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocument document = package.Document;
        int[] sources = sourceFrameIndexes.Distinct().OrderBy(index => index).ToArray();
        if (sources.Length == 0 || layerIds.Count == 0) return;
        if (destinationStartIndex < 0 || destinationStartIndex + sources.Length > document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(destinationStartIndex));
        HashSet<Guid> wantedLayers = layerIds.ToHashSet();

        foreach (SpriteLayer layer in document.Layers.Where(layer => wantedLayers.Contains(layer.Id) && layer.Kind != SpriteLayerKind.Group))
        {
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                Dictionary<Guid, SpriteTilemapCel> movingTilemaps = [];
                for (int offset = 0; offset < sources.Length; offset++)
                {
                    SpriteFrame sourceFrame = FrameAt(document, sources[offset]); SpriteTilemapCel? cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == sourceFrame.Id); if (cel is not null) movingTilemaps[sourceFrame.Id] = cel;
                }
                foreach (SpriteTilemapCel cel in movingTilemaps.Values) layer.TilemapCels.Remove(cel);
                for (int offset = 0; offset < sources.Length; offset++)
                {
                    SpriteFrame sourceFrame = FrameAt(document, sources[offset]); if (!movingTilemaps.TryGetValue(sourceFrame.Id, out SpriteTilemapCel? cel)) continue;
                    SpriteFrame targetFrame = FrameAt(document, destinationStartIndex + offset); SpriteTilemapCel? target = layer.TilemapCels.FirstOrDefault(item => item.FrameId == targetFrame.Id);
                    if (target is not null && !overwrite) { layer.TilemapCels.Add(cel); continue; } if (target is not null) layer.TilemapCels.Remove(target); cel.FrameId = targetFrame.Id; layer.TilemapCels.Add(cel);
                }
                continue;
            }
            Dictionary<Guid, SpriteCel> moving = [];
            for (int offset = 0; offset < sources.Length; offset++)
            {
                SpriteFrame sourceFrame = FrameAt(document, sources[offset]);
                SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == sourceFrame.Id);
                if (cel is not null) moving[sourceFrame.Id] = cel;
            }
            foreach (SpriteCel cel in moving.Values) layer.Cels.Remove(cel);
            for (int offset = 0; offset < sources.Length; offset++)
            {
                SpriteFrame sourceFrame = FrameAt(document, sources[offset]);
                if (!moving.TryGetValue(sourceFrame.Id, out SpriteCel? cel)) continue;
                SpriteFrame targetFrame = FrameAt(document, destinationStartIndex + offset);
                SpriteCel? target = layer.Cels.FirstOrDefault(item => item.FrameId == targetFrame.Id);
                if (target is not null && !overwrite) { layer.Cels.Add(cel); continue; }
                if (target is not null) layer.Cels.Remove(target);
                cel.FrameId = targetFrame.Id;
                layer.Cels.Add(cel);
            }
        }
        RemoveOrphanContents(package);
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void LinkCels(SpritePackageData package, IReadOnlyCollection<CelSelection> selections, CelSelection? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(package); if (selections.Count < 2) return;
        CelSelection sourceSelection = anchor ?? selections.First(); SpriteLayer sourceLayer = SpriteLayerTree.GetRequired(package.Document, sourceSelection.LayerId);
        if (sourceLayer.Kind == SpriteLayerKind.Tilemap)
        {
            Guid contentId = GetRequiredTilemapCel(package.Document, sourceSelection).TilemapContentId;
            foreach (CelSelection selection in selections)
            {
                SpriteLayer layer = SpriteLayerTree.GetRequired(package.Document, selection.LayerId); if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("Image and tilemap cels cannot be linked together.");
                GetRequiredTilemapCel(package.Document, selection).TilemapContentId = contentId;
            }
        }
        else
        {
            Guid contentId = GetRequiredCel(package.Document, sourceSelection).PixelContentId;
            foreach (CelSelection selection in selections)
            {
                SpriteLayer layer = SpriteLayerTree.GetRequired(package.Document, selection.LayerId); if (layer.Kind == SpriteLayerKind.Tilemap) throw new InvalidOperationException("Image and tilemap cels cannot be linked together.");
                GetRequiredCel(package.Document, selection).PixelContentId = contentId;
            }
        }
        RemoveOrphanContents(package); SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    public static void UnlinkCels(SpritePackageData package, IReadOnlyCollection<CelSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(package);
        foreach (CelSelection selection in selections.Distinct())
        {
            SpriteLayer layer = SpriteLayerTree.GetRequired(package.Document, selection.LayerId);
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                SpriteTilemapCel cel = GetRequiredTilemapCel(package.Document, selection); cel.TilemapContentId = CloneTilemapContent(package.Document, cel.TilemapContentId);
            }
            else
            {
                SpriteCel cel = GetRequiredCel(package.Document, selection); cel.PixelContentId = ClonePixelContent(package, cel.PixelContentId);
            }
        }
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    public static void ClearCels(SpritePackageData package, IReadOnlyCollection<CelSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(package);
        foreach (IGrouping<Guid, CelSelection> layerGroup in selections.Distinct().GroupBy(selection => selection.LayerId))
        {
            SpriteLayer layer = SpriteLayerTree.GetRequired(package.Document, layerGroup.Key); HashSet<Guid> frames = layerGroup.Select(item => item.FrameId).ToHashSet();
            if (layer.Kind == SpriteLayerKind.Tilemap) layer.TilemapCels.RemoveAll(cel => frames.Contains(cel.FrameId)); else layer.Cels.RemoveAll(cel => frames.Contains(cel.FrameId));
        }
        RemoveOrphanContents(package); SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    public static void SetCelProperties(SpriteDocument document, IEnumerable<CelSelection> selections, IntPoint? position = null, byte? opacity = null, int? zIndex = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (CelSelection selection in selections.Distinct())
        {
            SpriteLayer layer = SpriteLayerTree.GetRequired(document, selection.LayerId);
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                SpriteTilemapCel cel = GetRequiredTilemapCel(document, selection); if (position.HasValue) cel.Position = position.Value; if (opacity.HasValue) cel.Opacity = opacity.Value; if (zIndex.HasValue) cel.ZIndex = zIndex.Value;
            }
            else
            {
                SpriteCel cel = GetRequiredCel(document, selection); if (position.HasValue) cel.Position = position.Value; if (opacity.HasValue) cel.Opacity = opacity.Value; if (zIndex.HasValue) cel.ZIndex = zIndex.Value;
            }
        }
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static bool AreLinked(SpriteDocument document, IEnumerable<CelSelection> selections)
    {
        CelSelection[] values = selections.Distinct().ToArray(); if (values.Length == 0) return false;
        SpriteLayer first = SpriteLayerTree.GetRequired(document, values[0].LayerId);
        if (first.Kind == SpriteLayerKind.Tilemap) return values.All(selection => SpriteLayerTree.GetRequired(document, selection.LayerId).Kind == SpriteLayerKind.Tilemap) && values.Select(selection => GetRequiredTilemapCel(document, selection).TilemapContentId).Distinct().Count() == 1;
        return values.All(selection => SpriteLayerTree.GetRequired(document, selection.LayerId).Kind != SpriteLayerKind.Tilemap) && values.Select(selection => GetRequiredCel(document, selection).PixelContentId).Distinct().Count() == 1;
    }

    public static IReadOnlyList<int> ResolveFrameRange(SpriteDocument document, int from, int to)
    {
        ValidateRange(document, from, to);
        return Enumerable.Range(from, to - from + 1).ToArray();
    }

    private static SpriteCel GetRequiredCel(SpriteDocument document, CelSelection selection)
    {
        SpriteLayer layer = SpriteLayerTree.GetRequired(document, selection.LayerId);
        return layer.Cels.FirstOrDefault(cel => cel.FrameId == selection.FrameId)
            ?? throw new InvalidOperationException($"Layer '{layer.Name}' has no cel in the selected frame.");
    }


    private static SpriteTilemapCel GetRequiredTilemapCel(SpriteDocument document, CelSelection selection)
    {
        SpriteLayer layer = SpriteLayerTree.GetRequired(document, selection.LayerId);
        return layer.TilemapCels.FirstOrDefault(cel => cel.FrameId == selection.FrameId)
            ?? throw new InvalidOperationException($"Layer '{layer.Name}' has no tilemap cel in the selected frame.");
    }

    private static SpriteCel CloneCel(SpriteCel source, Guid frameId, Guid pixelContentId) => new()
    {
        FrameId = frameId,
        PixelContentId = pixelContentId,
        Position = source.Position,
        Opacity = source.Opacity,
        ZIndex = source.ZIndex
    };

    private static SpriteTilemapCel CloneTilemapCel(SpriteTilemapCel source, Guid frameId, Guid contentId) => new()
    {
        FrameId = frameId, TilemapContentId = contentId, Position = source.Position, Opacity = source.Opacity, ZIndex = source.ZIndex
    };

    private static Guid CloneTilemapContent(SpriteDocument document, Guid sourceId)
    {
        SpriteTilemapContent source = document.TilemapContents.Single(item => item.Id == sourceId);
        SpriteTilemapContent clone = new()
        {
            Columns = source.Columns, Rows = source.Rows, EmptyTileIndex = source.EmptyTileIndex,
            Cells = source.Cells.Select(cell => new SpriteTileCell { TileIndex = cell.TileIndex, Transform = cell.Transform, Opacity = cell.Opacity }).ToList()
        };
        document.TilemapContents.Add(clone); return clone.Id;
    }

    private static Guid ClonePixelContent(SpritePackageData package, Guid sourceId)
    {
        SpritePixelContentDescriptor sourceDescriptor = package.Document.PixelContents.Single(item => item.Id == sourceId);
        SpritePixelContentDescriptor descriptor = new() { Width = sourceDescriptor.Width, Height = sourceDescriptor.Height, Encoding = sourceDescriptor.Encoding };
        package.Document.PixelContents.Add(descriptor);
        package.PixelBuffers[descriptor.Id] = package.GetRequired(sourceId).Clone();
        return descriptor.Id;
    }

    private static SpriteCel? FindPreviousCel(SpriteDocument document, SpriteLayer layer, int insertionIndex)
    {
        for (int index = insertionIndex - 1; index >= 0; index--)
        {
            Guid frameId = document.Frames[index].Id;
            SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == frameId);
            if (cel is not null) return cel;
        }
        return null;
    }

    private static SpriteTilemapCel? FindPreviousTilemapCel(SpriteDocument document, SpriteLayer layer, int insertionIndex)
    {
        for (int index = insertionIndex - 1; index >= 0; index--)
        {
            Guid frameId = document.Frames[index].Id; SpriteTilemapCel? cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId); if (cel is not null) return cel;
        }
        return null;
    }

    private static SpriteFrame FrameAt(SpriteDocument document, int index)
    {
        if (index < 0 || index >= document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return document.Frames[index];
    }

    private static void ShiftSliceKeysForInsertion(SpriteDocument document, int insertionIndex)
    {
        foreach (SpriteSlice slice in document.Slices)
            foreach (SpriteSliceKey key in slice.Keys.Where(key => key.FrameNumber >= insertionIndex))
                key.FrameNumber++;
    }

    private static void UpdateSliceKeysForRemoval(SpriteDocument document, IReadOnlyCollection<int> removedIndexes)
    {
        HashSet<int> removed = removedIndexes.ToHashSet();
        foreach (SpriteSlice slice in document.Slices)
        {
            SpriteSliceKey fallback = slice.Resolve(Math.Max(0, removedIndexes.Min() - 1));
            slice.Keys.RemoveAll(key => removed.Contains(key.FrameNumber));
            foreach (SpriteSliceKey key in slice.Keys)
                key.FrameNumber -= removedIndexes.Count(index => index < key.FrameNumber);
            if (slice.Keys.Count == 0)
            {
                fallback.FrameNumber = 0;
                slice.Keys.Add(fallback);
            }
            slice.Keys.Sort((left, right) => left.FrameNumber.CompareTo(right.FrameNumber));
            SpriteSliceKey first = slice.Keys[0];
            slice.Bounds = first.Bounds; slice.Pivot = first.Pivot; slice.Center = first.Center;
        }
    }


    private static Dictionary<SpriteSliceKey, Guid> CaptureSliceKeyFrameIds(SpriteDocument document)
    {
        Dictionary<SpriteSliceKey, Guid> result = [];
        foreach (SpriteSlice slice in document.Slices)
            foreach (SpriteSliceKey key in slice.Keys)
                if (key.FrameNumber >= 0 && key.FrameNumber < document.Frames.Count)
                    result[key] = document.Frames[key.FrameNumber].Id;
        return result;
    }

    private static void RemapSliceKeysByFrameIdentity(SpriteDocument document, IReadOnlyDictionary<SpriteSliceKey, Guid> frameIds)
    {
        Dictionary<Guid, int> positions = document.Frames.Select((frame, index) => (frame.Id, index)).ToDictionary(item => item.Id, item => item.index);
        foreach (SpriteSlice slice in document.Slices)
        {
            foreach (SpriteSliceKey key in slice.Keys)
                if (frameIds.TryGetValue(key, out Guid frameId) && positions.TryGetValue(frameId, out int frameNumber))
                    key.FrameNumber = frameNumber;
            slice.Keys.Sort((left, right) => left.FrameNumber.CompareTo(right.FrameNumber));
            SpriteSliceKey first = slice.Keys.Count > 0 ? slice.Keys[0] : slice.Resolve(0);
            slice.Bounds = first.Bounds; slice.Pivot = first.Pivot; slice.Center = first.Center;
        }
    }

    private static void NormalizeFrameNumbers(SpriteDocument document)
    {
        for (int index = 0; index < document.Frames.Count; index++) document.Frames[index].Number = index;
    }

    private static void ValidateRange(SpriteDocument document, int from, int to)
    {
        if (from < 0 || to < from || to >= document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(from));
    }

    private static void ClampTags(SpriteDocument document)
    {
        int maximum = document.Frames.Count - 1;
        foreach (SpriteAnimationTag tag in document.Tags)
        {
            tag.FromFrame = Math.Clamp(tag.FromFrame, 0, maximum);
            tag.ToFrame = Math.Clamp(tag.ToFrame, tag.FromFrame, maximum);
        }
    }

    private static void RemapTagsByFrameIdentity(SpriteDocument document)
    {
        // Tag ranges are positional authoring ranges. Reordering frames intentionally leaves
        // tags at the same timeline positions, matching common sprite-editor behavior.
        ClampTags(document);
    }

    private static void RemoveOrphanContents(SpritePackageData package)
    {
        HashSet<Guid> used = package.Document.Layers.SelectMany(layer => layer.Cels).Select(cel => cel.PixelContentId)
            .Concat(package.Document.Tilesets.SelectMany(tileset => tileset.Tiles).Where(tile => tile.PixelContentId.HasValue).Select(tile => tile.PixelContentId!.Value)).ToHashSet();
        foreach (Guid id in package.Document.PixelContents.Select(item => item.Id).Where(id => !used.Contains(id)).ToArray())
        {
            package.Document.PixelContents.RemoveAll(item => item.Id == id); package.PixelBuffers.Remove(id);
        }
        HashSet<Guid> usedMaps = package.Document.Layers.SelectMany(layer => layer.TilemapCels).Select(cel => cel.TilemapContentId).ToHashSet();
        package.Document.TilemapContents.RemoveAll(item => !usedMaps.Contains(item.Id));
    }
}

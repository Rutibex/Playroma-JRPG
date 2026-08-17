using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public static class SpritePackageOperations
{
    public static SpriteLayer AddLayer(SpritePackageData package, string name, SpriteLayerKind kind, Guid frameId, Guid? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (parentId is Guid parent && SpriteLayerTree.GetRequired(package.Document, parent).Kind != SpriteLayerKind.Group)
            throw new InvalidOperationException("Only group layers can contain children.");
        if (kind == SpriteLayerKind.Tilemap) throw new InvalidOperationException("Tilemap layers require a tileset and must be created through TilemapDocumentOperations.");
        SpriteLayer layer = new() { Name = name.Trim(), Kind = kind, ParentId = parentId };
        if (kind != SpriteLayerKind.Group)
        {
            SpritePixelContentDescriptor descriptor = new() { Width = package.Document.Canvas.Width, Height = package.Document.Canvas.Height };
            package.Document.PixelContents.Add(descriptor);
            PixelBuffer pixels = new(descriptor.Width, descriptor.Height);
            if (kind == SpriteLayerKind.Background) pixels.Clear(Rgba32.White);
            package.PixelBuffers[descriptor.Id] = pixels;
            layer.Cels.Add(new SpriteCel { FrameId = frameId, PixelContentId = descriptor.Id });
        }
        if (kind == SpriteLayerKind.Background)
        {
            int insertionIndex = parentId is Guid backgroundParent
                ? Math.Max(0, package.Document.Layers.FindIndex(candidate => candidate.Id == backgroundParent) + 1)
                : 0;
            package.Document.Layers.Insert(insertionIndex, layer);
        }
        else package.Document.Layers.Add(layer);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        return layer;
    }

    public static SpriteLayer DuplicateLayer(SpritePackageData package, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteLayer source = SpriteLayerTree.GetRequired(package.Document, layerId);
        IReadOnlyList<Guid> subtree = SpriteLayerTree.SubtreeIds(package.Document, source.Id);
        Dictionary<Guid, Guid> layerMap = [];
        Dictionary<Guid, Guid> contentMap = [];
        SpriteLayer? first = null;
        int insertionIndex = package.Document.Layers.FindLastIndex(layer => subtree.Contains(layer.Id)) + 1;
        foreach (Guid sourceId in subtree)
        {
            SpriteLayer original = SpriteLayerTree.GetRequired(package.Document, sourceId);
            SpriteLayer clone = new()
            {
                Name = ReferenceEquals(original, source) ? original.Name + " copy" : original.Name,
                Kind = original.Kind,
                ParentId = original.ParentId,
                Visible = original.Visible,
                Locked = original.Locked,
                Continuous = original.Continuous,
                Opacity = original.Opacity,
                BlendMode = original.BlendMode,
                TilesetId = original.TilesetId,
                TileEditMode = original.TileEditMode
            };
            layerMap[original.Id] = clone.Id;
            if (original.ParentId is Guid oldParent && layerMap.TryGetValue(oldParent, out Guid newParent)) clone.ParentId = newParent;
            foreach (SpriteCel cel in original.Cels)
            {
                if (!contentMap.TryGetValue(cel.PixelContentId, out Guid newContentId))
                {
                    SpritePixelContentDescriptor oldDescriptor = package.Document.PixelContents.Single(item => item.Id == cel.PixelContentId);
                    SpritePixelContentDescriptor descriptor = new() { Width = oldDescriptor.Width, Height = oldDescriptor.Height, Encoding = oldDescriptor.Encoding };
                    package.Document.PixelContents.Add(descriptor);
                    package.PixelBuffers[descriptor.Id] = package.GetRequired(cel.PixelContentId).Clone();
                    contentMap[cel.PixelContentId] = descriptor.Id;
                    newContentId = descriptor.Id;
                }
                clone.Cels.Add(new SpriteCel { FrameId = cel.FrameId, PixelContentId = newContentId, Position = cel.Position, Opacity = cel.Opacity, ZIndex = cel.ZIndex });
            }
            Dictionary<Guid, Guid> tilemapMap = [];
            foreach (SpriteTilemapCel cel in original.TilemapCels)
            {
                if (!tilemapMap.TryGetValue(cel.TilemapContentId, out Guid newTilemapContentId))
                {
                    SpriteTilemapContent sourceMap = package.Document.TilemapContents.Single(item => item.Id == cel.TilemapContentId);
                    SpriteTilemapContent copyMap = new()
                    {
                        Columns = sourceMap.Columns,
                        Rows = sourceMap.Rows,
                        EmptyTileIndex = sourceMap.EmptyTileIndex,
                        Cells = sourceMap.Cells.Select(cell => new SpriteTileCell { TileIndex = cell.TileIndex, Transform = cell.Transform, Opacity = cell.Opacity }).ToList()
                    };
                    package.Document.TilemapContents.Add(copyMap); tilemapMap[cel.TilemapContentId] = copyMap.Id; newTilemapContentId = copyMap.Id;
                }
                clone.TilemapCels.Add(new SpriteTilemapCel { FrameId = cel.FrameId, TilemapContentId = newTilemapContentId, Position = cel.Position, Opacity = cel.Opacity, ZIndex = cel.ZIndex });
            }
            package.Document.Layers.Insert(insertionIndex++, clone);
            first ??= clone;
        }
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        return first!;
    }

    public static Guid? RemoveLayer(SpritePackageData package, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(package);
        IReadOnlyList<Guid> subtree = SpriteLayerTree.SubtreeIds(package.Document, layerId);
        int firstIndex = package.Document.Layers.FindIndex(layer => subtree.Contains(layer.Id));
        List<Guid> contents = package.Document.Layers.Where(layer => subtree.Contains(layer.Id)).SelectMany(layer => layer.Cels).Select(cel => cel.PixelContentId).Distinct().ToList();
        package.Document.Layers.RemoveAll(layer => subtree.Contains(layer.Id));
        RemoveOrphanContents(package, contents);
        RemoveOrphanTilemapContents(package.Document);
        if (package.Document.Layers.Count == 0)
        {
            SpriteFrame frame = package.Document.Frames[0];
            return AddLayer(package, "Layer 1", SpriteLayerKind.Image, frame.Id).Id;
        }
        int target = Math.Clamp(firstIndex - 1, 0, package.Document.Layers.Count - 1);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        return package.Document.Layers[target].Id;
    }

    public static SpriteLayer MergeDown(SpritePackageData package, Guid topLayerId, Guid frameId)
    {
        SpriteDocument document = package.Document;
        int topIndex = document.Layers.FindIndex(layer => layer.Id == topLayerId);
        if (topIndex < 0) throw new KeyNotFoundException("The selected layer does not exist.");
        SpriteLayer top = document.Layers[topIndex];
        if (top.Kind == SpriteLayerKind.Group) throw new InvalidOperationException("Flatten a group before merging it.");
        if (top.Kind == SpriteLayerKind.Tilemap) throw new InvalidOperationException("Rasterize the tilemap layer before merging it.");
        int bottomIndex = -1;
        for (int index = topIndex - 1; index >= 0; index--)
        {
            SpriteLayer candidate = document.Layers[index];
            if (candidate.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap && candidate.ParentId == top.ParentId) { bottomIndex = index; break; }
        }
        if (bottomIndex < 0) throw new InvalidOperationException("There is no paint layer below the selected layer.");
        SpriteLayer bottom = document.Layers[bottomIndex];
        foreach (SpriteFrame frame in document.Frames)
        {
            SpriteCel? topCel = top.Cels.FirstOrDefault(item => item.FrameId == frame.Id);
            SpriteCel? bottomExisting = bottom.Cels.FirstOrDefault(item => item.FrameId == frame.Id);
            if (topCel is null && bottomExisting is null) continue;
            PixelBuffer flattened = CompositeSpecific(document, package.PixelBuffers, frame.Id, [bottom, top]);
            SpriteCel bottomCel = EnsureCel(package, bottom, frame.Id);
            SpritePixelContentDescriptor descriptor = document.PixelContents.Single(item => item.Id == bottomCel.PixelContentId);
            descriptor.Width = document.Canvas.Width; descriptor.Height = document.Canvas.Height;
            package.PixelBuffers[bottomCel.PixelContentId] = flattened;
            bottomCel.Position = default; bottomCel.Opacity = byte.MaxValue;
        }
        bottom.Opacity = byte.MaxValue; bottom.BlendMode = SpriteBlendModes.Normal; bottom.Visible = true;
        RemoveLayer(package, top.Id);
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return bottom;
    }

    public static SpriteLayer Flatten(SpritePackageData package, Guid frameId, bool includeReferenceLayers = false)
    {
        SpriteDocument document = package.Document;
        Dictionary<Guid, PixelBuffer> rendered = document.Frames.ToDictionary(
            frame => frame.Id,
            frame => LayerCompositor.Composite(document, package.PixelBuffers, frame.Id, includeReferenceLayers));
        package.PixelBuffers.Clear();
        document.PixelContents.Clear(); document.TilemapContents.Clear(); document.Tilesets.Clear(); document.Layers.Clear();
        SpriteLayer layer = new() { Name = "Flattened", Kind = SpriteLayerKind.Image };
        foreach (SpriteFrame frame in document.Frames)
        {
            SpritePixelContentDescriptor descriptor = new() { Width = document.Canvas.Width, Height = document.Canvas.Height };
            package.PixelBuffers[descriptor.Id] = rendered[frame.Id]; document.PixelContents.Add(descriptor);
            layer.Cels.Add(new SpriteCel { FrameId = frame.Id, PixelContentId = descriptor.Id });
        }
        document.Layers.Add(layer);
        SpriteDocumentValidator.ThrowIfInvalid(document);
        return layer;
    }

    public static void ResizeSprite(SpritePackageData package, int width, int height, PixelResamplingMode mode)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        double scaleX = width / (double)package.Document.Canvas.Width;
        double scaleY = height / (double)package.Document.Canvas.Height;
        foreach (SpritePixelContentDescriptor descriptor in package.Document.PixelContents)
        {
            PixelBuffer resized = PixelSelectionTransforms.Resize(package.GetRequired(descriptor.Id), Math.Max(1, (int)Math.Round(descriptor.Width * scaleX)), Math.Max(1, (int)Math.Round(descriptor.Height * scaleY)), mode);
            descriptor.Width = resized.Width; descriptor.Height = resized.Height; package.PixelBuffers[descriptor.Id] = resized;
        }
        foreach (SpriteCel cel in package.Document.Layers.SelectMany(layer => layer.Cels))
            cel.Position = new((int)Math.Round(cel.Position.X * scaleX), (int)Math.Round(cel.Position.Y * scaleY));
        foreach (SpriteTilemapCel cel in package.Document.Layers.SelectMany(layer => layer.TilemapCels))
            cel.Position = new((int)Math.Round(cel.Position.X * scaleX), (int)Math.Round(cel.Position.Y * scaleY));
        foreach (SpriteTileset tileset in package.Document.Tilesets)
        {
            tileset.TileWidth = Math.Max(1, (int)Math.Round(tileset.TileWidth * scaleX));
            tileset.TileHeight = Math.Max(1, (int)Math.Round(tileset.TileHeight * scaleY));
            tileset.Margin = Math.Max(0, (int)Math.Round(tileset.Margin * Math.Min(scaleX, scaleY)));
            tileset.Spacing = Math.Max(0, (int)Math.Round(tileset.Spacing * Math.Min(scaleX, scaleY)));
        }
        ScaleSlices(package.Document, scaleX, scaleY);
        package.Document.Canvas.Width = width; package.Document.Canvas.Height = height;
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    public static void ResizeCanvas(SpritePackageData package, IntRect sourceRegion)
    {
        if (sourceRegion.Width <= 0 || sourceRegion.Height <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRegion));
        SpriteDocument document = package.Document;
        RasterizeTilemapLayers(package);
        Dictionary<Guid, PixelBuffer> replacement = [];
        List<SpritePixelContentDescriptor> descriptors = [];
        foreach (SpriteLayer layer in document.Layers.Where(layer => layer.Kind != SpriteLayerKind.Group))
        {
            foreach (SpriteCel cel in layer.Cels)
            {
                PixelBuffer old = package.GetRequired(cel.PixelContentId);
                PixelBuffer canvas = new(document.Canvas.Width, document.Canvas.Height);
                LayerCompositor.CompositeLayer(canvas, old, cel.Position.X, cel.Position.Y, SpriteBlendModes.Normal, cel.Opacity);
                PixelBuffer cropped = PixelSelectionTransforms.Crop(canvas, sourceRegion);
                SpritePixelContentDescriptor descriptor = new() { Width = cropped.Width, Height = cropped.Height };
                descriptors.Add(descriptor); replacement[descriptor.Id] = cropped;
                cel.PixelContentId = descriptor.Id; cel.Position = default; cel.Opacity = byte.MaxValue;
            }
        }
        document.PixelContents = descriptors; package.PixelBuffers.Clear();
        foreach ((Guid id, PixelBuffer buffer) in replacement) package.PixelBuffers[id] = buffer;
        TranslateSlices(document, -sourceRegion.X, -sourceRegion.Y);
        document.Canvas.Width = sourceRegion.Width; document.Canvas.Height = sourceRegion.Height;
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static bool TrimTransparent(SpritePackageData package, Guid frameId)
    {
        PixelBuffer composite = LayerCompositor.Composite(package.Document, package.PixelBuffers, frameId);
        IntRect? bounds = PixelSelectionTransforms.FindOpaqueBounds([composite]);
        if (bounds is null) return false;
        if (bounds.Value.X == 0 && bounds.Value.Y == 0 && bounds.Value.Width == composite.Width && bounds.Value.Height == composite.Height) return false;
        ResizeCanvas(package, bounds.Value); return true;
    }

    private static PixelBuffer CompositeSpecific(SpriteDocument document, IReadOnlyDictionary<Guid, PixelBuffer> pixels, Guid frameId, IReadOnlyList<SpriteLayer> layers)
    {
        PixelBuffer result = new(document.Canvas.Width, document.Canvas.Height);
        foreach (SpriteLayer layer in layers)
        {
            if (!layer.Visible) continue;
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                SpriteTilemapCel? tilemapCel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId);
                if (tilemapCel is null) continue;
                PixelBuffer tilemap = TilemapRasterizer.Render(document, pixels, layer, tilemapCel);
                LayerCompositor.CompositeLayer(result, tilemap, tilemapCel.Position.X, tilemapCel.Position.Y, layer.BlendMode, (byte)(layer.Opacity * tilemapCel.Opacity / 255));
                continue;
            }
            SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == frameId);
            if (cel is null || !pixels.TryGetValue(cel.PixelContentId, out PixelBuffer? source)) continue;
            LayerCompositor.CompositeLayer(result, source, cel.Position.X, cel.Position.Y, layer.BlendMode, (byte)(layer.Opacity * cel.Opacity / 255));
        }
        return result;
    }

    private static SpriteCel EnsureCel(SpritePackageData package, SpriteLayer layer, Guid frameId)
    {
        SpriteCel? existing = layer.Cels.FirstOrDefault(cel => cel.FrameId == frameId);
        if (existing is not null) return existing;
        SpritePixelContentDescriptor descriptor = new() { Width = package.Document.Canvas.Width, Height = package.Document.Canvas.Height };
        package.Document.PixelContents.Add(descriptor); package.PixelBuffers[descriptor.Id] = new PixelBuffer(descriptor.Width, descriptor.Height);
        SpriteCel cel = new() { FrameId = frameId, PixelContentId = descriptor.Id }; layer.Cels.Add(cel); return cel;
    }

    private static void RemoveOrphanContents(SpritePackageData package, IEnumerable<Guid> candidates)
    {
        HashSet<Guid> used = package.Document.Layers.SelectMany(layer => layer.Cels).Select(cel => cel.PixelContentId).ToHashSet();
        foreach (Guid contentId in candidates.Distinct().Where(id => !used.Contains(id)).ToArray())
        {
            package.Document.PixelContents.RemoveAll(item => item.Id == contentId); package.PixelBuffers.Remove(contentId);
        }
    }

    private static void RasterizeTilemapLayers(SpritePackageData package)
    {
        SpriteDocument document = package.Document;
        foreach (SpriteLayer layer in document.Layers.Where(item => item.Kind == SpriteLayerKind.Tilemap).ToArray())
        {
            SpriteLayer image = new() { Name = layer.Name + " pixels", Kind = SpriteLayerKind.Image, ParentId = layer.ParentId, Visible = layer.Visible, Locked = layer.Locked, Opacity = layer.Opacity, BlendMode = layer.BlendMode };
            foreach (SpriteTilemapCel cel in layer.TilemapCels)
            {
                PixelBuffer rendered = TilemapRasterizer.Render(document, package.PixelBuffers, layer, cel);
                SpritePixelContentDescriptor descriptor = new() { Width = rendered.Width, Height = rendered.Height };
                document.PixelContents.Add(descriptor); package.PixelBuffers[descriptor.Id] = rendered;
                image.Cels.Add(new SpriteCel { FrameId = cel.FrameId, PixelContentId = descriptor.Id, Position = cel.Position, Opacity = cel.Opacity, ZIndex = cel.ZIndex });
            }
            int index = document.Layers.IndexOf(layer); document.Layers[index] = image;
        }
        document.TilemapContents.Clear(); document.Tilesets.Clear();
    }


    private static void ScaleSlices(SpriteDocument document, double scaleX, double scaleY)
    {
        foreach (SpriteSlice slice in document.Slices)
        {
            slice.Bounds = ScaleRect(slice.Bounds, scaleX, scaleY);
            slice.Pivot = slice.Pivot is IntPoint pivot ? ScalePoint(pivot, scaleX, scaleY) : null;
            slice.Center = slice.Center is IntRect center ? ScaleContainedRect(center, slice.Bounds, scaleX, scaleY) : null;
            foreach (SpriteSliceKey key in slice.Keys)
            {
                IntRect oldBounds = key.Bounds;
                key.Bounds = ScaleRect(oldBounds, scaleX, scaleY);
                key.Pivot = key.Pivot is IntPoint keyPivot ? ScalePoint(keyPivot, scaleX, scaleY) : null;
                key.Center = key.Center is IntRect keyCenter ? ScaleContainedRect(keyCenter, key.Bounds, scaleX, scaleY) : null;
            }
        }
    }

    private static void TranslateSlices(SpriteDocument document, int offsetX, int offsetY)
    {
        foreach (SpriteSlice slice in document.Slices)
        {
            slice.Bounds = TranslateRect(slice.Bounds, offsetX, offsetY);
            slice.Pivot = slice.Pivot is IntPoint pivot ? new IntPoint(checked(pivot.X + offsetX), checked(pivot.Y + offsetY)) : null;
            slice.Center = slice.Center is IntRect center ? TranslateRect(center, offsetX, offsetY) : null;
            foreach (SpriteSliceKey key in slice.Keys)
            {
                key.Bounds = TranslateRect(key.Bounds, offsetX, offsetY);
                key.Pivot = key.Pivot is IntPoint keyPivot ? new IntPoint(checked(keyPivot.X + offsetX), checked(keyPivot.Y + offsetY)) : null;
                key.Center = key.Center is IntRect keyCenter ? TranslateRect(keyCenter, offsetX, offsetY) : null;
            }
        }
    }

    private static IntRect ScaleRect(IntRect value, double scaleX, double scaleY) => new(
        (int)Math.Round(value.X * scaleX),
        (int)Math.Round(value.Y * scaleY),
        Math.Max(1, (int)Math.Round(value.Width * scaleX)),
        Math.Max(1, (int)Math.Round(value.Height * scaleY)));

    private static IntPoint ScalePoint(IntPoint value, double scaleX, double scaleY) => new(
        (int)Math.Round(value.X * scaleX),
        (int)Math.Round(value.Y * scaleY));

    private static IntRect ScaleContainedRect(IntRect value, IntRect scaledBounds, double scaleX, double scaleY)
    {
        IntRect scaled = ScaleRect(value, scaleX, scaleY);
        int x = Math.Clamp(scaled.X, scaledBounds.X, Math.Max(scaledBounds.X, scaledBounds.Right - 1));
        int y = Math.Clamp(scaled.Y, scaledBounds.Y, Math.Max(scaledBounds.Y, scaledBounds.Bottom - 1));
        int width = Math.Clamp(scaled.Width, 1, Math.Max(1, scaledBounds.Right - x));
        int height = Math.Clamp(scaled.Height, 1, Math.Max(1, scaledBounds.Bottom - y));
        return new IntRect(x, y, width, height);
    }

    private static IntRect TranslateRect(IntRect value, int offsetX, int offsetY) => new(
        checked(value.X + offsetX), checked(value.Y + offsetY), value.Width, value.Height);

    private static void RemoveOrphanTilemapContents(SpriteDocument document)
    {
        HashSet<Guid> used = document.Layers.SelectMany(layer => layer.TilemapCels).Select(cel => cel.TilemapContentId).ToHashSet();
        document.TilemapContents.RemoveAll(item => !used.Contains(item.Id));
    }

}

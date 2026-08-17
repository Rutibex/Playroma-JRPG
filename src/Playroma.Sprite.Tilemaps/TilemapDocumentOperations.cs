using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Tilemaps;

public sealed record TilesetImportOptions(
    string Name,
    int TileWidth = 48,
    int TileHeight = 48,
    int Margin = 0,
    int Spacing = 0,
    string? SourceImagePath = null,
    string? SourceTileStudioProjectPath = null,
    Guid? ExternalTilesetId = null,
    Guid? ReplaceTilesetId = null,
    bool TreatFullyTransparentTilesAsEmpty = true);

public sealed record TileBrush(IReadOnlyList<int> TileIndexes, SpriteTileTransform Transform = SpriteTileTransform.None, byte Opacity = byte.MaxValue)
{
    public int PrimaryTileIndex => TileIndexes.Count == 0 ? -1 : TileIndexes[0];
}

public static class TilemapDocumentOperations
{
    public static SpriteTileset ImportTileset(SpritePackageData package, PixelBuffer sheet, TilesetImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(sheet); ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        if (options.TileWidth <= 0 || options.TileHeight <= 0 || options.Margin < 0 || options.Spacing < 0) throw new ArgumentOutOfRangeException(nameof(options));
        int usableWidth = sheet.Width - options.Margin * 2, usableHeight = sheet.Height - options.Margin * 2;
        if (usableWidth <= 0 || usableHeight <= 0) throw new InvalidDataException("Tileset margins consume the entire source image.");
        int columns = (usableWidth + options.Spacing) / (options.TileWidth + options.Spacing);
        int rows = (usableHeight + options.Spacing) / (options.TileHeight + options.Spacing);
        if (columns <= 0 || rows <= 0 || options.Margin * 2 + columns * options.TileWidth + Math.Max(0, columns - 1) * options.Spacing != sheet.Width || options.Margin * 2 + rows * options.TileHeight + Math.Max(0, rows - 1) * options.Spacing != sheet.Height)
            throw new InvalidDataException("The source image does not divide exactly into the requested tile grid, margin, and spacing.");

        SpriteTileset? existing = options.ReplaceTilesetId is Guid replaceId ? package.Document.Tilesets.FirstOrDefault(item => item.Id == replaceId) : null;
        if (existing is null && options.ExternalTilesetId is Guid externalId)
            existing = package.Document.Tilesets.FirstOrDefault(item => item.ExternalTilesetId == externalId);
        if (existing is null && !string.IsNullOrWhiteSpace(options.SourceImagePath))
            existing = package.Document.Tilesets.FirstOrDefault(item => string.Equals(item.SourceImagePath, options.SourceImagePath, StringComparison.OrdinalIgnoreCase));

        SpriteTileset tileset = existing ?? new SpriteTileset();
        List<Guid> oldContentIds = tileset.Tiles.Where(tile => tile.PixelContentId.HasValue).Select(tile => tile.PixelContentId!.Value).ToList();
        tileset.Name = options.Name.Trim(); tileset.TileWidth = options.TileWidth; tileset.TileHeight = options.TileHeight;
        tileset.Columns = columns; tileset.Rows = rows; tileset.Margin = options.Margin; tileset.Spacing = options.Spacing;
        tileset.SourceImagePath = options.SourceImagePath; tileset.SourceTileStudioProjectPath = options.SourceTileStudioProjectPath;
        tileset.ExternalTilesetId = options.ExternalTilesetId; tileset.Revision = existing is null ? 1 : checked(existing.Revision + 1);
        tileset.Tiles.Clear();
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int x = options.Margin + column * (options.TileWidth + options.Spacing);
            int y = options.Margin + row * (options.TileHeight + options.Spacing);
            PixelBuffer tilePixels = Crop(sheet, x, y, options.TileWidth, options.TileHeight);
            bool empty = options.TreatFullyTransparentTilesAsEmpty && IsTransparent(tilePixels);
            SpriteTileDefinition tile = new() { Index = row * columns + column, Name = $"Tile {row * columns + column}", Empty = empty };
            if (!empty)
            {
                SpritePixelContentDescriptor descriptor = new() { Width = options.TileWidth, Height = options.TileHeight };
                package.Document.PixelContents.Add(descriptor); package.PixelBuffers[descriptor.Id] = tilePixels; tile.PixelContentId = descriptor.Id;
            }
            tileset.Tiles.Add(tile);
        }
        if (existing is null) package.Document.Tilesets.Add(tileset);
        RemoveUnusedPixelContents(package, oldContentIds);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        return tileset;
    }

    public static SpriteLayer AddTilemapLayer(SpritePackageData package, Guid tilesetId, Guid frameId, string name = "Tilemap", int? columns = null, int? rows = null, Guid? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteTileset tileset = package.Document.Tilesets.FirstOrDefault(item => item.Id == tilesetId) ?? throw new KeyNotFoundException("The tileset does not exist.");
        if (package.Document.Frames.All(frame => frame.Id != frameId)) throw new KeyNotFoundException("The frame does not exist.");
        int mapColumns = columns ?? Math.Max(1, (int)Math.Ceiling(package.Document.Canvas.Width / (double)tileset.TileWidth));
        int mapRows = rows ?? Math.Max(1, (int)Math.Ceiling(package.Document.Canvas.Height / (double)tileset.TileHeight));
        SpriteTilemapContent content = CreateContent(mapColumns, mapRows);
        SpriteLayer layer = new() { Name = string.IsNullOrWhiteSpace(name) ? "Tilemap" : name.Trim(), Kind = SpriteLayerKind.Tilemap, TilesetId = tileset.Id, ParentId = parentId };
        layer.TilemapCels.Add(new SpriteTilemapCel { FrameId = frameId, TilemapContentId = content.Id });
        package.Document.TilemapContents.Add(content); package.Document.Layers.Add(layer);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        return layer;
    }

    public static SpriteTilemapCel EnsureCel(SpritePackageData package, SpriteLayer layer, Guid frameId, bool linkedFromPrevious = false)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(layer);
        if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("The selected layer is not a tilemap layer.");
        SpriteTilemapCel? cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId);
        if (cel is not null) return cel;
        SpriteTilemapContent content;
        if (linkedFromPrevious)
        {
            int frameIndex = package.Document.Frames.FindIndex(item => item.Id == frameId);
            SpriteTilemapCel? previous = package.Document.Frames.Take(Math.Max(0, frameIndex)).Reverse().Select(frame => layer.TilemapCels.FirstOrDefault(item => item.FrameId == frame.Id)).FirstOrDefault(item => item is not null);
            if (previous is not null) content = GetContent(package.Document, previous.TilemapContentId);
            else content = CreateDefaultContent(package.Document, layer);
        }
        else content = CreateDefaultContent(package.Document, layer);
        if (package.Document.TilemapContents.All(item => item.Id != content.Id)) package.Document.TilemapContents.Add(content);
        cel = new SpriteTilemapCel { FrameId = frameId, TilemapContentId = content.Id }; layer.TilemapCels.Add(cel);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document); return cel;
    }

    public static void UnlinkCel(SpritePackageData package, SpriteLayer layer, Guid frameId)
    {
        SpriteTilemapCel cel = EnsureCel(package, layer, frameId, linkedFromPrevious: false);
        SpriteTilemapContent source = GetContent(package.Document, cel.TilemapContentId);
        SpriteTilemapContent clone = CloneContent(source); package.Document.TilemapContents.Add(clone); cel.TilemapContentId = clone.Id;
        RemoveOrphanTilemapContents(package.Document); SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    public static void Paint(SpriteDocument document, SpriteLayer layer, Guid frameId, int x, int y, TileBrush brush, SpriteTileEditMode mode)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(layer); ArgumentNullException.ThrowIfNull(brush);
        SpriteTilemapCel cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId) ?? throw new InvalidOperationException("The tilemap layer has no cel in this frame.");
        SpriteTilemapContent map = GetContent(document, cel.TilemapContentId); EnsureCoordinate(map, x, y);
        SpriteTileset tileset = document.Tilesets.First(item => item.Id == layer.TilesetId);
        if (mode == SpriteTileEditMode.Stack)
        {
            int[] stack = brush.TileIndexes.Count == 0 ? [-1] : brush.TileIndexes.ToArray();
            int current = map.GetCell(x, y).TileIndex; int currentIndex = Array.IndexOf(stack, current);
            int next = stack[(currentIndex + 1 + stack.Length) % stack.Length]; map.SetCell(x, y, NewCell(next, brush));
        }
        else
        {
            map.SetCell(x, y, NewCell(brush.PrimaryTileIndex, brush));
            if (mode == SpriteTileEditMode.Automatic) RecalculateAutomatic(document, tileset, map, x, y, brush.PrimaryTileIndex);
        }
    }

    public static void Erase(SpriteDocument document, SpriteLayer layer, Guid frameId, int x, int y)
    {
        SpriteTilemapContent map = ContentFor(document, layer, frameId); EnsureCoordinate(map, x, y);
        map.SetCell(x, y, new SpriteTileCell { TileIndex = map.EmptyTileIndex });
        if (layer.TileEditMode == SpriteTileEditMode.Automatic && layer.TilesetId is Guid id)
            RecalculateAutomatic(document, document.Tilesets.First(item => item.Id == id), map, x, y, -1);
    }

    public static void Fill(SpriteDocument document, SpriteLayer layer, Guid frameId, int startX, int startY, TileBrush brush)
    {
        SpriteTilemapContent map = ContentFor(document, layer, frameId); EnsureCoordinate(map, startX, startY);
        int target = map.GetCell(startX, startY).TileIndex, replacement = brush.PrimaryTileIndex; if (target == replacement) return;
        Queue<(int X, int Y)> queue = new(); queue.Enqueue((startX, startY)); bool[] visited = new bool[map.CellCount];
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue(); if (x < 0 || x >= map.Columns || y < 0 || y >= map.Rows) continue;
            int index = y * map.Columns + x; if (visited[index] || map.Cells[index].TileIndex != target) continue;
            visited[index] = true; map.Cells[index] = NewCell(replacement, brush);
            queue.Enqueue((x - 1, y)); queue.Enqueue((x + 1, y)); queue.Enqueue((x, y - 1)); queue.Enqueue((x, y + 1));
        }
    }

    public static void FillRectangle(SpriteDocument document, SpriteLayer layer, Guid frameId, IntRect region, TileBrush brush)
    {
        SpriteTilemapContent map = ContentFor(document, layer, frameId);
        int left = Math.Clamp(region.X, 0, map.Columns), top = Math.Clamp(region.Y, 0, map.Rows), right = Math.Clamp(region.Right, 0, map.Columns), bottom = Math.Clamp(region.Bottom, 0, map.Rows);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) map.SetCell(x, y, NewCell(brush.PrimaryTileIndex, brush));
    }

    public static void TransformRegion(SpriteDocument document, SpriteLayer layer, Guid frameId, IntRect region, SpriteTileTransform transform)
    {
        SpriteTilemapContent map = ContentFor(document, layer, frameId);
        int left = Math.Clamp(region.X, 0, map.Columns), top = Math.Clamp(region.Y, 0, map.Rows), right = Math.Clamp(region.Right, 0, map.Columns), bottom = Math.Clamp(region.Bottom, 0, map.Rows);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) map.GetCell(x, y).Transform ^= transform;
    }

    public static void RemapTiles(SpriteDocument document, Guid tilesetId, IReadOnlyDictionary<int, int> remap)
    {
        HashSet<Guid> layerIds = document.Layers.Where(layer => layer.Kind == SpriteLayerKind.Tilemap && layer.TilesetId == tilesetId).Select(layer => layer.Id).ToHashSet();
        HashSet<Guid> contentIds = document.Layers.Where(layer => layerIds.Contains(layer.Id)).SelectMany(layer => layer.TilemapCels).Select(cel => cel.TilemapContentId).ToHashSet();
        foreach (SpriteTilemapContent content in document.TilemapContents.Where(item => contentIds.Contains(item.Id)))
            foreach (SpriteTileCell cell in content.Cells) if (remap.TryGetValue(cell.TileIndex, out int replacement)) cell.TileIndex = replacement;
    }

    public static void ResizeMap(SpriteDocument document, SpriteLayer layer, Guid frameId, int columns, int rows, int sourceX = 0, int sourceY = 0)
    {
        if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        SpriteTilemapContent map = ContentFor(document, layer, frameId); List<SpriteTileCell> cells = Enumerable.Range(0, checked(columns * rows)).Select(_ => new SpriteTileCell { TileIndex = map.EmptyTileIndex }).ToList();
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            int oldX = x + sourceX, oldY = y + sourceY; if (oldX < 0 || oldX >= map.Columns || oldY < 0 || oldY >= map.Rows) continue;
            SpriteTileCell old = map.GetCell(oldX, oldY); cells[y * columns + x] = new SpriteTileCell { TileIndex = old.TileIndex, Transform = old.Transform, Opacity = old.Opacity };
        }
        map.Columns = columns; map.Rows = rows; map.Cells = cells;
    }

    public static SpriteLayer RasterizeToImageLayer(SpritePackageData package, SpriteLayer tilemapLayer, Guid frameId, string? name = null)
    {
        if (tilemapLayer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("The selected layer is not a tilemap layer.");
        SpriteTilemapCel cel = tilemapLayer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId) ?? throw new InvalidOperationException("The selected tilemap layer has no cel in this frame.");
        PixelBuffer rendered = TilemapRasterizer.Render(package.Document, package.PixelBuffers, tilemapLayer, cel);
        SpritePixelContentDescriptor descriptor = new() { Width = rendered.Width, Height = rendered.Height }; package.Document.PixelContents.Add(descriptor); package.PixelBuffers[descriptor.Id] = rendered;
        SpriteLayer layer = new() { Name = name ?? tilemapLayer.Name + " pixels", Kind = SpriteLayerKind.Image, ParentId = tilemapLayer.ParentId, Cels = [new SpriteCel { FrameId = frameId, PixelContentId = descriptor.Id, Position = cel.Position, Opacity = cel.Opacity, ZIndex = cel.ZIndex }] };
        int index = package.Document.Layers.IndexOf(tilemapLayer); package.Document.Layers.Insert(index + 1, layer); SpriteDocumentValidator.ThrowIfInvalid(package.Document); return layer;
    }

    public static SpriteTilemapContent ContentFor(SpriteDocument document, SpriteLayer layer, Guid frameId)
    {
        if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("The selected layer is not a tilemap layer.");
        SpriteTilemapCel cel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId) ?? throw new InvalidOperationException("The tilemap layer has no cel in this frame.");
        return GetContent(document, cel.TilemapContentId);
    }

    public static SpriteTilemapContent CreateContent(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        return new SpriteTilemapContent { Columns = columns, Rows = rows, Cells = Enumerable.Range(0, checked(columns * rows)).Select(_ => new SpriteTileCell()).ToList() };
    }

    private static SpriteTilemapContent CreateDefaultContent(SpriteDocument document, SpriteLayer layer)
    {
        SpriteTileset tileset = document.Tilesets.First(item => item.Id == layer.TilesetId);
        return CreateContent(Math.Max(1, (int)Math.Ceiling(document.Canvas.Width / (double)tileset.TileWidth)), Math.Max(1, (int)Math.Ceiling(document.Canvas.Height / (double)tileset.TileHeight)));
    }

    private static SpriteTilemapContent CloneContent(SpriteTilemapContent source) => new()
    {
        Columns = source.Columns, Rows = source.Rows, EmptyTileIndex = source.EmptyTileIndex,
        Cells = source.Cells.Select(cell => new SpriteTileCell { TileIndex = cell.TileIndex, Transform = cell.Transform, Opacity = cell.Opacity }).ToList()
    };

    private static SpriteTileCell NewCell(int tileIndex, TileBrush brush) => new() { TileIndex = tileIndex, Transform = brush.Transform, Opacity = brush.Opacity };
    private static SpriteTilemapContent GetContent(SpriteDocument document, Guid id) => document.TilemapContents.FirstOrDefault(item => item.Id == id) ?? throw new KeyNotFoundException("The tilemap content does not exist.");
    private static void EnsureCoordinate(SpriteTilemapContent map, int x, int y) { if (x < 0 || x >= map.Columns || y < 0 || y >= map.Rows) throw new ArgumentOutOfRangeException(nameof(x)); }

    private static void RecalculateAutomatic(SpriteDocument document, SpriteTileset tileset, SpriteTilemapContent map, int centerX, int centerY, int fallbackTile)
    {
        for (int y = centerY - 1; y <= centerY + 1; y++) for (int x = centerX - 1; x <= centerX + 1; x++)
        {
            if (x < 0 || x >= map.Columns || y < 0 || y >= map.Rows) continue;
            SpriteTileCell cell = map.GetCell(x, y); if (cell.TileIndex == map.EmptyTileIndex) continue;
            int mask = NeighborMask(map, x, y); SpriteAutoTileRule? rule = tileset.AutoRules.FirstOrDefault(item => item.NeighborMask == mask);
            if (rule is not null) { cell.TileIndex = rule.TileIndex; cell.Transform = rule.Transform; }
            else if (fallbackTile >= 0 && tileset.Tiles.Any(tile => tile.Index == fallbackTile)) cell.TileIndex = fallbackTile;
        }
    }

    private static int NeighborMask(SpriteTilemapContent map, int x, int y)
    {
        (int dx, int dy)[] directions = [(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)]; int mask = 0;
        for (int bit = 0; bit < directions.Length; bit++)
        {
            int nx = x + directions[bit].dx, ny = y + directions[bit].dy;
            if (nx >= 0 && nx < map.Columns && ny >= 0 && ny < map.Rows && map.GetCell(nx, ny).TileIndex != map.EmptyTileIndex) mask |= 1 << bit;
        }
        return mask;
    }

    private static PixelBuffer Crop(PixelBuffer source, int x, int y, int width, int height)
    {
        PixelBuffer result = new(width, height); for (int py = 0; py < height; py++) for (int px = 0; px < width; px++) result.SetPixel(px, py, source.GetPixel(x + px, y + py)); return result;
    }
    private static bool IsTransparent(PixelBuffer buffer) { for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++) if (buffer.GetPixel(x, y).A != 0) return false; return true; }

    private static void RemoveUnusedPixelContents(SpritePackageData package, IEnumerable<Guid> candidates)
    {
        HashSet<Guid> used = package.Document.Layers.SelectMany(layer => layer.Cels).Select(cel => cel.PixelContentId)
            .Concat(package.Document.Tilesets.SelectMany(tileset => tileset.Tiles).Where(tile => tile.PixelContentId.HasValue).Select(tile => tile.PixelContentId!.Value)).ToHashSet();
        foreach (Guid id in candidates.Distinct().Where(id => !used.Contains(id)).ToArray()) { package.Document.PixelContents.RemoveAll(item => item.Id == id); package.PixelBuffers.Remove(id); }
    }

    public static void RemoveOrphanTilemapContents(SpriteDocument document)
    {
        HashSet<Guid> used = document.Layers.SelectMany(layer => layer.TilemapCels).Select(cel => cel.TilemapContentId).ToHashSet(); document.TilemapContents.RemoveAll(item => !used.Contains(item.Id));
    }
}

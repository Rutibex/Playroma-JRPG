using Playroma.Sprite.Core;

namespace Playroma.Sprite.Pixel;

public static class TilemapRasterizer
{
    public static PixelBuffer Render(SpriteDocument document, IReadOnlyDictionary<Guid, PixelBuffer> pixels, SpriteLayer layer, SpriteTilemapCel cel)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(cel);
        if (layer.Kind != SpriteLayerKind.Tilemap) throw new ArgumentException("The layer is not a tilemap layer.", nameof(layer));
        SpriteTileset tileset = document.Tilesets.FirstOrDefault(item => item.Id == layer.TilesetId)
            ?? throw new InvalidDataException($"Tilemap layer '{layer.Name}' references a missing tileset.");
        SpriteTilemapContent map = document.TilemapContents.FirstOrDefault(item => item.Id == cel.TilemapContentId)
            ?? throw new InvalidDataException($"Tilemap layer '{layer.Name}' references missing tilemap content.");
        PixelBuffer result = new(checked(map.Columns * tileset.TileWidth), checked(map.Rows * tileset.TileHeight));
        result.Clear(Rgba32.Transparent);
        Dictionary<int, SpriteTileDefinition> definitions = tileset.Tiles.ToDictionary(tile => tile.Index);
        for (int y = 0; y < map.Rows; y++)
        for (int x = 0; x < map.Columns; x++)
        {
            SpriteTileCell cell = map.GetCell(x, y);
            if (cell.TileIndex == map.EmptyTileIndex || cell.Opacity == 0) continue;
            if (!definitions.TryGetValue(cell.TileIndex, out SpriteTileDefinition? definition) || definition.Empty || definition.PixelContentId is not Guid contentId) continue;
            if (!pixels.TryGetValue(contentId, out PixelBuffer? tile)) continue;
            PixelBuffer transformed = Transform(tile, cell.Transform, tileset.TileWidth, tileset.TileHeight);
            LayerCompositor.CompositeLayer(result, transformed, x * tileset.TileWidth, y * tileset.TileHeight, SpriteBlendModes.Normal, cell.Opacity);
        }
        return result;
    }

    public static PixelBuffer Transform(PixelBuffer source, SpriteTileTransform transform, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        PixelBuffer current = source;
        if ((transform & SpriteTileTransform.Rotate90) != 0) current = Rotate90(current);
        if ((transform & SpriteTileTransform.FlipHorizontal) != 0) current = FlipHorizontal(current);
        if ((transform & SpriteTileTransform.FlipVertical) != 0) current = FlipVertical(current);
        if (current.Width == targetWidth && current.Height == targetHeight) return current;
        PixelBuffer normalized = new(targetWidth, targetHeight);
        for (int y = 0; y < targetHeight; y++)
        for (int x = 0; x < targetWidth; x++)
            normalized.SetPixel(x, y, current.GetPixel(Math.Clamp(x, 0, current.Width - 1), Math.Clamp(y, 0, current.Height - 1)));
        return normalized;
    }

    private static PixelBuffer Rotate90(PixelBuffer source)
    {
        PixelBuffer result = new(source.Height, source.Width);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            result.SetPixel(source.Height - 1 - y, x, source.GetPixel(x, y));
        return result;
    }

    private static PixelBuffer FlipHorizontal(PixelBuffer source)
    {
        PixelBuffer result = new(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            result.SetPixel(source.Width - 1 - x, y, source.GetPixel(x, y));
        return result;
    }

    private static PixelBuffer FlipVertical(PixelBuffer source)
    {
        PixelBuffer result = new(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            result.SetPixel(x, source.Height - 1 - y, source.GetPixel(x, y));
        return result;
    }
}

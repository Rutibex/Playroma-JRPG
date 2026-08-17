using Playroma.Sprite.Core;

namespace Playroma.Sprite.Pixel;

public static class LayerCompositor
{
    public static PixelBuffer Composite(SpriteDocument document, IReadOnlyDictionary<Guid, PixelBuffer> pixels, Guid frameId, bool includeReferenceLayers = false)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(pixels);
        PixelBuffer result = new(document.Canvas.Width, document.Canvas.Height);
        result.Clear(Rgba32.Transparent);
        Dictionary<Guid, SpriteLayer> layers = document.Layers.ToDictionary(layer => layer.Id);
        foreach (SpriteLayer layer in SpriteLayerTree.EnumeratePaintOrder(document))
        {
            if (!IsEffectivelyVisible(layer, layers) || layer.Opacity == 0 || (layer.Kind == SpriteLayerKind.Reference && !includeReferenceLayers)) continue;
            if (layer.Kind == SpriteLayerKind.Tilemap)
            {
                SpriteTilemapCel? tilemapCel = layer.TilemapCels.FirstOrDefault(item => item.FrameId == frameId);
                if (tilemapCel is null || tilemapCel.Opacity == 0) continue;
                PixelBuffer tilemap = TilemapRasterizer.Render(document, pixels, layer, tilemapCel);
                CompositeLayer(result, tilemap, tilemapCel.Position.X, tilemapCel.Position.Y, layer.BlendMode, (byte)(layer.Opacity * tilemapCel.Opacity / 255));
                continue;
            }
            SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == frameId);
            if (cel is null || cel.Opacity == 0 || !pixels.TryGetValue(cel.PixelContentId, out PixelBuffer? source)) continue;
            CompositeLayer(result, source, cel.Position.X, cel.Position.Y, layer.BlendMode, (byte)(layer.Opacity * cel.Opacity / 255));
        }
        return result;
    }

    public static void CompositeLayer(PixelBuffer destination, PixelBuffer source, int offsetX, int offsetY, string? blendMode, byte opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(source);
        double opacityFactor = opacity / 255d;
        for (int sy = 0; sy < source.Height; sy++) for (int sx = 0; sx < source.Width; sx++)
        {
            int dx = sx + offsetX, dy = sy + offsetY;
            if (dx < 0 || dx >= destination.Width || dy < 0 || dy >= destination.Height) continue;
            Rgba32 src = source.GetPixel(sx, sy);
            if (src.A == 0) continue;
            Rgba32 dst = destination.GetPixel(dx, dy);
            destination.SetPixel(dx, dy, Blend(dst, src, blendMode, opacityFactor));
        }
    }

    public static Rgba32 Blend(Rgba32 destination, Rgba32 source, string? mode, double opacityFactor = 1d)
    {
        opacityFactor = Math.Clamp(opacityFactor, 0d, 1d);
        byte effectiveAlpha = (byte)Math.Clamp((int)Math.Round(source.A * opacityFactor), 0, 255);
        if (effectiveAlpha == 0) return destination;
        source = source.WithAlpha(effectiveAlpha);
        string normalized = mode?.Trim().ToLowerInvariant() ?? SpriteBlendModes.Normal;
        if (normalized == SpriteBlendModes.Normal) return PixelCompositor.AlphaComposite(destination, source);
        Rgba32 blended = new(
            BlendChannel(destination.R, source.R, normalized),
            BlendChannel(destination.G, source.G, normalized),
            BlendChannel(destination.B, source.B, normalized),
            source.A);
        return PixelCompositor.AlphaComposite(destination, blended);
    }

    private static byte BlendChannel(byte backdrop, byte source, string mode)
    {
        int b = backdrop, s = source;
        int value = mode switch
        {
            SpriteBlendModes.Multiply => b * s / 255,
            SpriteBlendModes.Screen => 255 - (255 - b) * (255 - s) / 255,
            SpriteBlendModes.Overlay => b < 128 ? 2 * b * s / 255 : 255 - 2 * (255 - b) * (255 - s) / 255,
            SpriteBlendModes.Darken => Math.Min(b, s),
            SpriteBlendModes.Lighten => Math.Max(b, s),
            SpriteBlendModes.Add => Math.Min(255, b + s),
            SpriteBlendModes.Subtract => Math.Max(0, b - s),
            SpriteBlendModes.Difference => Math.Abs(b - s),
            SpriteBlendModes.HardLight => s < 128 ? 2 * b * s / 255 : 255 - 2 * (255 - b) * (255 - s) / 255,
            SpriteBlendModes.SoftLight => SoftLight(b, s),
            _ => s
        };
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static int SoftLight(int backdrop, int source)
    {
        double b = backdrop / 255d, s = source / 255d;
        double value = s <= .5 ? b - (1 - 2 * s) * b * (1 - b) : b + (2 * s - 1) * (Math.Sqrt(b) - b);
        return (int)Math.Round(value * 255d);
    }

    private static bool IsEffectivelyVisible(SpriteLayer layer, IReadOnlyDictionary<Guid, SpriteLayer> layers)
    {
        if (!layer.Visible) return false;
        HashSet<Guid> visited = [];
        Guid? parent = layer.ParentId;
        while (parent is Guid id && layers.TryGetValue(id, out SpriteLayer? parentLayer))
        {
            if (!visited.Add(id) || !parentLayer.Visible) return false;
            parent = parentLayer.ParentId;
        }
        return true;
    }
}

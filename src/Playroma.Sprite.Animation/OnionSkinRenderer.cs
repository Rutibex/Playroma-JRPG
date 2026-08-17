using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Animation;

public sealed class OnionSkinSettings
{
    public bool Enabled { get; set; } = true;
    public int PreviousFrames { get; set; } = 1;
    public int NextFrames { get; set; } = 1;
    public byte BaseOpacity { get; set; } = 96;
    public Rgba32 PreviousTint { get; set; } = new(255, 80, 80, 255);
    public Rgba32 NextTint { get; set; } = new(80, 140, 255, 255);
    public bool TintFrames { get; set; } = true;
    public bool IncludeReferenceLayers { get; set; }
    public bool RestrictToTag { get; set; }
}

public static class OnionSkinRenderer
{
    public static PixelBuffer Render(SpritePackageData package, int activeFrameIndex, OnionSkinSettings settings, SpriteAnimationTag? activeTag = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(settings);
        SpriteDocument document = package.Document;
        if (activeFrameIndex < 0 || activeFrameIndex >= document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(activeFrameIndex));
        PixelBuffer result = new(document.Canvas.Width, document.Canvas.Height);
        if (!settings.Enabled) return result;

        int minimum = settings.RestrictToTag && activeTag is not null ? activeTag.FromFrame : 0;
        int maximum = settings.RestrictToTag && activeTag is not null ? activeTag.ToFrame : document.Frames.Count - 1;
        for (int distance = Math.Max(settings.PreviousFrames, settings.NextFrames); distance >= 1; distance--)
        {
            if (distance <= settings.PreviousFrames)
                CompositeGhost(package, result, activeFrameIndex - distance, distance, minimum, maximum, settings.PreviousTint, settings);
            if (distance <= settings.NextFrames)
                CompositeGhost(package, result, activeFrameIndex + distance, distance, minimum, maximum, settings.NextTint, settings);
        }
        return result;
    }

    public static PixelBuffer CompositeWithCurrent(SpritePackageData package, int activeFrameIndex, OnionSkinSettings settings, SpriteAnimationTag? activeTag = null)
    {
        PixelBuffer result = Render(package, activeFrameIndex, settings, activeTag);
        Guid frameId = package.Document.Frames[activeFrameIndex].Id;
        PixelBuffer current = LayerCompositor.Composite(package.Document, package.PixelBuffers, frameId, settings.IncludeReferenceLayers);
        LayerCompositor.CompositeLayer(result, current, 0, 0, SpriteBlendModes.Normal, byte.MaxValue);
        return result;
    }

    private static void CompositeGhost(SpritePackageData package, PixelBuffer destination, int frameIndex, int distance, int minimum, int maximum, Rgba32 tint, OnionSkinSettings settings)
    {
        if (frameIndex < minimum || frameIndex > maximum) return;
        Guid frameId = package.Document.Frames[frameIndex].Id;
        PixelBuffer frame = LayerCompositor.Composite(package.Document, package.PixelBuffers, frameId, settings.IncludeReferenceLayers);
        byte opacity = (byte)Math.Clamp(settings.BaseOpacity / Math.Max(1, distance), 1, 255);
        if (settings.TintFrames) frame = Tint(frame, tint);
        LayerCompositor.CompositeLayer(destination, frame, 0, 0, SpriteBlendModes.Normal, opacity);
    }

    private static PixelBuffer Tint(PixelBuffer source, Rgba32 tint)
    {
        PixelBuffer result = source.Clone();
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Rgba32 color = result.GetPixel(x, y);
                if (color.A == 0) continue;
                byte luminance = (byte)Math.Clamp((int)Math.Round(color.R * .2126 + color.G * .7152 + color.B * .0722), 0, 255);
                result.SetPixel(x, y, new Rgba32((byte)(tint.R * luminance / 255), (byte)(tint.G * luminance / 255), (byte)(tint.B * luminance / 255), color.A));
            }
        return result;
    }
}

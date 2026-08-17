using System.Text.Json;
using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Automation;

public sealed record AtlasFrameSource(string Name, int FrameIndex, PixelBuffer Pixels, int DurationMilliseconds);
public sealed record PackedAtlasFrame(string Name, int FrameIndex, IntRect Frame, IntRect Source, int DurationMilliseconds, bool Trimmed);
public sealed record SpriteAtlasResult(PixelBuffer Image, IReadOnlyList<PackedAtlasFrame> Frames, string MetadataJson);

public static class SpriteAtlasPacker
{
    public static SpriteAtlasResult Pack(SpritePackageData package, SpriteExportPreset preset, IReadOnlyCollection<int>? frameIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(preset);
        int[] indexes = frameIndexes?.Distinct().Order().ToArray() ?? Enumerable.Range(0, package.Document.Frames.Count).ToArray();
        if (indexes.Length == 0) throw new InvalidOperationException("An atlas requires at least one frame.");
        List<AtlasFrameSource> sources = indexes.Select(index =>
        {
            if (index < 0 || index >= package.Document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
            PixelBuffer rendered = AnimationFrameRenderer.RenderFrame(package, index);
            PixelBuffer scaled = preset.Scale <= 1 ? rendered : ScaleNearest(rendered, preset.Scale);
            return new AtlasFrameSource($"{AutomationVariables.Sanitize(package.Document.Name)}_{index:D4}", index, scaled, package.Document.Frames[index].DurationMilliseconds);
        }).ToList();
        if (preset.Ordering == AtlasOrdering.AreaDescending) sources = sources.OrderByDescending(item => item.Pixels.Width * item.Pixels.Height).ThenBy(item => item.FrameIndex).ToList();
        else if (preset.Ordering == AtlasOrdering.Name) sources = sources.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

        List<(AtlasFrameSource Source, IntRect SourceRect)> prepared = sources.Select(source => (source, preset.TrimTransparent ? FindOpaqueBounds(source.Pixels) : new IntRect(0, 0, source.Pixels.Width, source.Pixels.Height))).ToList();
        int maximumWidth = Math.Max(1, preset.MaximumWidth);
        int padding = Math.Max(0, preset.Padding);
        List<(AtlasFrameSource Source, IntRect SourceRect, IntRect Destination)> placements = [];
        int x = padding, y = padding, rowHeight = 0, usedWidth = 1, usedHeight = 1;
        foreach ((AtlasFrameSource source, IntRect sourceRect) in prepared)
        {
            int width = Math.Max(1, sourceRect.Width), height = Math.Max(1, sourceRect.Height);
            int occupiedWidth = width + padding * 2;
            if (x > padding && x + occupiedWidth > maximumWidth) { x = padding; y += rowHeight; rowHeight = 0; }
            if (x + occupiedWidth > maximumWidth) throw new InvalidOperationException($"Frame {source.Name} exceeds the atlas maximum width.");
            IntRect destination = new(x + padding, y + padding, width, height);
            placements.Add((source, sourceRect, destination));
            x += occupiedWidth; rowHeight = Math.Max(rowHeight, height + padding * 2);
            usedWidth = Math.Max(usedWidth, x); usedHeight = Math.Max(usedHeight, y + rowHeight);
        }
        int atlasWidth = preset.PowerOfTwo ? NextPowerOfTwo(usedWidth) : usedWidth;
        int atlasHeight = preset.PowerOfTwo ? NextPowerOfTwo(usedHeight) : usedHeight;
        if (atlasWidth > preset.MaximumWidth || atlasHeight > preset.MaximumHeight) throw new InvalidOperationException("Packed atlas exceeds the configured maximum dimensions.");
        PixelBuffer atlas = new(Math.Max(1, atlasWidth), Math.Max(1, atlasHeight));
        List<PackedAtlasFrame> frames = [];
        foreach ((AtlasFrameSource source, IntRect sourceRect, IntRect destination) in placements)
        {
            CopyRegion(source.Pixels, sourceRect, atlas, destination.X, destination.Y);
            if (preset.Extrude) Extrude(atlas, destination);
            frames.Add(new(source.Name, source.FrameIndex, destination, sourceRect, source.DurationMilliseconds, preset.TrimTransparent));
        }
        string metadata = BuildMetadata(package, atlas, frames, preset);
        return new(atlas, frames, metadata);
    }

    private static string BuildMetadata(SpritePackageData package, PixelBuffer atlas, IReadOnlyList<PackedAtlasFrame> frames, SpriteExportPreset preset)
    {
        object data = new
        {
            schema = "playroma-sprite-atlas/1",
            image = new { width = atlas.Width, height = atlas.Height, format = "png" },
            source = new { document = package.Document.Name, width = package.Document.Canvas.Width, height = package.Document.Canvas.Height },
            settings = new { preset.Padding, preset.Extrude, preset.PowerOfTwo, preset.Scale, ordering = preset.Ordering.ToString() },
            frames = frames.Select(frame => new
            {
                frame.Name,
                frame.FrameIndex,
                frame.DurationMilliseconds,
                frame.Trimmed,
                rect = new { frame.Frame.X, frame.Frame.Y, frame.Frame.Width, frame.Frame.Height },
                source = new { frame.Source.X, frame.Source.Y, frame.Source.Width, frame.Source.Height }
            })
        };
        return JsonSerializer.Serialize(data, AutomationJson.Options);
    }

    private static IntRect FindOpaqueBounds(PixelBuffer image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) if (image.GetPixel(x, y).A != 0)
        { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < left || bottom < top ? new IntRect(0, 0, 1, 1) : new IntRect(left, top, right - left + 1, bottom - top + 1);
    }

    private static PixelBuffer ScaleNearest(PixelBuffer source, int scale)
    {
        scale = Math.Clamp(scale, 1, 64); PixelBuffer result = new(checked(source.Width * scale), checked(source.Height * scale));
        for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x++) result.SetPixel(x, y, source.GetPixel(x / scale, y / scale));
        return result;
    }

    private static void CopyRegion(PixelBuffer source, IntRect rectangle, PixelBuffer destination, int xOffset, int yOffset)
    {
        for (int y = 0; y < rectangle.Height; y++) for (int x = 0; x < rectangle.Width; x++)
            destination.TrySetPixel(xOffset + x, yOffset + y, source.GetPixel(rectangle.X + x, rectangle.Y + y));
    }

    private static void Extrude(PixelBuffer image, IntRect rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        for (int x = 0; x < rectangle.Width; x++)
        {
            image.TrySetPixel(rectangle.X + x, rectangle.Y - 1, image.GetPixel(rectangle.X + x, rectangle.Y));
            image.TrySetPixel(rectangle.X + x, rectangle.Bottom, image.GetPixel(rectangle.X + x, rectangle.Bottom - 1));
        }
        for (int y = 0; y < rectangle.Height; y++)
        {
            image.TrySetPixel(rectangle.X - 1, rectangle.Y + y, image.GetPixel(rectangle.X, rectangle.Y + y));
            image.TrySetPixel(rectangle.Right, rectangle.Y + y, image.GetPixel(rectangle.Right - 1, rectangle.Y + y));
        }
        image.TrySetPixel(rectangle.X - 1, rectangle.Y - 1, image.GetPixel(rectangle.X, rectangle.Y));
        image.TrySetPixel(rectangle.Right, rectangle.Y - 1, image.GetPixel(rectangle.Right - 1, rectangle.Y));
        image.TrySetPixel(rectangle.X - 1, rectangle.Bottom, image.GetPixel(rectangle.X, rectangle.Bottom - 1));
        image.TrySetPixel(rectangle.Right, rectangle.Bottom, image.GetPixel(rectangle.Right - 1, rectangle.Bottom - 1));
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1; while (result < value && result < 1 << 30) result <<= 1; return result;
    }
}

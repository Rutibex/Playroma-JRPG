using System.Text.Json;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Tilemaps;

public static class SliceDocumentOperations
{
    public static SpriteSlice Add(SpriteDocument document, string name, IntRect bounds, int frameNumber = 0, IntPoint? pivot = null, IntRect? center = null)
    {
        ArgumentNullException.ThrowIfNull(document); ValidateKey(document, frameNumber, bounds, center);
        string root = string.IsNullOrWhiteSpace(name) ? "Slice" : name.Trim(); string candidate = root; int suffix = 2;
        while (document.Slices.Any(slice => string.Equals(slice.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = $"{root} {suffix++}";
        SpriteSlice slice = new() { Name = candidate, Bounds = bounds, Pivot = pivot, Center = center, Keys = [new SpriteSliceKey { FrameNumber = frameNumber, Bounds = bounds, Pivot = pivot, Center = center }] };
        document.Slices.Add(slice); SpriteDocumentValidator.ThrowIfInvalid(document); return slice;
    }

    public static void Remove(SpriteDocument document, Guid sliceId)
    {
        ArgumentNullException.ThrowIfNull(document); if (document.Slices.RemoveAll(slice => slice.Id == sliceId) == 0) throw new KeyNotFoundException("The slice does not exist.");
        SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static void Rename(SpriteDocument document, Guid sliceId, string name)
    {
        SpriteSlice slice = GetRequired(document, sliceId); slice.Name = string.IsNullOrWhiteSpace(name) ? "Slice" : name.Trim(); SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static SpriteSliceKey UpsertKey(SpriteDocument document, Guid sliceId, int frameNumber, IntRect bounds, IntPoint? pivot, IntRect? center)
    {
        ValidateKey(document, frameNumber, bounds, center); SpriteSlice slice = GetRequired(document, sliceId);
        SpriteSliceKey? key = slice.Keys.FirstOrDefault(item => item.FrameNumber == frameNumber);
        if (key is null) { key = new SpriteSliceKey { FrameNumber = frameNumber }; slice.Keys.Add(key); }
        key.Bounds = bounds; key.Pivot = pivot; key.Center = center; slice.Keys.Sort((left, right) => left.FrameNumber.CompareTo(right.FrameNumber));
        if (frameNumber == 0 || slice.Keys.Count == 1) { slice.Bounds = bounds; slice.Pivot = pivot; slice.Center = center; }
        SpriteDocumentValidator.ThrowIfInvalid(document); return key;
    }

    public static void RemoveKey(SpriteDocument document, Guid sliceId, int frameNumber)
    {
        SpriteSlice slice = GetRequired(document, sliceId); if (slice.Keys.Count <= 1) throw new InvalidOperationException("A slice must retain at least one key.");
        if (slice.Keys.RemoveAll(key => key.FrameNumber == frameNumber) == 0) throw new KeyNotFoundException("The slice has no key at that frame.");
        SpriteSliceKey first = slice.Keys.OrderBy(key => key.FrameNumber).First(); slice.Bounds = first.Bounds; slice.Pivot = first.Pivot; slice.Center = first.Center; SpriteDocumentValidator.ThrowIfInvalid(document);
    }

    public static SpriteSlice GetRequired(SpriteDocument document, Guid sliceId) => document.Slices.FirstOrDefault(slice => slice.Id == sliceId) ?? throw new KeyNotFoundException("The slice does not exist.");

    public static string ExportMetadata(SpriteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var payload = new
        {
            schema = "playroma-slices/1",
            document = document.Name,
            canvas = new { document.Canvas.Width, document.Canvas.Height },
            slices = document.Slices.Select(slice => new
            {
                id = slice.Id,
                slice.Name,
                keys = ResolveKeys(slice).Select(key => new
                {
                    frame = key.FrameNumber,
                    bounds = new { key.Bounds.X, key.Bounds.Y, key.Bounds.Width, key.Bounds.Height },
                    pivot = key.Pivot is IntPoint pivot ? new { pivot.X, pivot.Y } : null,
                    center = key.Center is IntRect center ? new { center.X, center.Y, center.Width, center.Height } : null
                }),
                userData = slice.UserData
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static IEnumerable<SpriteSliceKey> ResolveKeys(SpriteSlice slice) =>
        slice.Keys.Count == 0 ? new[] { slice.Resolve(0) } : slice.Keys;

    private static void ValidateKey(SpriteDocument document, int frameNumber, IntRect bounds, IntRect? center)
    {
        if (frameNumber < 0 || frameNumber >= document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameNumber));
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (center is IntRect value && (value.X < bounds.X || value.Y < bounds.Y || value.Right > bounds.Right || value.Bottom > bounds.Bottom)) throw new ArgumentException("The nine-slice center must be inside the slice bounds.", nameof(center));
    }
}

public static class NineSliceRenderer
{
    public static PixelBuffer Render(PixelBuffer source, SpriteSliceKey key, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source); if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        IntRect bounds = key.Bounds; if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentException("The slice has no drawable bounds.", nameof(key));
        IntRect center = key.Center ?? new IntRect(bounds.X + bounds.Width / 3, bounds.Y + bounds.Height / 3, Math.Max(1, bounds.Width / 3), Math.Max(1, bounds.Height / 3));
        int left = center.X - bounds.X, top = center.Y - bounds.Y, right = bounds.Right - center.Right, bottom = bounds.Bottom - center.Bottom;
        left = Math.Min(left, width); right = Math.Min(right, Math.Max(0, width - left)); top = Math.Min(top, height); bottom = Math.Min(bottom, Math.Max(0, height - top));
        PixelBuffer result = new(width, height); result.Clear(Rgba32.Transparent);
        CopyScaled(source, result, new(bounds.X, bounds.Y, left, top), new(0, 0, left, top));
        CopyScaled(source, result, new(center.X, bounds.Y, center.Width, top), new(left, 0, Math.Max(0, width - left - right), top));
        CopyScaled(source, result, new(center.Right, bounds.Y, right, top), new(width - right, 0, right, top));
        CopyScaled(source, result, new(bounds.X, center.Y, left, center.Height), new(0, top, left, Math.Max(0, height - top - bottom)));
        CopyScaled(source, result, center, new(left, top, Math.Max(0, width - left - right), Math.Max(0, height - top - bottom)));
        CopyScaled(source, result, new(center.Right, center.Y, right, center.Height), new(width - right, top, right, Math.Max(0, height - top - bottom)));
        CopyScaled(source, result, new(bounds.X, center.Bottom, left, bottom), new(0, height - bottom, left, bottom));
        CopyScaled(source, result, new(center.X, center.Bottom, center.Width, bottom), new(left, height - bottom, Math.Max(0, width - left - right), bottom));
        CopyScaled(source, result, new(center.Right, center.Bottom, right, bottom), new(width - right, height - bottom, right, bottom));
        return result;
    }

    private static void CopyScaled(PixelBuffer source, PixelBuffer destination, IntRect from, IntRect to)
    {
        if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0) return;
        for (int y = 0; y < to.Height; y++) for (int x = 0; x < to.Width; x++)
        {
            int sx = from.X + Math.Min(from.Width - 1, x * from.Width / to.Width); int sy = from.Y + Math.Min(from.Height - 1, y * from.Height / to.Height);
            int dx = to.X + x, dy = to.Y + y; if (sx < 0 || sx >= source.Width || sy < 0 || sy >= source.Height || dx < 0 || dx >= destination.Width || dy < 0 || dy >= destination.Height) continue;
            destination.SetPixel(dx, dy, source.GetPixel(sx, sy));
        }
    }
}

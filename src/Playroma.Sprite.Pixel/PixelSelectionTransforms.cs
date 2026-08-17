using Playroma.Sprite.Core;

namespace Playroma.Sprite.Pixel;

public enum PixelResamplingMode { NearestNeighbor, Bilinear }
public enum QuarterTurn { Clockwise90, HalfTurn, CounterClockwise90 }

public sealed record PixelClipboardPayload(PixelBuffer Pixels, SelectionMask Mask, IntPoint Origin)
{
    public PixelClipboardPayload DeepClone() => new(Pixels.Clone(), Mask.Clone(), Origin);
}

public static class PixelSelectionTransforms
{
    public static PixelClipboardPayload Copy(PixelBuffer source, SelectionMask selection)
    {
        Validate(source, selection);
        IntRect bounds = selection.GetBounds() ?? new IntRect(0, 0, 1, 1);
        PixelBuffer pixels = new(bounds.Width, bounds.Height);
        SelectionMask mask = new(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++) for (int x = 0; x < bounds.Width; x++)
        {
            int sx = bounds.X + x, sy = bounds.Y + y;
            if (!selection.Contains(sx, sy)) continue;
            pixels.SetPixel(x, y, source.GetPixel(sx, sy)); mask.Set(x, y, selection.Get(sx, sy));
        }
        return new(pixels, mask, new(bounds.X, bounds.Y));
    }

    public static PixelClipboardPayload Cut(PixelBuffer source, SelectionMask selection)
    {
        PixelClipboardPayload payload = Copy(source, selection);
        Clear(source, selection);
        return payload;
    }

    public static void Clear(PixelBuffer target, SelectionMask selection)
    {
        Validate(target, selection);
        for (int y = 0; y < target.Height; y++) for (int x = 0; x < target.Width; x++)
            if (selection.Contains(x, y)) target.SetPixel(x, y, Rgba32.Transparent);
    }

    public static SelectionMask Paste(PixelBuffer target, PixelClipboardPayload payload, IntPoint destination, bool mergeAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(payload);
        SelectionMask result = new(target.Width, target.Height);
        for (int y = 0; y < payload.Pixels.Height; y++) for (int x = 0; x < payload.Pixels.Width; x++)
        {
            if (!payload.Mask.Contains(x, y)) continue;
            int dx = destination.X + x, dy = destination.Y + y;
            if (dx < 0 || dx >= target.Width || dy < 0 || dy >= target.Height) continue;
            Rgba32 source = payload.Pixels.GetPixel(x, y);
            target.SetPixel(dx, dy, mergeAlpha ? PixelCompositor.AlphaComposite(target.GetPixel(dx, dy), source) : source);
            result.Set(dx, dy, payload.Mask.Get(x, y));
        }
        return result;
    }

    public static SelectionMask Move(PixelBuffer target, SelectionMask selection, int dx, int dy)
    {
        PixelClipboardPayload payload = Cut(target, selection);
        return Paste(target, payload, new(payload.Origin.X + dx, payload.Origin.Y + dy), false);
    }

    public static SelectionMask Flip(PixelBuffer target, SelectionMask selection, bool horizontal, bool vertical)
    {
        Validate(target, selection);
        IntRect? optional = selection.GetBounds(); if (optional is null) return selection.Clone();
        IntRect bounds = optional.Value;
        PixelClipboardPayload payload = Cut(target, selection);
        PixelBuffer transformed = new(payload.Pixels.Width, payload.Pixels.Height);
        SelectionMask mask = new(payload.Mask.Width, payload.Mask.Height);
        for (int y = 0; y < payload.Pixels.Height; y++) for (int x = 0; x < payload.Pixels.Width; x++)
        {
            int tx = horizontal ? payload.Pixels.Width - 1 - x : x;
            int ty = vertical ? payload.Pixels.Height - 1 - y : y;
            if (!payload.Mask.Contains(x, y)) continue;
            transformed.SetPixel(tx, ty, payload.Pixels.GetPixel(x, y)); mask.Set(tx, ty, payload.Mask.Get(x, y));
        }
        return Paste(target, new(transformed, mask, payload.Origin), new(bounds.X, bounds.Y), false);
    }

    public static SelectionMask RotateQuarter(PixelBuffer target, SelectionMask selection, QuarterTurn turn)
    {
        Validate(target, selection);
        PixelClipboardPayload payload = Cut(target, selection);
        bool quarter = turn != QuarterTurn.HalfTurn;
        int width = quarter ? payload.Pixels.Height : payload.Pixels.Width;
        int height = quarter ? payload.Pixels.Width : payload.Pixels.Height;
        PixelBuffer transformed = new(width, height);
        SelectionMask mask = new(width, height);
        for (int y = 0; y < payload.Pixels.Height; y++) for (int x = 0; x < payload.Pixels.Width; x++)
        {
            if (!payload.Mask.Contains(x, y)) continue;
            (int tx, int ty) = turn switch
            {
                QuarterTurn.Clockwise90 => (payload.Pixels.Height - 1 - y, x),
                QuarterTurn.CounterClockwise90 => (y, payload.Pixels.Width - 1 - x),
                _ => (payload.Pixels.Width - 1 - x, payload.Pixels.Height - 1 - y)
            };
            transformed.SetPixel(tx, ty, payload.Pixels.GetPixel(x, y)); mask.Set(tx, ty, payload.Mask.Get(x, y));
        }
        IntPoint destination = CenteredDestination(payload.Origin, payload.Pixels.Width, payload.Pixels.Height, width, height);
        return Paste(target, new(transformed, mask, destination), destination, false);
    }


    public static SelectionMask Rotate(PixelBuffer target, SelectionMask selection, double degrees, PixelResamplingMode mode = PixelResamplingMode.NearestNeighbor)
    {
        Validate(target, selection);
        PixelClipboardPayload payload = Cut(target, selection);
        double radians = degrees * Math.PI / 180d;
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        int width = Math.Max(1, (int)Math.Ceiling(Math.Abs(payload.Pixels.Width * cosine) + Math.Abs(payload.Pixels.Height * sine)));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Abs(payload.Pixels.Width * sine) + Math.Abs(payload.Pixels.Height * cosine)));
        PixelBuffer transformed = new(width, height);
        SelectionMask mask = new(width, height);
        double sourceCx = (payload.Pixels.Width - 1) / 2d, sourceCy = (payload.Pixels.Height - 1) / 2d;
        double targetCx = (width - 1) / 2d, targetCy = (height - 1) / 2d;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double dx = x - targetCx, dy = y - targetCy;
            double sx = cosine * dx + sine * dy + sourceCx;
            double sy = -sine * dx + cosine * dy + sourceCy;
            int nearestX = (int)Math.Round(sx), nearestY = (int)Math.Round(sy);
            if (nearestX < 0 || nearestX >= payload.Pixels.Width || nearestY < 0 || nearestY >= payload.Pixels.Height || !payload.Mask.Contains(nearestX, nearestY)) continue;
            Rgba32 color = mode == PixelResamplingMode.Bilinear ? Bilinear(payload.Pixels, sx, sy) : payload.Pixels.GetPixel(nearestX, nearestY);
            transformed.SetPixel(x, y, color); mask.Set(x, y, payload.Mask.Get(nearestX, nearestY));
        }
        IntPoint destination = CenteredDestination(payload.Origin, payload.Pixels.Width, payload.Pixels.Height, width, height);
        return Paste(target, new(transformed, mask, destination), destination, false);
    }

    public static SelectionMask Scale(PixelBuffer target, SelectionMask selection, IntRect destination, PixelResamplingMode mode = PixelResamplingMode.NearestNeighbor)
    {
        Validate(target, selection);
        if (destination.Width <= 0 || destination.Height <= 0) throw new ArgumentOutOfRangeException(nameof(destination));
        PixelClipboardPayload payload = Cut(target, selection);
        PixelBuffer scaled = Resize(payload.Pixels, destination.Width, destination.Height, mode);
        SelectionMask scaledMask = ResizeMask(payload.Mask, destination.Width, destination.Height);
        return Paste(target, new(scaled, scaledMask, new(destination.X, destination.Y)), new(destination.X, destination.Y), false);
    }

    public static PixelBuffer Resize(PixelBuffer source, int width, int height, PixelResamplingMode mode = PixelResamplingMode.NearestNeighbor)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        PixelBuffer result = new(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (mode == PixelResamplingMode.NearestNeighbor)
            {
                int sx = Math.Min(source.Width - 1, x * source.Width / width);
                int sy = Math.Min(source.Height - 1, y * source.Height / height);
                result.SetPixel(x, y, source.GetPixel(sx, sy));
            }
            else
            {
                double sx = (x + .5) * source.Width / width - .5;
                double sy = (y + .5) * source.Height / height - .5;
                result.SetPixel(x, y, Bilinear(source, sx, sy));
            }
        }
        return result;
    }

    public static PixelBuffer Crop(PixelBuffer source, IntRect region)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        PixelBuffer result = new(region.Width, region.Height);
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
            if (source.TryGetPixel(region.X + x, region.Y + y, out Rgba32 color)) result.SetPixel(x, y, color);
        return result;
    }

    public static IntRect? FindOpaqueBounds(IEnumerable<PixelBuffer> buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        foreach (PixelBuffer buffer in buffers)
        for (int y = 0; y < buffer.Height; y++) for (int x = 0; x < buffer.Width; x++)
        {
            if (buffer.GetPixel(x, y).A == 0) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right < left ? null : new IntRect(left, top, right - left + 1, bottom - top + 1);
    }

    private static SelectionMask ResizeMask(SelectionMask source, int width, int height)
    {
        SelectionMask result = new(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int sx = Math.Min(source.Width - 1, x * source.Width / width);
            int sy = Math.Min(source.Height - 1, y * source.Height / height);
            if (source.Contains(sx, sy)) result.Set(x, y, source.Get(sx, sy));
        }
        return result;
    }

    private static Rgba32 Bilinear(PixelBuffer source, double x, double y)
    {
        int x0 = Math.Clamp((int)Math.Floor(x), 0, source.Width - 1), y0 = Math.Clamp((int)Math.Floor(y), 0, source.Height - 1);
        int x1 = Math.Min(source.Width - 1, x0 + 1), y1 = Math.Min(source.Height - 1, y0 + 1);
        double tx = Math.Clamp(x - Math.Floor(x), 0d, 1d), ty = Math.Clamp(y - Math.Floor(y), 0d, 1d);
        Rgba32 a = source.GetPixel(x0, y0), b = source.GetPixel(x1, y0), c = source.GetPixel(x0, y1), d = source.GetPixel(x1, y1);
        byte Mix(byte aa, byte bb, byte cc, byte dd) => (byte)Math.Clamp((int)Math.Round((aa * (1 - tx) + bb * tx) * (1 - ty) + (cc * (1 - tx) + dd * tx) * ty), 0, 255);
        return new(Mix(a.R, b.R, c.R, d.R), Mix(a.G, b.G, c.G, d.G), Mix(a.B, b.B, c.B, d.B), Mix(a.A, b.A, c.A, d.A));
    }

    private static IntPoint CenteredDestination(IntPoint origin, int oldWidth, int oldHeight, int newWidth, int newHeight) =>
        new(origin.X + (oldWidth - newWidth) / 2, origin.Y + (oldHeight - newHeight) / 2);

    private static void Validate(PixelBuffer buffer, SelectionMask selection)
    {
        ArgumentNullException.ThrowIfNull(buffer); ArgumentNullException.ThrowIfNull(selection);
        if (buffer.Width != selection.Width || buffer.Height != selection.Height) throw new ArgumentException("Selection dimensions do not match the pixel buffer.", nameof(selection));
    }
}

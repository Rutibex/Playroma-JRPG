using Playroma.Sprite.Core;

namespace Playroma.Sprite.Pixel;

public enum SelectionCombineMode { Replace, Add, Subtract, Intersect }

public sealed class SelectionMask
{
    private readonly byte[] values;

    public SelectionMask(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        values = new byte[checked(width * height)];
    }

    private SelectionMask(int width, int height, byte[] values)
    {
        Width = width;
        Height = height;
        this.values = values;
    }

    public int Width { get; }
    public int Height { get; }
    public bool IsEmpty => Array.TrueForAll(values, value => value == 0);
    public bool IsFull => Array.TrueForAll(values, value => value == byte.MaxValue);
    public ReadOnlyMemory<byte> Values => values;

    public byte Get(int x, int y) => values[Offset(x, y)];
    public bool Contains(int x, int y) => Get(x, y) != 0;
    public void Set(int x, int y, byte value = byte.MaxValue) => values[Offset(x, y)] = value;
    public bool TrySet(int x, int y, byte value = byte.MaxValue)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
        Set(x, y, value);
        return true;
    }

    public void Clear() => Array.Clear(values);
    public void SelectAll() => Array.Fill(values, byte.MaxValue);
    public SelectionMask Clone() => new(Width, Height, (byte[])values.Clone());

    public void CopyFrom(SelectionMask source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureSameSize(source);
        source.values.CopyTo(values, 0);
    }

    public void Combine(SelectionMask incoming, SelectionCombineMode mode)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        EnsureSameSize(incoming);
        for (int index = 0; index < values.Length; index++)
        {
            byte a = values[index];
            byte b = incoming.values[index];
            values[index] = mode switch
            {
                SelectionCombineMode.Replace => b,
                SelectionCombineMode.Add => Math.Max(a, b),
                SelectionCombineMode.Subtract => (byte)(b == 0 ? a : 0),
                SelectionCombineMode.Intersect => Math.Min(a, b),
                _ => b
            };
        }
    }

    public void Invert()
    {
        for (int index = 0; index < values.Length; index++) values[index] = (byte)(255 - values[index]);
    }

    public IntRect? GetBounds()
    {
        int left = Width, top = Height, right = -1, bottom = -1;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (!Contains(x, y)) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right < left ? null : new IntRect(left, top, right - left + 1, bottom - top + 1);
    }

    public int CountSelected()
    {
        int count = 0;
        foreach (byte value in values) if (value != 0) count++;
        return count;
    }

    public static SelectionMask Rectangle(int width, int height, IntRect rectangle)
    {
        SelectionMask result = new(width, height);
        int left = Math.Clamp(rectangle.X, 0, width);
        int top = Math.Clamp(rectangle.Y, 0, height);
        int right = Math.Clamp(rectangle.Right, 0, width);
        int bottom = Math.Clamp(rectangle.Bottom, 0, height);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) result.Set(x, y);
        return result;
    }

    public static SelectionMask Ellipse(int width, int height, IntRect rectangle)
    {
        SelectionMask result = new(width, height);
        if (rectangle.Width == 0 || rectangle.Height == 0) return result;
        double cx = rectangle.X + (rectangle.Width - 1) / 2d;
        double cy = rectangle.Y + (rectangle.Height - 1) / 2d;
        double rx = Math.Max(.5, rectangle.Width / 2d);
        double ry = Math.Max(.5, rectangle.Height / 2d);
        int left = Math.Clamp(rectangle.X, 0, width);
        int top = Math.Clamp(rectangle.Y, 0, height);
        int right = Math.Clamp(rectangle.Right, 0, width);
        int bottom = Math.Clamp(rectangle.Bottom, 0, height);
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            double dx = (x - cx) / rx;
            double dy = (y - cy) / ry;
            if (dx * dx + dy * dy <= 1d) result.Set(x, y);
        }
        return result;
    }

    public static SelectionMask Polygon(int width, int height, IReadOnlyList<IntPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        SelectionMask result = new(width, height);
        if (points.Count < 3) return result;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                IntPoint a = points[i]; IntPoint b = points[j];
                bool crosses = (a.Y > y) != (b.Y > y) && x < (double)(b.X - a.X) * (y - a.Y) / (double)(b.Y - a.Y) + a.X;
                if (crosses) inside = !inside;
            }
            if (inside) result.Set(x, y);
        }
        return result;
    }

    public static SelectionMask FromOpaque(PixelBuffer source, byte alphaThreshold = 1)
    {
        ArgumentNullException.ThrowIfNull(source);
        SelectionMask result = new(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            if (source.GetPixel(x, y).A >= alphaThreshold) result.Set(x, y);
        return result;
    }

    public static SelectionMask MagicWand(PixelBuffer source, int startX, int startY, int tolerance, bool contiguous)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (startX < 0 || startX >= source.Width || startY < 0 || startY >= source.Height) return new(source.Width, source.Height);
        tolerance = Math.Clamp(tolerance, 0, 255);
        Rgba32 target = source.GetPixel(startX, startY);
        SelectionMask result = new(source.Width, source.Height);
        bool Matches(int x, int y) => source.GetPixel(x, y).ColorDistanceSquared(target) <= tolerance * tolerance * 4;
        if (!contiguous)
        {
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++) if (Matches(x, y)) result.Set(x, y);
            return result;
        }
        Queue<IntPoint> queue = new();
        queue.Enqueue(new(startX, startY)); result.Set(startX, startY);
        ReadOnlySpan<IntPoint> directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
        while (queue.Count > 0)
        {
            IntPoint point = queue.Dequeue();
            foreach (IntPoint direction in directions)
            {
                int x = point.X + direction.X, y = point.Y + direction.Y;
                if (x < 0 || x >= source.Width || y < 0 || y >= source.Height || result.Contains(x, y) || !Matches(x, y)) continue;
                result.Set(x, y); queue.Enqueue(new(x, y));
            }
        }
        return result;
    }

    public void Grow(int amount)
    {
        if (amount < 0) { Shrink(-amount); return; }
        for (int iteration = 0; iteration < amount; iteration++)
        {
            byte[] next = (byte[])values.Clone();
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                if (Contains(x, y)) continue;
                if (NeighborSelected(x - 1, y) || NeighborSelected(x + 1, y) || NeighborSelected(x, y - 1) || NeighborSelected(x, y + 1)) next[y * Width + x] = 255;
            }
            next.CopyTo(values, 0);
        }
    }

    public void Shrink(int amount)
    {
        if (amount < 0) { Grow(-amount); return; }
        for (int iteration = 0; iteration < amount; iteration++)
        {
            byte[] next = (byte[])values.Clone();
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                if (!Contains(x, y)) continue;
                if (!NeighborSelected(x - 1, y) || !NeighborSelected(x + 1, y) || !NeighborSelected(x, y - 1) || !NeighborSelected(x, y + 1)) next[y * Width + x] = 0;
            }
            next.CopyTo(values, 0);
        }
    }


    public void Feather(int radius)
    {
        if (radius <= 0) return;
        byte[] source = (byte[])values.Clone();
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            int sum = 0, count = 0;
            for (int oy = -radius; oy <= radius; oy++) for (int ox = -radius; ox <= radius; ox++)
            {
                int sx = x + ox, sy = y + oy;
                if (sx < 0 || sx >= Width || sy < 0 || sy >= Height) continue;
                sum += source[sy * Width + sx]; count++;
            }
            values[y * Width + x] = count == 0 ? (byte)0 : (byte)(sum / count);
        }
    }

    public SelectionMask Border(int thickness)
    {
        thickness = Math.Max(1, thickness);
        SelectionMask inner = Clone(); inner.Shrink(thickness);
        SelectionMask result = Clone(); result.Combine(inner, SelectionCombineMode.Subtract);
        return result;
    }

    public SelectionMask Translate(int dx, int dy)
    {
        SelectionMask result = new(Width, Height);
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            if (Contains(x, y)) result.TrySet(x + dx, y + dy, Get(x, y));
        return result;
    }

    private bool NeighborSelected(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && Contains(x, y);
    private int Offset(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return checked(y * Width + x);
    }
    private void EnsureSameSize(SelectionMask other)
    {
        if (other.Width != Width || other.Height != Height) throw new ArgumentException("Selection dimensions do not match.", nameof(other));
    }
}

namespace Playroma.JRPG.Mapping;

public static class MapDrawingAlgorithms
{
    public static IReadOnlyList<MapPoint> Line(MapPoint start, MapPoint end)
    {
        List<MapPoint> points = [];
        int x = start.X;
        int y = start.Y;
        int dx = Math.Abs(end.X - start.X);
        int sx = start.X < end.X ? 1 : -1;
        int dy = -Math.Abs(end.Y - start.Y);
        int sy = start.Y < end.Y ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            points.Add(new MapPoint(x, y));
            if (x == end.X && y == end.Y) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
        }
        return points;
    }

    public static IReadOnlyList<MapPoint> Rectangle(MapPoint start, MapPoint end, bool filled)
    {
        MapRect bounds = MapRect.FromPoints(start, end);
        if (filled) return bounds.Cells().ToArray();
        HashSet<MapPoint> result = [];
        for (int x = bounds.Left; x < bounds.Right; x++)
        {
            result.Add(new MapPoint(x, bounds.Top));
            result.Add(new MapPoint(x, bounds.Bottom - 1));
        }
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            result.Add(new MapPoint(bounds.Left, y));
            result.Add(new MapPoint(bounds.Right - 1, y));
        }
        return result.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    public static IReadOnlyList<MapPoint> Ellipse(MapPoint start, MapPoint end, bool filled)
    {
        MapRect bounds = MapRect.FromPoints(start, end);
        if (bounds.Width == 1 || bounds.Height == 1) return Line(start, end);
        double rx = (bounds.Width - 1) / 2d;
        double ry = (bounds.Height - 1) / 2d;
        double cx = bounds.Left + rx;
        double cy = bounds.Top + ry;
        HashSet<MapPoint> result = [];
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                double nx = (x - cx) / rx;
                double ny = (y - cy) / ry;
                double distance = nx * nx + ny * ny;
                if (filled)
                {
                    if (distance <= 1.05d) result.Add(new MapPoint(x, y));
                }
                else if (distance is >= 0.70d and <= 1.30d)
                {
                    result.Add(new MapPoint(x, y));
                }
            }
        }
        return result.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    public static IReadOnlyList<MapPoint> FloodFill(
        IReadOnlyMapBuffer map,
        MapPoint start,
        int layer,
        Func<int, bool> matches,
        int maximumCells = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(matches);
        if (!map.IsInside(start.X, start.Y)) return [];
        if (!matches(map.Get(start.X, start.Y, layer))) return [];
        Queue<MapPoint> pending = new();
        HashSet<MapPoint> visited = [];
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            MapPoint point = pending.Dequeue();
            if (!visited.Add(point)) continue;
            if (!map.IsInside(point.X, point.Y) || !matches(map.Get(point.X, point.Y, layer))) continue;
            if (visited.Count > maximumCells) throw new InvalidOperationException("Flood fill exceeded the configured safety limit.");
            pending.Enqueue(new MapPoint(point.X, point.Y - 1));
            pending.Enqueue(new MapPoint(point.X + 1, point.Y));
            pending.Enqueue(new MapPoint(point.X, point.Y + 1));
            pending.Enqueue(new MapPoint(point.X - 1, point.Y));
        }
        return visited.Where(point => map.IsInside(point.X, point.Y) && matches(map.Get(point.X, point.Y, layer))).ToArray();
    }
}

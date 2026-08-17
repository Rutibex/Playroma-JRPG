namespace Playroma.JRPG.Mapping;

public sealed class TileStamp
{
    private readonly int[] _values;

    public TileStamp(int width, int height, int layerCount, IEnumerable<int> values, MapPoint? anchor = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (layerCount <= 0) throw new ArgumentOutOfRangeException(nameof(layerCount));
        ArgumentNullException.ThrowIfNull(values);
        Width = width;
        Height = height;
        LayerCount = layerCount;
        _values = values.ToArray();
        if (_values.Length != checked(width * height * layerCount)) throw new ArgumentException("The stamp data length is invalid.", nameof(values));
        Anchor = anchor ?? new MapPoint(0, 0);
        if (Anchor.X < 0 || Anchor.X >= width || Anchor.Y < 0 || Anchor.Y >= height) throw new ArgumentOutOfRangeException(nameof(anchor));
    }

    public int Width { get; }
    public int Height { get; }
    public int LayerCount { get; }
    public MapPoint Anchor { get; }
    public MapSize Size => new(Width, Height);

    public int Get(int x, int y, int layer)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || layer < 0 || layer >= LayerCount) throw new ArgumentOutOfRangeException(nameof(x));
        return _values[checked((layer * Height + y) * Width + x)];
    }

    public static TileStamp Single(int value, int layer = 0)
    {
        int[] values = new int[layer + 1];
        values[layer] = value;
        return new TileStamp(1, 1, values.Length, values);
    }
}

public sealed class MapBrushEngine
{
    private readonly AutotileTopologyResolver _autotiles = new();

    public void PreviewStamp(
        EditableMapBuffer map,
        TileStamp stamp,
        MapPoint destination,
        int destinationLayer,
        bool reshapeAutotiles = true)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stamp);
        HashSet<MapPoint> affected = [];
        for (int sourceLayer = 0; sourceLayer < stamp.LayerCount; sourceLayer++)
        {
            int targetLayer = destinationLayer + sourceLayer;
            if (targetLayer < 0 || targetLayer >= map.LayerCount) continue;
            for (int y = 0; y < stamp.Height; y++)
            {
                for (int x = 0; x < stamp.Width; x++)
                {
                    int targetX = destination.X + x - stamp.Anchor.X;
                    int targetY = destination.Y + y - stamp.Anchor.Y;
                    if (!map.IsInside(targetX, targetY)) continue;
                    map.PreviewSet(targetX, targetY, targetLayer, stamp.Get(x, y, sourceLayer));
                    affected.Add(new MapPoint(targetX, targetY));
                }
            }
            if (reshapeAutotiles)
            {
                HashSet<MapPoint> reshape = [];
                foreach (MapPoint point in affected)
                {
                    for (int yy = point.Y - 1; yy <= point.Y + 1; yy++)
                    {
                        for (int xx = point.X - 1; xx <= point.X + 1; xx++)
                        {
                            if (map.IsInside(xx, yy)) reshape.Add(new MapPoint(xx, yy));
                        }
                    }
                }
                _autotiles.Reshape(map, reshape, targetLayer);
            }
        }
    }


    public void PreviewValues(
        EditableMapBuffer map,
        IEnumerable<(MapPoint Point, int Value)> values,
        int layer,
        bool reshapeAutotiles = true)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(values);
        HashSet<MapPoint> affected = [];
        foreach ((MapPoint point, int value) in values)
        {
            if (!map.IsInside(point.X, point.Y)) continue;
            map.PreviewSet(point.X, point.Y, layer, value);
            affected.Add(point);
        }
        if (!reshapeAutotiles) return;
        HashSet<MapPoint> reshape = [];
        foreach (MapPoint point in affected)
        {
            for (int y = point.Y - 1; y <= point.Y + 1; y++)
            {
                for (int x = point.X - 1; x <= point.X + 1; x++)
                {
                    if (map.IsInside(x, y)) reshape.Add(new MapPoint(x, y));
                }
            }
        }
        _autotiles.Reshape(map, reshape, layer);
    }
    public void PreviewCells(
        EditableMapBuffer map,
        IEnumerable<MapPoint> cells,
        int layer,
        int value,
        bool reshapeAutotiles = true)
    {
        ArgumentNullException.ThrowIfNull(cells);
        HashSet<MapPoint> affected = [];
        foreach (MapPoint point in cells)
        {
            if (!map.IsInside(point.X, point.Y)) continue;
            map.PreviewSet(point.X, point.Y, layer, value);
            affected.Add(point);
        }
        if (!reshapeAutotiles) return;
        HashSet<MapPoint> reshape = [];
        foreach (MapPoint point in affected)
        {
            for (int y = point.Y - 1; y <= point.Y + 1; y++)
            {
                for (int x = point.X - 1; x <= point.X + 1; x++)
                {
                    if (map.IsInside(x, y)) reshape.Add(new MapPoint(x, y));
                }
            }
        }
        _autotiles.Reshape(map, reshape, layer);
    }
}

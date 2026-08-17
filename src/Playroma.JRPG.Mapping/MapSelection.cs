namespace Playroma.JRPG.Mapping;

public sealed class MapSelectionData
{
    private readonly Dictionary<int, int[]> _layers;

    public MapSelectionData(MapRect bounds, IReadOnlyDictionary<int, int[]> layers)
    {
        if (bounds.IsEmpty) throw new ArgumentException("A map selection cannot be empty.", nameof(bounds));
        ArgumentNullException.ThrowIfNull(layers);
        Bounds = bounds;
        _layers = layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        int expected = checked(bounds.Width * bounds.Height);
        if (_layers.Any(pair => pair.Value.Length != expected)) throw new ArgumentException("A selected layer has an invalid data length.", nameof(layers));
    }

    public MapRect Bounds { get; }
    public IReadOnlyDictionary<int, int[]> Layers => _layers;

    public static MapSelectionData Capture(IReadOnlyMapBuffer map, MapRect bounds, IEnumerable<int> layers)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layers);
        MapRect clipped = bounds.Intersect(new MapRect(0, 0, map.Width, map.Height));
        if (clipped.IsEmpty) throw new ArgumentException("The selection is outside the map.", nameof(bounds));
        Dictionary<int, int[]> result = [];
        foreach (int layer in layers.Distinct().Order())
        {
            if (layer < 0 || layer >= map.LayerCount) continue;
            int[] values = new int[checked(clipped.Width * clipped.Height)];
            int index = 0;
            foreach (MapPoint point in clipped.Cells()) values[index++] = map.Get(point.X, point.Y, layer);
            result[layer] = values;
        }
        return new MapSelectionData(clipped, result);
    }

    public void PreviewPaste(EditableMapBuffer destination, MapPoint topLeft, bool transparentZero = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach ((int layer, int[] values) in _layers)
        {
            if (layer < 0 || layer >= destination.LayerCount) continue;
            int index = 0;
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    int value = values[index++];
                    if (transparentZero && value == 0) continue;
                    int targetX = topLeft.X + x;
                    int targetY = topLeft.Y + y;
                    if (destination.IsInside(targetX, targetY)) destination.PreviewSet(targetX, targetY, layer, value);
                }
            }
        }
    }

    public MapSelectionData MirrorHorizontal()
    {
        Dictionary<int, int[]> transformed = [];
        foreach ((int layer, int[] source) in _layers)
        {
            int[] target = new int[source.Length];
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    target[y * Bounds.Width + (Bounds.Width - x - 1)] = source[y * Bounds.Width + x];
                }
            }
            transformed[layer] = target;
        }
        return new MapSelectionData(new MapRect(0, 0, Bounds.Width, Bounds.Height), transformed);
    }

    public MapSelectionData MirrorVertical()
    {
        Dictionary<int, int[]> transformed = [];
        foreach ((int layer, int[] source) in _layers)
        {
            int[] target = new int[source.Length];
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    target[(Bounds.Height - y - 1) * Bounds.Width + x] = source[y * Bounds.Width + x];
                }
            }
            transformed[layer] = target;
        }
        return new MapSelectionData(new MapRect(0, 0, Bounds.Width, Bounds.Height), transformed);
    }

    public MapSelectionData RotateClockwise()
    {
        Dictionary<int, int[]> transformed = [];
        foreach ((int layer, int[] source) in _layers)
        {
            int[] target = new int[source.Length];
            int newWidth = Bounds.Height;
            for (int y = 0; y < Bounds.Height; y++)
            {
                for (int x = 0; x < Bounds.Width; x++)
                {
                    int targetX = Bounds.Height - y - 1;
                    int targetY = x;
                    target[targetY * newWidth + targetX] = source[y * Bounds.Width + x];
                }
            }
            transformed[layer] = target;
        }
        return new MapSelectionData(new MapRect(0, 0, Bounds.Height, Bounds.Width), transformed);
    }
}

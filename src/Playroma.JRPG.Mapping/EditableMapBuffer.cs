namespace Playroma.JRPG.Mapping;

public interface IReadOnlyMapBuffer
{
    int Width { get; }
    int Height { get; }
    int LayerCount { get; }
    int Get(int x, int y, int layer);
    bool IsInside(int x, int y);
}

public sealed class EditableMapBuffer : IReadOnlyMapBuffer
{
    private readonly int[] _baseline;
    private readonly int[] _working;
    private readonly Dictionary<int, int> _preview = [];

    public EditableMapBuffer(int width, int height, int layerCount, IEnumerable<int> values)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (layerCount <= 0) throw new ArgumentOutOfRangeException(nameof(layerCount));
        ArgumentNullException.ThrowIfNull(values);
        Width = width;
        Height = height;
        LayerCount = layerCount;
        _working = values.ToArray();
        int expected = checked(width * height * layerCount);
        if (_working.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} map values but received {_working.Length}.", nameof(values));
        }
        _baseline = _working.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public int LayerCount { get; }
    public int Count => _working.Length;
    public bool HasPreview => _preview.Count > 0;
    public bool IsModified => !_working.AsSpan().SequenceEqual(_baseline);
    public IReadOnlyDictionary<int, int> PreviewValues => _preview;

    public bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public int Get(int x, int y, int layer) => _working[GetIndex(x, y, layer)];

    public int GetEffective(int x, int y, int layer)
    {
        int index = GetIndex(x, y, layer);
        return _preview.TryGetValue(index, out int value) ? value : _working[index];
    }

    public void BeginPreview() => _preview.Clear();

    public void PreviewSet(int x, int y, int layer, int value)
    {
        int index = GetIndex(x, y, layer);
        if (_working[index] == value) _preview.Remove(index);
        else _preview[index] = value;
    }

    public void PreviewSetIndex(int index, int value)
    {
        ValidateIndex(index);
        if (_working[index] == value) _preview.Remove(index);
        else _preview[index] = value;
    }

    public void CancelPreview() => _preview.Clear();

    public MapDataPatch BuildPreviewPatch()
    {
        if (_preview.Count == 0) throw new InvalidOperationException("The map buffer contains no preview changes.");
        return MapDataPatch.FromChanges(
            _preview.Select(item => new MapDataChange(item.Key, _working[item.Key], item.Value)),
            Width,
            Height);
    }

    public MapDataPatch CommitPreview()
    {
        MapDataPatch patch = BuildPreviewPatch();
        patch.Apply(_working);
        _preview.Clear();
        return patch;
    }

    public void ApplyPatch(MapDataPatch patch, bool verifyBefore = true)
    {
        ArgumentNullException.ThrowIfNull(patch);
        patch.Apply(_working, verifyBefore);
        _preview.Clear();
    }

    public void ResetToBaseline()
    {
        _baseline.CopyTo(_working, 0);
        _preview.Clear();
    }

    public int[] CaptureValues(bool includePreview = false)
    {
        int[] result = _working.ToArray();
        if (includePreview)
        {
            foreach ((int index, int value) in _preview) result[index] = value;
        }
        return result;
    }

    public int GetIndex(int x, int y, int layer)
    {
        if (!IsInside(x, y)) throw new ArgumentOutOfRangeException(nameof(x), $"Map cell ({x},{y}) is outside {Width}×{Height}.");
        if (layer < 0 || layer >= LayerCount) throw new ArgumentOutOfRangeException(nameof(layer));
        return checked((layer * Height + y) * Width + x);
    }

    public MapLayerCell DecodeIndex(int index)
    {
        ValidateIndex(index);
        int cellsPerLayer = checked(Width * Height);
        int layer = index / cellsPerLayer;
        int cell = index % cellsPerLayer;
        return new MapLayerCell(cell % Width, cell / Width, layer);
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= _working.Length) throw new ArgumentOutOfRangeException(nameof(index));
    }
}

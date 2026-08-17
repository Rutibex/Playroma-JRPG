namespace Playroma.JRPG.Mapping;

public sealed record MapEditPreview(
    MapToolKind Tool,
    MapPoint Start,
    MapPoint Current,
    IReadOnlyDictionary<int, int> Values,
    MapRect? Selection = null);

public sealed record MapEditCommitted(
    MapDataPatch Patch,
    string Description,
    DateTimeOffset TimestampUtc);

public sealed class EditableMapSession
{
    private readonly MapBrushEngine _brushes = new();

    public EditableMapSession(string relativePath, int mapId, EditableMapBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        RelativePath = relativePath.Replace('\\', '/');
        MapId = mapId;
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public string RelativePath { get; }
    public int MapId { get; }
    public EditableMapBuffer Buffer { get; }
    public MapEditPreview? Preview { get; private set; }
    public MapRect? Selection { get; private set; }

    public event EventHandler? PreviewChanged;
    public event EventHandler<MapEditCommitted>? EditCommitted;

    public void BeginPreview(MapToolKind tool, MapPoint start)
    {
        Buffer.BeginPreview();
        Preview = new MapEditPreview(tool, start, start, Buffer.PreviewValues);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PreviewCells(
        MapPoint current,
        IEnumerable<MapPoint> cells,
        int layer,
        int value,
        bool reshapeAutotiles = true)
    {
        if (Preview is null) throw new InvalidOperationException("No map-edit preview is active.");
        Buffer.BeginPreview();
        _brushes.PreviewCells(Buffer, cells, layer, value, reshapeAutotiles);
        Preview = Preview with { Current = current, Values = new Dictionary<int, int>(Buffer.PreviewValues) };
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PreviewValues(
        MapPoint current,
        IEnumerable<(MapPoint Point, int Value)> values,
        int layer,
        bool reshapeAutotiles = true)
    {
        if (Preview is null) throw new InvalidOperationException("No map-edit preview is active.");
        Buffer.BeginPreview();
        _brushes.PreviewValues(Buffer, values, layer, reshapeAutotiles);
        Preview = Preview with { Current = current, Values = new Dictionary<int, int>(Buffer.PreviewValues) };
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PreviewStamp(MapPoint current, TileStamp stamp, int layer, bool reshapeAutotiles = true)
    {
        if (Preview is null) throw new InvalidOperationException("No map-edit preview is active.");
        Buffer.BeginPreview();
        _brushes.PreviewStamp(Buffer, stamp, current, layer, reshapeAutotiles);
        Preview = Preview with { Current = current, Values = new Dictionary<int, int>(Buffer.PreviewValues) };
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public MapDataPatch CommitPreview(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        MapDataPatch patch = Buffer.CommitPreview();
        Preview = null;
        EditCommitted?.Invoke(this, new MapEditCommitted(patch, description, DateTimeOffset.UtcNow));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
        return patch;
    }

    public void CancelPreview()
    {
        Buffer.CancelPreview();
        Preview = null;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(MapRect? selection)
    {
        Selection = selection is { IsEmpty: false } value ? value : null;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
}

using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

public sealed record ParallaxRenderSnapshot(
    string Name,
    bool LoopX,
    bool LoopY,
    int SpeedX,
    int SpeedY,
    bool ShowInEditor,
    bool IsZeroParallax)
{
    public static ParallaxRenderSnapshot Empty { get; } = new(string.Empty, false, false, 0, 0, true, false);
    public bool HasImage => !string.IsNullOrWhiteSpace(Name);
}

public sealed record EventGraphicSnapshot(
    int TileId,
    string CharacterName,
    int CharacterIndex,
    int Direction,
    int Pattern)
{
    public bool IsTile => TileId > 0;
    public bool IsCharacter => !string.IsNullOrWhiteSpace(CharacterName);
    public static EventGraphicSnapshot Empty { get; } = new(0, string.Empty, 0, 2, 1);
}

public sealed record MapEventRenderSnapshot(
    int Id,
    string Name,
    int X,
    int Y,
    int PriorityType,
    int PageIndex,
    EventGraphicSnapshot Graphic)
{
    public RenderPass Pass => PriorityType switch
    {
        <= 0 => RenderPass.EventBelow,
        1 => RenderPass.EventSame,
        _ => RenderPass.EventAbove
    };
}

public sealed class MapRenderSnapshot
{
    private readonly int[] _data;
    private readonly IReadOnlyDictionary<int, int>? _overrides;

    public MapRenderSnapshot(
        int mapId,
        string editorName,
        string relativePath,
        int width,
        int height,
        int tilesetId,
        int scrollType,
        int[] data,
        IReadOnlyList<MapEventRenderSnapshot> events,
        ParallaxRenderSnapshot parallax,
        IReadOnlyList<ProjectDiagnostic>? diagnostics = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(data);
        int cellsPerLayer = checked(width * height);
        if (data.Length < checked(cellsPerLayer * 6)) throw new ArgumentException("MV map data requires at least six complete layers.", nameof(data));
        MapId = mapId;
        EditorName = editorName ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        Width = width;
        Height = height;
        TilesetId = tilesetId;
        ScrollType = scrollType;
        _data = data.ToArray();
        _overrides = null;
        LayerCount = _data.Length / cellsPerLayer;
        Events = events?.ToArray() ?? [];
        Parallax = parallax ?? ParallaxRenderSnapshot.Empty;
        Diagnostics = diagnostics?.ToArray() ?? [];
    }

    public int MapId { get; }
    public string EditorName { get; }
    public string RelativePath { get; }
    public int Width { get; }
    public int Height { get; }
    public int TilesetId { get; }
    public int ScrollType { get; }
    public int LayerCount { get; }
    public IReadOnlyList<MapEventRenderSnapshot> Events { get; }
    public ParallaxRenderSnapshot Parallax { get; }
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }
    public bool LoopVertical => ScrollType is 1 or 3;
    public bool LoopHorizontal => ScrollType is 2 or 3;
    public PixelSize PixelSize => new(checked(Width * MvTileRules.TileWidth), checked(Height * MvTileRules.TileHeight));

    public int Read(int x, int y, int layer)
    {
        if (layer < 0 || layer >= LayerCount) return 0;
        if (LoopHorizontal) x = Mod(x, Width);
        if (LoopVertical) y = Mod(y, Height);
        if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
        int index = checked((layer * Height + y) * Width + x);
        return _overrides is not null && _overrides.TryGetValue(index, out int value) ? value : _data[index];
    }

    public MapRenderSnapshot WithOverrides(IReadOnlyDictionary<int, int> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new MapRenderSnapshot(this, overrides);
    }

    public MapRenderSnapshot WithData(int[] data) => new(
        MapId, EditorName, RelativePath, Width, Height, TilesetId, ScrollType, data, Events, Parallax, Diagnostics);

    private MapRenderSnapshot(MapRenderSnapshot source, IReadOnlyDictionary<int, int> overrides)
    {
        MapId = source.MapId;
        EditorName = source.EditorName;
        RelativePath = source.RelativePath;
        Width = source.Width;
        Height = source.Height;
        TilesetId = source.TilesetId;
        ScrollType = source.ScrollType;
        _data = source._data;
        _overrides = new Dictionary<int, int>(overrides);
        LayerCount = source.LayerCount;
        Events = source.Events;
        Parallax = source.Parallax;
        Diagnostics = source.Diagnostics;
    }

    public int ReadShadow(int x, int y) => Read(x, y, 4);
    public int ReadRegion(int x, int y) => Read(x, y, 5);
    public ReadOnlyMemory<int> Data => _data;

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

public sealed class TilesetRenderSnapshot
{
    private readonly int[] _flags;
    private readonly string[] _names;

    public TilesetRenderSnapshot(
        int id,
        string name,
        int mode,
        string relativePath,
        string[] names,
        int[] flags,
        IReadOnlyList<ProjectDiagnostic>? diagnostics = null)
    {
        Id = id;
        Name = name ?? string.Empty;
        Mode = mode;
        RelativePath = relativePath ?? string.Empty;
        _names = names?.ToArray() ?? [];
        _flags = flags?.ToArray() ?? [];
        Diagnostics = diagnostics?.ToArray() ?? [];
    }

    public int Id { get; }
    public string Name { get; }
    public int Mode { get; }
    public string RelativePath { get; }
    public IReadOnlyList<string> Names => _names;
    public IReadOnlyList<int> RawFlags => _flags;
    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }

    public string GetSheetName(TileSheetKind sheet)
    {
        int index = (int)sheet;
        return index >= 0 && index < _names.Length ? _names[index] ?? string.Empty : string.Empty;
    }

    public TilesetFlags GetFlags(int tileId) =>
        tileId >= 0 && tileId < _flags.Length ? new TilesetFlags(_flags[tileId]) : new TilesetFlags(0);
}

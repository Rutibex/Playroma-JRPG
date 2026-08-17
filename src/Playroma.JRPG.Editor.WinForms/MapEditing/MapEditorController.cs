using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Rendering;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.MapEditing;

public interface IMapEditorController : IAsyncDisposable
{
    int? ActiveMapId { get; }
    MapEditorMode Mode { get; }
    MapEditorState State { get; }
    MapRenderDocumentControl? ActiveSurface { get; }

    event EventHandler<ActiveMapChangedEventArgs>? ActiveMapChanged;
    event EventHandler<MapEditorStateChangedEventArgs>? StateChanged;

    Task OpenMapAsync(
        int mapId,
        MapEditorNavigationTarget? target = null,
        CancellationToken cancellationToken = default);

    void SetMode(MapEditorMode mode);
    void SetTool(MapToolKind tool);
    void CaptureActiveViewState();
    void RestoreViewStates(IEnumerable<MapViewState> states);
    IReadOnlyList<MapViewState> CaptureViewStates();
}

/// <summary>
/// Owns active-map switching, stale-load rejection, and per-map view state. The WinForms
/// form only lays out controls and routes commands.
/// </summary>
public sealed class MapEditorController : IMapEditorController
{
    private readonly MvProjectSession _session;
    private readonly ISelectionService _selection;
    private readonly Dictionary<int, MapViewState> _viewStates = [];
    private readonly MapOverlayOptions _defaultOverlays;
    private readonly bool _defaultMinimapVisible;
    private CancellationTokenSource? _loadCancellation;
    private int _loadGeneration;
    private bool _disposed;

    public MapEditorController(
        MvProjectSession session,
        ISelectionService selection,
        MapOverlayOptions defaultOverlays = MapOverlayOptions.Grid | MapOverlayOptions.EventIds,
        bool defaultMinimapVisible = true)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _defaultOverlays = defaultOverlays;
        _defaultMinimapVisible = defaultMinimapVisible;
        State = new MapEditorState(null, MapEditorMode.Map, MapToolKind.Pencil, false);
    }

    public int? ActiveMapId => ActiveSurface?.MapId;
    public MapEditorMode Mode => State.Mode;
    public MapEditorState State { get; private set; }
    public MapRenderDocumentControl? ActiveSurface { get; private set; }

    public event EventHandler<ActiveMapChangedEventArgs>? ActiveMapChanged;
    public event EventHandler<MapEditorStateChangedEventArgs>? StateChanged;

    public async Task OpenMapAsync(
        int mapId,
        MapEditorNavigationTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (ActiveMapId == mapId && ActiveSurface is { } existing)
        {
            ApplyNavigation(existing, target);
            return;
        }

        MvMapDescriptor descriptor = _session.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == mapId)
            ?? throw new KeyNotFoundException($"Map {mapId} was not found in the current project.");

        CaptureActiveViewState();
        int generation = Interlocked.Increment(ref _loadGeneration);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loadCancellation.Token;
        SetState(State with { IsLoading = true, ErrorMessage = null });

        MapRenderDocumentControl? next = null;
        try
        {
            next = new MapRenderDocumentControl(
                _session,
                descriptor.Id,
                _selection,
                descriptor.RelativePath,
                descriptor.Name)
            {
                EmbeddedChromeVisible = false
            };
            await next.LoadAsync(token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (generation != _loadGeneration)
            {
                next.Dispose();
                return;
            }

            if (_viewStates.TryGetValue(mapId, out MapViewState? state))
            {
                next.RestoreViewState(state);
            }
            else
            {
                next.Overlays = _defaultOverlays;
                next.MinimapVisible = _defaultMinimapVisible;
                next.SetMapEditorMode(target?.PreferredMode ?? State.Mode);
            }

            MapRenderDocumentControl? previous = ActiveSurface;
            if (previous is not null) previous.EditorStateChanged -= OnSurfaceEditorStateChanged;
            ActiveSurface = next;
            ActiveSurface.EditorStateChanged += OnSurfaceEditorStateChanged;
            next = null;
            _selection.Select(new MapSelection(mapId));
            SetState(new MapEditorState(
                mapId,
                ActiveSurface.EditorMode,
                ActiveSurface.ActiveTool,
                false));
            ActiveMapChanged?.Invoke(this, new ActiveMapChangedEventArgs(previous, ActiveSurface));
            previous?.Dispose();
            ApplyNavigation(ActiveSurface, target);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            next?.Dispose();
            if (generation == _loadGeneration)
            {
                SetState(State with { IsLoading = false });
            }
        }
        catch (Exception exception)
        {
            next?.Dispose();
            if (generation == _loadGeneration)
            {
                SetState(State with { IsLoading = false, ErrorMessage = exception.Message });
            }
            throw;
        }
    }

    public void SetMode(MapEditorMode mode)
    {
        ThrowIfDisposed();
        ActiveSurface?.SetMapEditorMode(mode);
        MapToolKind tool = ActiveSurface?.ActiveTool
            ?? (mode == MapEditorMode.Events ? MapToolKind.Events : MapToolKind.Pencil);
        SetState(State with { Mode = mode, Tool = tool });
    }

    public void SetTool(MapToolKind tool)
    {
        ThrowIfDisposed();
        ActiveSurface?.SetActiveTool(tool);
        MapEditorMode mode = tool == MapToolKind.Events ? MapEditorMode.Events : MapEditorMode.Map;
        SetState(State with { Mode = mode, Tool = tool });
    }

    public void CaptureActiveViewState()
    {
        if (ActiveSurface is null) return;
        _viewStates[ActiveSurface.MapId] = ActiveSurface.CaptureViewState();
    }

    public void RestoreViewStates(IEnumerable<MapViewState> states)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(states);
        _viewStates.Clear();
        foreach (MapViewState state in states)
        {
            _viewStates[state.MapId] = state;
        }
    }

    public IReadOnlyList<MapViewState> CaptureViewStates()
    {
        ThrowIfDisposed();
        CaptureActiveViewState();
        return _viewStates.Values.OrderBy(state => state.MapId).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        MapRenderDocumentControl? surface = ActiveSurface;
        ActiveSurface = null;
        if (surface is not null)
        {
            surface.EditorStateChanged -= OnSurfaceEditorStateChanged;
            surface.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private void OnSurfaceEditorStateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveSurface) || ActiveSurface is null) return;
        SetState(State with
        {
            ActiveMapId = ActiveSurface.MapId,
            Mode = ActiveSurface.EditorMode,
            Tool = ActiveSurface.ActiveTool,
            IsLoading = false,
            ErrorMessage = null
        });
    }

    private void ApplyNavigation(
        MapRenderDocumentControl surface,
        MapEditorNavigationTarget? target)
    {
        if (target is null) return;
        if (target.PreferredMode.HasValue) SetMode(target.PreferredMode.Value);
        if (target.EventId.HasValue)
        {
            surface.SelectEvent(target.EventId.Value, centre: target.CentreTarget);
        }
        else if (target.TileX.HasValue && target.TileY.HasValue && target.CentreTarget)
        {
            surface.CentreOnTile(target.TileX.Value, target.TileY.Value);
        }
    }

    private void SetState(MapEditorState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, new MapEditorStateChangedEventArgs(state));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

using Playroma.JRPG.Mapping;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Editor.WinForms.Rendering;

namespace Playroma.JRPG.Editor.WinForms.MapEditing;

public enum MapEditorMode
{
    Map,
    Events
}

public sealed record MapEditorNavigationTarget(
    int MapId,
    int? TileX = null,
    int? TileY = null,
    int? EventId = null,
    MapEditorMode? PreferredMode = null,
    bool CentreTarget = true);

/// <summary>
/// Stable user-view state retained while switching between maps. It intentionally excludes
/// transient pointer gestures and preview buffers.
/// </summary>
public sealed record MapViewState(
    int MapId,
    float Zoom,
    int CentreTileX,
    int CentreTileY,
    MapEditorMode Mode,
    MapToolKind Tool,
    MapEditMode EditMode,
    int SelectedLayer,
    int SelectedValue,
    string SelectedSheet,
    MapOverlayOptions Overlays,
    bool MinimapVisible);

public sealed record MapEditorState(
    int? ActiveMapId,
    MapEditorMode Mode,
    MapToolKind Tool,
    bool IsLoading,
    string? ErrorMessage = null);

public sealed class ActiveMapChangedEventArgs : EventArgs
{
    public ActiveMapChangedEventArgs(
        MapRenderDocumentControl? previous,
        MapRenderDocumentControl? current)
    {
        Previous = previous;
        Current = current;
    }

    public MapRenderDocumentControl? Previous { get; }
    public MapRenderDocumentControl? Current { get; }
}

public sealed class MapEditorStateChangedEventArgs : EventArgs
{
    public MapEditorStateChangedEventArgs(MapEditorState state) => State = state;
    public MapEditorState State { get; }
}

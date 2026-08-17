using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.MapEditing;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;
using Playroma.JRPG.RpgMakerMV;

using ShellEditorCommandContext = Playroma.JRPG.Editor.Shell.EditorCommandContext;
namespace Playroma.JRPG.Editor.WinForms.Rendering;

/// <summary>Complete standard-MV map editing document built on the Phase 6 renderer.</summary>
public sealed class MapRenderDocumentControl : UserControl, IEditorCommandTarget
{
    private readonly MvProjectSession _session;
    private readonly ISelectionService _selectionService;
    private readonly int _mapId;
    private readonly string? _relativePath;
    private readonly string? _editorName;
    private readonly MvProjectMapRenderLoader _loader = new();
    private readonly MapCanvasControl _canvas = new() { Dock = DockStyle.Fill };
    private readonly MapMinimapControl _minimap = new() { Dock = DockStyle.Fill };
    private readonly Panel _minimapPanel = new() { Dock = DockStyle.Right, Width = 220, Padding = new Padding(4), Visible = true };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStrip _editToolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _mapStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _coordinateStatus = new() { Text = "—" };
    private readonly ToolStripStatusLabel _editStatus = new() { Text = "Pencil" };
    private readonly ToolStripStatusLabel _zoomStatus = new() { Text = "100%" };
    private readonly Panel _noticePanel = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false };
    private readonly Label _notice = new() { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(1200, 0), Padding = new Padding(8), ForeColor = Color.Goldenrod };
    private readonly Label _loading = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Loading map editor…", BackColor = Color.FromArgb(29, 31, 34), ForeColor = Color.White };
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 300 };
    private readonly Dictionary<MapToolKind, ToolStripButton> _toolButtons = [];
    private readonly ToolStripComboBox _modeChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
    private readonly ToolStripComboBox _layerChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumericUpDown _valueEditor = new() { Minimum = 0, Maximum = 8191, Width = 80 };
    private readonly ToolStripButton _filledButton = new("Filled") { CheckOnClick = true, Checked = true };
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _minimapCancellation;
    private LoadedMapRenderContext? _renderContext;
    private EditableMapSession? _editSession;
    private MapToolKind _tool = MapToolKind.Pencil;
    private MapEditMode _editMode = MapEditMode.Automatic;
    private int _selectedValue;
    private string _selectedSheet = "A5";
    private MapTileBrushSelection? _selectedBrush;
    private int _selectedLayer;
    private bool _gestureActive;
    private MapPoint _gestureStart;
    private MapPoint _gestureCurrent;
    private MapPoint _lastStrokePoint;
    private readonly HashSet<MapPoint> _strokePoints = [];
    private readonly HashSet<int> _selectedEventIds = [];
    private Dictionary<int, MapPoint>? _eventDragOrigins;
    private bool _committingEdit;
    private bool _loaded;
    private bool _disposed;

    public MapRenderDocumentControl(MvProjectSession session, int mapId, ISelectionService selectionService, string? relativePath = null, string? editorName = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _mapId = mapId;
        _relativePath = relativePath;
        _editorName = editorName;
        Dock = DockStyle.Fill;
        _noticePanel.Controls.Add(_notice);
        _minimapPanel.Controls.Add(_minimap);
        _status.Items.AddRange([_mapStatus, _coordinateStatus, _editStatus, _zoomStatus]);
        BuildToolbar();
        BuildEditToolbar();
        Controls.Add(_canvas);
        Controls.Add(_minimapPanel);
        Controls.Add(_loading);
        Controls.Add(_noticePanel);
        Controls.Add(_status);
        Controls.Add(_editToolbar);
        Controls.Add(_toolbar);

        _canvas.CoordinateChanged += OnCoordinateChanged;
        _canvas.TilePointerDown += OnTilePointerDown;
        _canvas.TilePointerMove += OnTilePointerMove;
        _canvas.TilePointerUp += OnTilePointerUp;
        _canvas.TileDoubleClicked += OnTileDoubleClicked;
        _canvas.ZoomChanged += OnCanvasZoomChanged;
        _canvas.ViewportChanged += OnCanvasViewportChanged;
        _minimap.MapPointRequested += (_, eventArgs) => _canvas.CentreOnMapPixel(eventArgs.MapPixel.X, eventArgs.MapPixel.Y);
        _selectionService.SelectionChanged += OnSelectionChanged;
        _reloadTimer.Tick += async (_, _) =>
        {
            _reloadTimer.Stop();
            await ReloadAsync().ConfigureAwait(true);
        };
        _session.Assets.Changed += OnAssetsChanged;
        _session.DocumentChanged += OnDocumentChanged;
    }

    public event EventHandler<DocumentRequestEventArgs>? DocumentRequested;
    public event EventHandler? EditorStateChanged;
    public event EventHandler<MapCoordinateEventArgs>? CoordinateChanged;
    public event EventHandler? ViewportChanged;

    public int MapId => _mapId;
    public string DisplayName => _renderContext?.Map.EditorName ?? _editorName ?? $"Map {_mapId:D3}";
    public MapToolKind ActiveTool => _tool;
    public MapEditorMode EditorMode => _tool == MapToolKind.Events ? MapEditorMode.Events : MapEditorMode.Map;
    public MapEditMode ActiveEditMode => _editMode;
    public int ActiveLayer => _selectedLayer;
    public int SelectedValue => _selectedValue;
    public string SelectedSheet => _selectedSheet;
    public float Zoom => _canvas.Zoom;
    public MapOverlayOptions Overlays
    {
        get => _canvas.Overlays;
        set
        {
            _canvas.Overlays = value;
            EditorStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool MinimapVisible
    {
        get => _minimapPanel.Visible;
        set
        {
            _minimapPanel.Visible = value;
            EditorStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool EmbeddedChromeVisible
    {
        get => _toolbar.Visible || _editToolbar.Visible || _status.Visible;
        set
        {
            _toolbar.Visible = value;
            _editToolbar.Visible = value;
            _status.Visible = value;
        }
    }
    public bool IsMapLoaded => _renderContext is not null;

    public void ApplyTheme(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _canvas.ApplyPalette(palette);
        _loading.BackColor = palette.CanvasBackground;
        _loading.ForeColor = palette.PrimaryText;
        _notice.ForeColor = palette.Warning;
        _minimapPanel.BackColor = palette.PanelBackground;
        Invalidate(true);
    }


    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public void SetMapEditorMode(MapEditorMode mode)
    {
        SetActiveTool(mode == MapEditorMode.Events
            ? MapToolKind.Events
            : (_tool == MapToolKind.Events ? MapToolKind.Pencil : _tool));
    }

    public void SetActiveTool(MapToolKind tool)
    {
        SelectTool(tool);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEditMode(MapEditMode mode)
    {
        _editMode = mode;
        _modeChoice.SelectedIndex = mode == MapEditMode.Automatic ? 0 : 1;
        UpdateEditStatus();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetExplicitLayer(int layer)
    {
        _selectedLayer = Math.Clamp(layer, 0, 5);
        _layerChoice.SelectedIndex = _selectedLayer;
        UpdateEditStatus();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedValue(int value, string? sheetName = null)
    {
        _selectedValue = Math.Clamp(value, 0, decimal.ToInt32(_valueEditor.Maximum));
        _selectedBrush = null;
        if (!string.IsNullOrWhiteSpace(sheetName)) _selectedSheet = sheetName;
        _valueEditor.Value = Math.Clamp(_selectedValue, decimal.ToInt32(_valueEditor.Minimum), decimal.ToInt32(_valueEditor.Maximum));
        UpdateEditStatus();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => _canvas.ZoomIn();
    public void ZoomOut() => _canvas.ZoomOut();
    public void ActualSize() => _canvas.ActualSize();
    public void ZoomToFit() => _canvas.ZoomToFit();
    public void SetZoom(float zoom) => _canvas.SetZoom(zoom);
    public void CentreOnTile(int x, int y) => _canvas.CentreOnTile(x, y);
    public ValueTask ReloadMapAsync(CancellationToken cancellationToken = default) => ReloadAsync(cancellationToken);

    public void ToggleOverlay(MapOverlayOptions option)
    {
        if ((_canvas.Overlays & option) != 0) _canvas.Overlays &= ~option;
        else _canvas.Overlays |= option;
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public MapViewState CaptureViewState()
    {
        PixelRect visible = _canvas.VisibleMapRegion;
        int centreX = Math.Max(0, (visible.X + visible.Width / 2) / MvTileRules.TileWidth);
        int centreY = Math.Max(0, (visible.Y + visible.Height / 2) / MvTileRules.TileHeight);
        return new MapViewState(
            _mapId,
            _canvas.Zoom,
            centreX,
            centreY,
            EditorMode,
            _tool,
            _editMode,
            _selectedLayer,
            _selectedValue,
            _selectedSheet,
            _canvas.Overlays,
            _minimapPanel.Visible);
    }

    public void RestoreViewState(MapViewState state)
    {
        if (state.MapId != _mapId) return;
        _canvas.Overlays = state.Overlays;
        _minimapPanel.Visible = state.MinimapVisible;
        SetEditMode(state.EditMode);
        SetExplicitLayer(state.SelectedLayer);
        SetSelectedValue(state.SelectedValue, state.SelectedSheet);
        SetMapEditorMode(state.Mode);
        if (state.Mode == MapEditorMode.Map && state.Tool != MapToolKind.Events)
        {
            SetActiveTool(state.Tool);
        }
        _canvas.SetZoom(state.Zoom);
        _canvas.CentreOnTile(state.CentreTileX, state.CentreTileY);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SelectEvent(int eventId, bool centre)
    {
        MapEventRenderSnapshot? mapEvent = _renderContext?.Map.Events.FirstOrDefault(item => item.Id == eventId);
        if (mapEvent is null) return false;
        SetMapEditorMode(MapEditorMode.Events);
        _selectedEventIds.Clear();
        _selectedEventIds.Add(eventId);
        UpdateEventSelectionOverlay();
        if (centre) _canvas.CentreOnTile(mapEvent.X, mapEvent.Y);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public event EventHandler? CommandStateChanged;

    public EditorCommandStatus QueryCommand(EditorCommandId command, ShellEditorCommandContext context)
    {
        bool hasSelection = _editSession?.Selection is MapRect;
        if (command == EditorCommandIds.EditCopy)
            return hasSelection ? EditorCommandStatus.Enabled("Copy Map Selection") : EditorCommandStatus.Disabled("Copy Map Selection", "Select map tiles first.");
        if (command == EditorCommandIds.EditCut)
            return hasSelection ? EditorCommandStatus.Enabled("Cut Map Selection") : EditorCommandStatus.Disabled("Cut Map Selection", "Select map tiles first.");
        if (command == EditorCommandIds.EditDelete)
            return hasSelection ? EditorCommandStatus.Enabled("Clear Map Selection") : EditorCommandStatus.Disabled("Clear Map Selection", "Select map tiles first.");
        if (command == EditorCommandIds.EditPaste)
            return Clipboard.ContainsText() ? EditorCommandStatus.Enabled("Paste Map Selection") : EditorCommandStatus.Disabled("Paste Map Selection", "The clipboard does not contain map tiles.");
        return EditorCommandStatus.NotHandled(command);
    }

    public async ValueTask ExecuteCommandAsync(EditorCommandId command, ShellEditorCommandContext context)
    {
        await ExecuteRoutedCommandAsync(command).ConfigureAwait(true);
        CommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask<bool> ExecuteRoutedCommandAsync(EditorCommandId command)
    {
        if (command == EditorCommandIds.EditCopy)
        {
            await CopySelectionAsync().ConfigureAwait(true);
            return true;
        }
        if (command == EditorCommandIds.EditPaste)
        {
            await PasteSelectionAtViewportCentreAsync().ConfigureAwait(true);
            return true;
        }
        if (command == EditorCommandIds.EditCut)
        {
            await CutSelectionAsync().ConfigureAwait(true);
            return true;
        }
        if (command == EditorCommandIds.EditDelete && _editSession?.Selection is MapRect selection)
        {
            await ClearSelectionAsync(selection).ConfigureAwait(true);
            return true;
        }
        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _gestureActive)
        {
            CancelGesture();
            return true;
        }
        if (keyData == (Keys.Control | Keys.C))
        {
            _ = CopySelectionAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.V))
        {
            _ = PasteSelectionAtViewportCentreAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.X))
        {
            _ = CutSelectionAsync();
            return true;
        }
        if (keyData == Keys.Delete && _editSession?.Selection is MapRect selection)
        {
            _ = ClearSelectionAsync(selection);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _session.Assets.Changed -= OnAssetsChanged;
            _session.DocumentChanged -= OnDocumentChanged;
            _selectionService.SelectionChanged -= OnSelectionChanged;
            _reloadTimer.Stop();
            _reloadTimer.Dispose();
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _minimapCancellation?.Cancel();
            _minimapCancellation?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildToolbar()
    {
        ToolStripButton zoomOut = new("−") { ToolTipText = "Zoom out (Ctrl+mouse wheel)" };
        ToolStripButton zoomIn = new("+") { ToolTipText = "Zoom in (Ctrl+mouse wheel)" };
        ToolStripButton actual = new("1:1") { ToolTipText = "Actual tile size" };
        ToolStripButton fit = new("Fit") { ToolTipText = "Fit the entire map" };
        ToolStripButton refresh = new("Refresh") { ToolTipText = "Reload map data and staged assets" };
        ToolStripButton properties = new("Properties") { ToolTipText = "Open the map properties editor" };
        ToolStripButton animate = new("Animate") { CheckOnClick = true, Checked = true, ToolTipText = "Animate A1 water and waterfall tiles" };
        ToolStripButton minimap = new("Minimap") { CheckOnClick = true, Checked = true, ToolTipText = "Show the map overview" };
        ToolStripDropDownButton overlays = new("Overlays");
        AddOverlay(overlays, "Grid", MapOverlayOptions.Grid, isChecked: true);
        AddOverlay(overlays, "Regions", MapOverlayOptions.Regions, isChecked: false);
        AddOverlay(overlays, "Shadow bits", MapOverlayOptions.Shadows, isChecked: false);
        AddOverlay(overlays, "Directional passability", MapOverlayOptions.Passability, isChecked: false);
        AddOverlay(overlays, "Terrain tags", MapOverlayOptions.TerrainTags, isChecked: false);
        AddOverlay(overlays, "Event IDs", MapOverlayOptions.EventIds, isChecked: true);
        AddOverlay(overlays, "Event names", MapOverlayOptions.EventNames, isChecked: false);
        AddOverlay(overlays, "Event priority", MapOverlayOptions.EventPriorities, isChecked: false);

        zoomOut.Click += (_, _) => _canvas.ZoomOut();
        zoomIn.Click += (_, _) => _canvas.ZoomIn();
        actual.Click += (_, _) => _canvas.ActualSize();
        fit.Click += (_, _) => _canvas.ZoomToFit();
        refresh.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        animate.CheckedChanged += (_, _) => _canvas.AnimationEnabled = animate.Checked;
        minimap.CheckedChanged += (_, _) => _minimapPanel.Visible = minimap.Checked;
        properties.Click += (_, _) => OpenProperties();

        _toolbar.Items.AddRange([
            zoomOut,
            zoomIn,
            actual,
            fit,
            new ToolStripSeparator(),
            overlays,
            animate,
            minimap,
            new ToolStripSeparator(),
            refresh,
            properties
        ]);
    }

    private void BuildEditToolbar()
    {
        AddTool(MapToolKind.Pencil, "Pencil", "Paint selected tile (P)");
        AddTool(MapToolKind.Rectangle, "Rectangle", "Paint rectangle (R)");
        AddTool(MapToolKind.Ellipse, "Ellipse", "Paint ellipse (O)");
        AddTool(MapToolKind.Line, "Line", "Paint line (L)");
        AddTool(MapToolKind.Pattern, "Pattern", "Paint a deterministic checker pattern");
        AddTool(MapToolKind.Random, "Random", "Paint deterministic tile variation");
        AddTool(MapToolKind.FloodFill, "Fill", "Flood fill (F)");
        AddTool(MapToolKind.Eraser, "Eraser", "Erase selected layer (E)");
        AddTool(MapToolKind.Selection, "Select", "Select map area (S)");
        AddTool(MapToolKind.MagicWand, "Wand", "Select connected equal cells");
        AddTool(MapToolKind.Region, "Region", "Paint region IDs");
        AddTool(MapToolKind.Shadow, "Shadow", "Paint shadow bit mask");
        AddTool(MapToolKind.Eyedropper, "Pick", "Pick a tile value from the map");
        AddTool(MapToolKind.Events, "Events", "Create, select, move and manage map events");

        _modeChoice.Items.AddRange(["Automatic", "Explicit"]);
        _modeChoice.SelectedIndex = 0;
        _modeChoice.SelectedIndexChanged += (_, _) =>
        {
            _editMode = _modeChoice.SelectedIndex == 0 ? MapEditMode.Automatic : MapEditMode.ExplicitLayer;
            _layerChoice.Enabled = _editMode == MapEditMode.ExplicitLayer || _tool is MapToolKind.Region or MapToolKind.Shadow;
            UpdateEditStatus();
        };
        _layerChoice.Items.AddRange(["Lower 1", "Lower 2", "Upper 1", "Upper 2", "Shadow", "Region"]);
        _layerChoice.SelectedIndex = 0;
        _layerChoice.SelectedIndexChanged += (_, _) =>
        {
            _selectedLayer = Math.Max(0, _layerChoice.SelectedIndex);
            UpdateEditStatus();
        };
        _valueEditor.ValueChanged += (_, _) =>
        {
            _selectedValue = decimal.ToInt32(_valueEditor.Value);
            UpdateEditStatus();
        };
        _filledButton.CheckedChanged += (_, _) => UpdateEditStatus();

        ToolStripButton copy = new("Copy") { ToolTipText = "Copy selection (Ctrl+C)" };
        ToolStripButton cut = new("Cut") { ToolTipText = "Copy and clear all selected layers (Ctrl+X)" };
        ToolStripButton paste = new("Paste") { ToolTipText = "Paste selection at the viewport centre (Ctrl+V)" };
        ToolStripButton rotate = new("Rotate") { ToolTipText = "Rotate the map clipboard 90 degrees clockwise" };
        ToolStripButton mirrorH = new("Mirror H") { ToolTipText = "Mirror the map clipboard horizontally" };
        ToolStripButton mirrorV = new("Mirror V") { ToolTipText = "Mirror the map clipboard vertically" };
        ToolStripButton export = new("Export") { ToolTipText = "Export the current selection as a Playroma map-section file" };
        ToolStripButton prefab = new("Prefab") { ToolTipText = "Save the selection under .playroma/prefabs/maps" };
        ToolStripButton clear = new("Clear") { ToolTipText = "Clear the current selection on the active layer" };
        copy.Click += async (_, _) => await CopySelectionAsync().ConfigureAwait(true);
        cut.Click += async (_, _) => await CutSelectionAsync().ConfigureAwait(true);
        paste.Click += async (_, _) => await PasteSelectionAtViewportCentreAsync().ConfigureAwait(true);
        rotate.Click += (_, _) => TransformClipboard(selection => selection.RotateClockwise(), "Rotated clipboard");
        mirrorH.Click += (_, _) => TransformClipboard(selection => selection.MirrorHorizontal(), "Mirrored clipboard horizontally");
        mirrorV.Click += (_, _) => TransformClipboard(selection => selection.MirrorVertical(), "Mirrored clipboard vertically");
        export.Click += async (_, _) => await ExportSelectionAsync().ConfigureAwait(true);
        prefab.Click += async (_, _) => await SaveSelectionAsPrefabAsync().ConfigureAwait(true);
        clear.Click += async (_, _) =>
        {
            if (_editSession?.Selection is MapRect selection) await ClearSelectionAsync(selection).ConfigureAwait(true);
        };

        _editToolbar.Items.AddRange([
            new ToolStripSeparator(),
            new ToolStripLabel("Mode:"),
            _modeChoice,
            new ToolStripLabel("Layer:"),
            _layerChoice,
            new ToolStripLabel("Value:"),
            new ToolStripControlHost(_valueEditor),
            _filledButton,
            new ToolStripSeparator(),
            copy,
            cut,
            paste,
            rotate,
            mirrorH,
            mirrorV,
            export,
            prefab,
            clear
        ]);
        SelectTool(MapToolKind.Pencil);
    }

    private void AddTool(MapToolKind tool, string text, string tooltip)
    {
        ToolStripButton button = new(text) { CheckOnClick = true, ToolTipText = tooltip };
        button.Click += (_, _) => SelectTool(tool);
        _toolButtons[tool] = button;
        _editToolbar.Items.Add(button);
    }

    private void SelectTool(MapToolKind tool)
    {
        _tool = tool;
        foreach ((MapToolKind candidate, ToolStripButton button) in _toolButtons) button.Checked = candidate == tool;
        if (tool == MapToolKind.Region)
        {
            _selectedLayer = 5;
            _layerChoice.SelectedIndex = 5;
            _valueEditor.Maximum = 255;
            _canvas.Overlays |= MapOverlayOptions.Regions;
        }
        else if (tool == MapToolKind.Shadow)
        {
            _selectedLayer = 4;
            _layerChoice.SelectedIndex = 4;
            _valueEditor.Maximum = 15;
            _canvas.Overlays |= MapOverlayOptions.Shadows;
        }
        else
        {
            _valueEditor.Maximum = 8191;
        }
        _modeChoice.Enabled = tool is not MapToolKind.Region and not MapToolKind.Shadow and not MapToolKind.Events;
        _layerChoice.Enabled = tool is MapToolKind.Region or MapToolKind.Shadow || _editMode == MapEditMode.ExplicitLayer;
        UpdateEditStatus();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddOverlay(ToolStripDropDownButton parent, string text, MapOverlayOptions option, bool isChecked)
    {
        ToolStripMenuItem item = new(text) { CheckOnClick = true, Checked = isChecked };
        item.CheckedChanged += (_, _) =>
        {
            if (item.Checked) _canvas.Overlays |= option;
            else _canvas.Overlays &= ~option;
        };
        parent.DropDownItems.Add(item);
    }

    private async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        CancelGesture();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loadCancellation.Token;
        _loading.Visible = true;
        _loading.BringToFront();
        _mapStatus.Text = "Loading map…";
        try
        {
            LoadedMapRenderContext context = await _loader.LoadAsync(_session, _mapId, _relativePath, _editorName, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _renderContext = context;
            _editSession = new EditableMapSession(
                context.Map.RelativePath,
                _mapId,
                new EditableMapBuffer(context.Map.Width, context.Map.Height, context.Map.LayerCount, context.Map.Data.ToArray()));
            _editSession.PreviewChanged += OnEditPreviewChanged;
            _canvas.SetContext(context);
            _canvas.SelectionRegion = null;
            _minimap.SetViewport(_canvas.VisibleMapRegion);
            StartMinimapRender(context);
            _mapStatus.Text = $"Map {_mapId:D3} — {context.Map.EditorName} — {context.Map.Width}×{context.Map.Height} — Tileset {context.Tileset.Id}: {context.Tileset.Name}";
            if (context.CompatibilityNotices.Count > 0)
            {
                _notice.Text = string.Join(Environment.NewLine, context.CompatibilityNotices.Select(item => "⚠ " + item.Message));
                _noticePanel.Visible = true;
            }
            else
            {
                _noticePanel.Visible = false;
                _notice.Text = string.Empty;
            }
            BeginInvoke(new Action(_canvas.ZoomToFit));
        }
        catch (OperationCanceledException)
        {
            _mapStatus.Text = "Map loading cancelled";
        }
        catch (Exception exception)
        {
            _mapStatus.Text = "Map editor failed";
            _notice.Text = exception.Message;
            _noticePanel.Visible = true;
        }
        finally
        {
            if (!token.IsCancellationRequested) _loading.Visible = false;
        }
    }

    private void StartMinimapRender(LoadedMapRenderContext context)
    {
        _minimapCancellation?.Cancel();
        _minimapCancellation?.Dispose();
        _minimapCancellation = new CancellationTokenSource();
        CancellationToken token = _minimapCancellation.Token;
        _ = RenderMinimapAsync(context, token);
    }

    private async Task RenderMinimapAsync(LoadedMapRenderContext context, CancellationToken cancellationToken)
    {
        try
        {
            MapMinimapResult? result = await Task.Run(
                () => BuildMinimapWithoutCancellationEscape(context, cancellationToken),
                CancellationToken.None).ConfigureAwait(true);
            if (result is null || cancellationToken.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;
            TryBeginInvoke(new Action(() =>
            {
                if (cancellationToken.IsCancellationRequested || IsDisposed || !ReferenceEquals(_renderContext, context)) return;
                _minimap.SetImage(result);
                _minimap.SetViewport(_canvas.VisibleMapRegion);
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsDisposed || !IsHandleCreated) return;
            TryBeginInvoke(new Action(() =>
            {
                if (!IsDisposed && ReferenceEquals(_renderContext, context))
                {
                    _mapStatus.Text = "Minimap unavailable: " + exception.Message;
                }
            }));
        }
    }

    /// <summary>
    /// Keeps expected minimap cancellation inside the background worker. This is
    /// important for fire-and-forget map loading because Visual Studio's Just My
    /// Code debugger otherwise stops when cancellation crosses Task.Run, even
    /// though RenderMinimapAsync handles it later.
    /// </summary>
    private static MapMinimapResult? BuildMinimapWithoutCancellationEscape(
        LoadedMapRenderContext context,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        try
        {
            return new MapMinimapBuilder().Build(
                context.Map,
                context.Tileset,
                context.Assets,
                320,
                320,
                0,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void TryBeginInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(action);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated)
        {
        }
    }

    private void OnTilePointerDown(object? sender, MapTilePointerEventArgs e)
    {
        EditableMapSession? edit = _editSession;
        if (edit is null) return;
        MapPoint point = new(e.X, e.Y);
        if (_tool == MapToolKind.Events)
        {
            HandleEventPointerDown(point, e);
            return;
        }
        if (e.Button == MouseButtons.Right || _tool == MapToolKind.Eyedropper)
        {
            PickValue(point);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (_tool == MapToolKind.Selection)
        {
            _gestureActive = true;
            _gestureStart = point;
            _gestureCurrent = point;
            UpdateSelectionPreview();
            return;
        }
        if (_tool == MapToolKind.MagicWand)
        {
            int value = edit.Buffer.Get(point.X, point.Y, _selectedLayer);
            IReadOnlyList<MapPoint> cells = MapDrawingAlgorithms.FloodFill(edit.Buffer, point, _selectedLayer, candidate => candidate == value);
            MapRect bounds = BoundsOf(cells);
            edit.SetSelection(bounds);
            UpdateSelectionOverlay();
            return;
        }

        _gestureActive = true;
        _gestureStart = point;
        _gestureCurrent = point;
        _lastStrokePoint = point;
        _strokePoints.Clear();
        _strokePoints.Add(point);
        edit.BeginPreview(_tool, point);
        if (_tool == MapToolKind.FloodFill)
        {
            int before = edit.Buffer.Get(point.X, point.Y, EffectiveLayer());
            IReadOnlyList<MapPoint> fill = MapDrawingAlgorithms.FloodFill(edit.Buffer, point, EffectiveLayer(), value => value == before);
            edit.PreviewCells(point, fill, EffectiveLayer(), EffectiveValue(), ReshapeAutotiles());
            _ = CommitGestureAsync(DescriptionForTool());
            return;
        }
        UpdateGesturePreview(point);
    }

    private void OnTilePointerMove(object? sender, MapTilePointerEventArgs e)
    {
        if (_tool == MapToolKind.Events && _gestureActive && e.Button == MouseButtons.Left)
        {
            _gestureCurrent = new MapPoint(e.X, e.Y);
            UpdateEventDragPreview();
            return;
        }
        if (!_gestureActive || e.Button != MouseButtons.Left) return;
        MapPoint point = new(e.X, e.Y);
        if (point == _gestureCurrent) return;
        _gestureCurrent = point;
        if (_tool == MapToolKind.Selection)
        {
            UpdateSelectionPreview();
            return;
        }
        if (_tool is MapToolKind.Pencil or MapToolKind.Pattern or MapToolKind.Random or MapToolKind.Eraser or MapToolKind.Region or MapToolKind.Shadow)
        {
            foreach (MapPoint cell in MapDrawingAlgorithms.Line(_lastStrokePoint, point)) _strokePoints.Add(cell);
            _lastStrokePoint = point;
        }
        UpdateGesturePreview(point);
    }

    private void OnTilePointerUp(object? sender, MapTilePointerEventArgs e)
    {
        if (_tool == MapToolKind.Events && _gestureActive && e.Button == MouseButtons.Left)
        {
            _gestureCurrent = new MapPoint(e.X, e.Y);
            _ = CommitEventMoveAsync();
            return;
        }
        if (!_gestureActive || e.Button != MouseButtons.Left) return;
        _gestureCurrent = new MapPoint(e.X, e.Y);
        if (_tool == MapToolKind.Selection)
        {
            _editSession?.SetSelection(MapRect.FromPoints(_gestureStart, _gestureCurrent));
            _gestureActive = false;
            UpdateSelectionOverlay();
            return;
        }
        UpdateGesturePreview(_gestureCurrent);
        _ = CommitGestureAsync(DescriptionForTool());
    }

    private void UpdateGesturePreview(MapPoint current)
    {
        EditableMapSession? edit = _editSession;
        if (edit is null || edit.Preview is null) return;
        IEnumerable<MapPoint> cells = _tool switch
        {
            MapToolKind.Pencil or MapToolKind.Pattern or MapToolKind.Random or MapToolKind.Eraser or MapToolKind.Region or MapToolKind.Shadow => _strokePoints,
            MapToolKind.Rectangle => MapDrawingAlgorithms.Rectangle(_gestureStart, current, _filledButton.Checked),
            MapToolKind.Ellipse => MapDrawingAlgorithms.Ellipse(_gestureStart, current, _filledButton.Checked),
            MapToolKind.Line => MapDrawingAlgorithms.Line(_gestureStart, current),
            _ => [_gestureStart]
        };
        if (_tool == MapToolKind.Pencil && _selectedBrush is { } brush)
        {
            edit.PreviewValues(
                current,
                ExpandBrushValues(cells, brush),
                EffectiveLayer(),
                ReshapeAutotiles());
        }
        else if (_tool is MapToolKind.Pattern or MapToolKind.Random)
        {
            IEnumerable<(MapPoint Point, int Value)> values = cells.Select(point => (point, BrushValue(point)));
            edit.PreviewValues(current, values, EffectiveLayer(), ReshapeAutotiles());
        }
        else
        {
            edit.PreviewCells(current, cells, EffectiveLayer(), EffectiveValue(), ReshapeAutotiles());
        }
    }

    private static IEnumerable<(MapPoint Point, int Value)> ExpandBrushValues(
        IEnumerable<MapPoint> anchors,
        MapTileBrushSelection brush)
    {
        foreach (MapPoint anchor in anchors)
        {
            for (int y = 0; y < brush.Height; y++)
            {
                for (int x = 0; x < brush.Width; x++)
                {
                    int index = y * brush.Width + x;
                    if (index < 0 || index >= brush.TileIds.Count) continue;
                    yield return (
                        new MapPoint(anchor.X + x - brush.AnchorX, anchor.Y + y - brush.AnchorY),
                        brush.TileIds[index]);
                }
            }
        }
    }

    private int BrushValue(MapPoint point)
    {
        if (MvTileRules.IsAutotile(_selectedValue)) return _selectedValue;
        return _tool switch
        {
            MapToolKind.Pattern => Math.Min(8191, _selectedValue + ((point.X + point.Y) & 1)),
            MapToolKind.Random => Math.Min(8191, _selectedValue + (DeterministicHash(point.X, point.Y, _mapId) & 3)),
            _ => _selectedValue
        };
    }

    private static int DeterministicHash(int x, int y, int seed)
    {
        unchecked
        {
            uint value = (uint)seed * 0x9E3779B9u;
            value ^= (uint)x * 0x85EBCA6Bu;
            value = (value << 13) | (value >> 19);
            value ^= (uint)y * 0xC2B2AE35u;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }

    private async Task CommitGestureAsync(string description)
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit is null || context is null)
        {
            CancelGesture();
            return;
        }
        _gestureActive = false;
        if (!edit.Buffer.HasPreview)
        {
            edit.CancelPreview();
            _canvas.ClearEditPreview();
            return;
        }

        MapDataPatch patch = edit.CommitPreview(description);
        try
        {
            _committingEdit = true;
            await using ProjectTransaction transaction = _session.BeginTransaction(description, allowHistoryMerge: _tool == MapToolKind.Pencil);
            transaction.Add(new ApplyMapDataPatchCommand(edit.RelativePath, patch, description));
            await transaction.CommitAsync().ConfigureAwait(true);
            MapRenderSnapshot updated = context.Map.WithData(edit.Buffer.CaptureValues());
            _renderContext = context with { Map = updated };
            _canvas.UpdateMapSnapshot(updated);
            StartMinimapRender(_renderContext);
            _editStatus.Text = $"{description}: {patch.ChangedValueCount:N0} value(s)";
        }
        catch (Exception exception)
        {
            edit.Buffer.ApplyPatch(patch.Reverse());
            MessageBox.Show(this, exception.Message, "Map edit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ScheduleReload();
        }
        finally
        {
            _committingEdit = false;
            _strokePoints.Clear();
            _canvas.ClearEditPreview();
        }
    }

    private void CancelGesture()
    {
        _gestureActive = false;
        _strokePoints.Clear();
        _eventDragOrigins = null;
        _editSession?.CancelPreview();
        _canvas.ClearEditPreview();
        UpdateSelectionOverlay();
    }

    private void OnEditPreviewChanged(object? sender, EventArgs e)
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit?.Preview is null || context is null || edit.Buffer.PreviewValues.Count == 0)
        {
            _canvas.ClearEditPreview();
            return;
        }
        MapDirtyBounds bounds = edit.Buffer.BuildPreviewPatch().DirtyBounds;
        MapRect expanded = Expand(bounds.Rect, context.Map.Width, context.Map.Height, 1);
        PixelRect region = ToPixelRect(expanded);
        _canvas.SetEditPreview(context.Map.WithOverrides(edit.Buffer.PreviewValues), region);
    }

    private void UpdateSelectionPreview()
    {
        MapRect selection = MapRect.FromPoints(_gestureStart, _gestureCurrent);
        _canvas.SelectionRegion = ToPixelRect(selection);
    }

    private void UpdateSelectionOverlay()
    {
        _canvas.SelectionRegion = _editSession?.Selection is MapRect selection ? ToPixelRect(selection) : null;
    }

    private void HandleEventPointerDown(MapPoint point, MapTilePointerEventArgs e)
    {
        LoadedMapRenderContext? context = _renderContext;
        if (context is null) return;
        MapEventRenderSnapshot? hit = context.Map.Events
            .Where(mapEvent => mapEvent.X == point.X && mapEvent.Y == point.Y)
            .OrderByDescending(mapEvent => mapEvent.PriorityType)
            .FirstOrDefault();
        if (e.Button == MouseButtons.Right)
        {
            ShowEventContextMenu(point, hit, e.ClientLocation);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (hit is null)
        {
            if ((e.Modifiers & Keys.Control) == 0) _selectedEventIds.Clear();
            UpdateEventSelectionOverlay();
            UpdateEditStatus();
            return;
        }
        if ((e.Modifiers & Keys.Control) != 0)
        {
            if (!_selectedEventIds.Add(hit.Id)) _selectedEventIds.Remove(hit.Id);
        }
        else if (!_selectedEventIds.Contains(hit.Id))
        {
            _selectedEventIds.Clear();
            _selectedEventIds.Add(hit.Id);
        }
        _gestureActive = true;
        _gestureStart = point;
        _gestureCurrent = point;
        _eventDragOrigins = context.Map.Events
            .Where(mapEvent => _selectedEventIds.Contains(mapEvent.Id))
            .ToDictionary(mapEvent => mapEvent.Id, mapEvent => new MapPoint(mapEvent.X, mapEvent.Y));
        UpdateEventSelectionOverlay();
        UpdateEditStatus();
    }

    private void OnTileDoubleClicked(object? sender, MapTilePointerEventArgs e)
    {
        if (_tool != MapToolKind.Events || e.Button != MouseButtons.Left) return;
        MapPoint point = new(e.X, e.Y);
        MapEventRenderSnapshot? hit = _renderContext?.Map.Events.FirstOrDefault(mapEvent => mapEvent.X == point.X && mapEvent.Y == point.Y);
        if (hit is null) _ = CreateEventAsync(point);
        else
        {
            _selectedEventIds.Clear();
            _selectedEventIds.Add(hit.Id);
            UpdateEventSelectionOverlay();
            _editStatus.Text = $"Selected event {hit.Id:D3}: {hit.Name}";
            OpenEventEditor(hit.Id, hit.Name);
        }
    }

    private void UpdateEventDragPreview()
    {
        if (_eventDragOrigins is null || _eventDragOrigins.Count == 0) return;
        int dx = _gestureCurrent.X - _gestureStart.X;
        int dy = _gestureCurrent.Y - _gestureStart.Y;
        MapRect bounds = default;
        foreach (MapPoint origin in _eventDragOrigins.Values)
        {
            int x = Math.Clamp(origin.X + dx, 0, Math.Max(0, (_renderContext?.Map.Width ?? 1) - 1));
            int y = Math.Clamp(origin.Y + dy, 0, Math.Max(0, (_renderContext?.Map.Height ?? 1) - 1));
            MapRect cell = new(x, y, 1, 1);
            bounds = bounds.IsEmpty ? cell : bounds.Union(cell);
        }
        _canvas.SelectionRegion = ToPixelRect(bounds);
    }

    private async Task CommitEventMoveAsync()
    {
        LoadedMapRenderContext? context = _renderContext;
        Dictionary<int, MapPoint>? origins = _eventDragOrigins;
        _gestureActive = false;
        _eventDragOrigins = null;
        if (context is null || origins is null || origins.Count == 0) return;
        int dx = _gestureCurrent.X - _gestureStart.X;
        int dy = _gestureCurrent.Y - _gestureStart.Y;
        MapEventMove[] moves = origins.Select(pair => new MapEventMove(
            pair.Key,
            pair.Value,
            new MapPoint(
                Math.Clamp(pair.Value.X + dx, 0, context.Map.Width - 1),
                Math.Clamp(pair.Value.Y + dy, 0, context.Map.Height - 1))))
            .Where(move => move.Before != move.After)
            .ToArray();
        if (moves.Length == 0)
        {
            UpdateEventSelectionOverlay();
            return;
        }
        try
        {
            await using ProjectTransaction transaction = _session.BeginTransaction(moves.Length == 1 ? "Move map event" : "Move map events");
            transaction.Add(new MoveMapEventsCommand(context.Map.RelativePath, moves));
            await transaction.CommitAsync().ConfigureAwait(true);
            _editStatus.Text = $"Moved {moves.Length} event(s)";
            await ReloadAsync().ConfigureAwait(true);
            _selectedEventIds.UnionWith(moves.Select(move => move.EventId));
            UpdateEventSelectionOverlay();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Move event failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateEventSelectionOverlay();
        }
    }

    private async Task CreateEventAsync(MapPoint point)
    {
        LoadedMapRenderContext? context = _renderContext;
        if (context is null) return;
        try
        {
            (int eventId, int arrayLength) = await FindFreeEventIdAsync(context.Map.RelativePath).ConfigureAwait(true);
            string raw = CreateBlankEventJson(eventId, point);
            IEditorCommand command = eventId < arrayLength
                ? new SetJsonValueCommand(context.Map.RelativePath, JsonPath.Root.Append("events").Append(eventId), raw, "Create map event")
                : new InsertJsonArrayElementCommand(context.Map.RelativePath, JsonPath.Root.Append("events"), eventId, raw, "Create map event");
            await using ProjectTransaction transaction = _session.BeginTransaction("Create map event");
            transaction.Add(command);
            await transaction.CommitAsync().ConfigureAwait(true);
            _selectedEventIds.Clear();
            _selectedEventIds.Add(eventId);
            await ReloadAsync().ConfigureAwait(true);
            UpdateEventSelectionOverlay();
            OpenEventEditor(eventId, $"EV{eventId:D3}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Create event failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DuplicateEventAsync(int eventId)
    {
        LoadedMapRenderContext? context = _renderContext;
        if (context is null) return;
        try
        {
            ReadOnlyMemory<byte> bytes;
            await using (Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(context.Map.RelativePath).ConfigureAwait(true))
            {
                bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
            }
            System.Text.Json.Nodes.JsonArray events = (System.Text.Json.Nodes.JsonNode.Parse(bytes.Span) as System.Text.Json.Nodes.JsonObject)?["events"] as System.Text.Json.Nodes.JsonArray
                ?? throw new InvalidDataException("The map event array is missing.");
            System.Text.Json.Nodes.JsonObject source = events[eventId]?.DeepClone() as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidDataException($"Event {eventId} is missing.");
            (int newId, int arrayLength) = FindFreeEventId(events);
            source["id"] = newId;
            source["name"] = (source["name"]?.GetValue<string>() ?? $"EV{eventId:D3}") + " Copy";
            source["x"] = Math.Clamp((source["x"]?.GetValue<int>() ?? 0) + 1, 0, context.Map.Width - 1);
            source["y"] = Math.Clamp(source["y"]?.GetValue<int>() ?? 0, 0, context.Map.Height - 1);
            string raw = source.ToJsonString();
            IEditorCommand command = newId < arrayLength
                ? new SetJsonValueCommand(context.Map.RelativePath, JsonPath.Root.Append("events").Append(newId), raw, "Duplicate map event")
                : new InsertJsonArrayElementCommand(context.Map.RelativePath, JsonPath.Root.Append("events"), newId, raw, "Duplicate map event");
            await using ProjectTransaction transaction = _session.BeginTransaction("Duplicate map event");
            transaction.Add(command);
            await transaction.CommitAsync().ConfigureAwait(true);
            _selectedEventIds.Clear();
            _selectedEventIds.Add(newId);
            await ReloadAsync().ConfigureAwait(true);
            UpdateEventSelectionOverlay();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Duplicate event failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedEventsAsync()
    {
        LoadedMapRenderContext? context = _renderContext;
        if (context is null || _selectedEventIds.Count == 0) return;
        if (MessageBox.Show(this, $"Delete {_selectedEventIds.Count} selected event(s)?", "Delete Events", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await using ProjectTransaction transaction = _session.BeginTransaction("Delete map events");
            foreach (int eventId in _selectedEventIds.Order())
            {
                transaction.Add(new SetJsonValueCommand(context.Map.RelativePath, JsonPath.Root.Append("events").Append(eventId), "null", "Delete map event"));
            }
            await transaction.CommitAsync().ConfigureAwait(true);
            _selectedEventIds.Clear();
            await ReloadAsync().ConfigureAwait(true);
            UpdateEventSelectionOverlay();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Delete event failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowEventContextMenu(MapPoint point, MapEventRenderSnapshot? hit, Point clientLocation)
    {
        ContextMenuStrip menu = new();
        if (hit is null)
        {
            menu.Items.Add("Create Event", null, async (_, _) => await CreateEventAsync(point).ConfigureAwait(true));
        }
        else
        {
            // Opening a modal Event Editor directly inside a ToolStrip click callback re-enters
            // WinForms before the popup has completed its native close sequence. Post the request
            // to the canvas queue so it runs immediately after the context menu has closed.
            menu.Items.Add("Edit Event", null, (_, _) =>
                ControlHelpers.Post(_canvas, () => OpenEventEditor(hit.Id, hit.Name)));
            menu.Items.Add($"Select {hit.Id:D3} {hit.Name}", null, (_, _) =>
            {
                _selectedEventIds.Clear();
                _selectedEventIds.Add(hit.Id);
                UpdateEventSelectionOverlay();
            });
            menu.Items.Add("Duplicate", null, async (_, _) => await DuplicateEventAsync(hit.Id).ConfigureAwait(true));
            menu.Items.Add("Delete", null, async (_, _) =>
            {
                _selectedEventIds.Clear();
                _selectedEventIds.Add(hit.Id);
                await DeleteSelectedEventsAsync().ConfigureAwait(true);
            });
        }
        if (_selectedEventIds.Count > 1) menu.Items.Add("Delete Selected Events", null, async (_, _) => await DeleteSelectedEventsAsync().ConfigureAwait(true));
        ControlHelpers.DisposeTransientContextMenuAfterClose(menu, _canvas);
        menu.Show(_canvas, clientLocation);
    }

    private void UpdateEventSelectionOverlay()
    {
        LoadedMapRenderContext? context = _renderContext;
        if (context is null || _selectedEventIds.Count == 0)
        {
            _canvas.SelectionRegion = null;
            return;
        }
        MapRect bounds = default;
        foreach (MapEventRenderSnapshot mapEvent in context.Map.Events.Where(item => _selectedEventIds.Contains(item.Id)))
        {
            MapRect cell = new(mapEvent.X, mapEvent.Y, 1, 1);
            bounds = bounds.IsEmpty ? cell : bounds.Union(cell);
        }
        _canvas.SelectionRegion = ToPixelRect(bounds);
        UpdateEditStatus();
    }

    private async Task<(int Id, int ArrayLength)> FindFreeEventIdAsync(string relativePath)
    {
        ReadOnlyMemory<byte> bytes;
        await using (Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(relativePath).ConfigureAwait(true))
        {
            bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        }
        using JsonDocument json = JsonDocument.Parse(bytes);
        JsonElement events = json.RootElement.GetProperty("events");
        for (int index = 1; index < events.GetArrayLength(); index++) if (events[index].ValueKind == JsonValueKind.Null) return (index, events.GetArrayLength());
        return (events.GetArrayLength(), events.GetArrayLength());
    }

    private static (int Id, int ArrayLength) FindFreeEventId(System.Text.Json.Nodes.JsonArray events)
    {
        for (int index = 1; index < events.Count; index++) if (events[index] is null) return (index, events.Count);
        return (events.Count, events.Count);
    }

    private static string CreateBlankEventJson(int id, MapPoint point)
    {
        var mapEvent = new
        {
            id,
            name = $"EV{id:D3}",
            note = "",
            pages = new[]
            {
                new
                {
                    conditions = new { actorId = 1, actorValid = false, itemId = 1, itemValid = false, selfSwitchCh = "A", selfSwitchValid = false, switch1Id = 1, switch1Valid = false, switch2Id = 1, switch2Valid = false, variableId = 1, variableValid = false, variableValue = 0 },
                    directionFix = false,
                    image = new { tileId = 0, characterName = "", direction = 2, pattern = 1, characterIndex = 0 },
                    list = new[] { new { code = 0, indent = 0, parameters = Array.Empty<object>() } },
                    moveFrequency = 3,
                    moveRoute = new { list = new[] { new { code = 0, parameters = Array.Empty<object>() } }, repeat = true, skippable = false, wait = false },
                    moveSpeed = 3,
                    moveType = 0,
                    priorityType = 1,
                    stepAnime = false,
                    through = false,
                    trigger = 0,
                    walkAnime = true
                }
            },
            x = point.X,
            y = point.Y
        };
        return JsonSerializer.Serialize(mapEvent);
    }

    private void PickValue(MapPoint point)
    {
        EditableMapSession? edit = _editSession;
        if (edit is null) return;
        int value = edit.Buffer.Get(point.X, point.Y, EffectiveLayer());
        _selectedBrush = null;
        _selectedValue = value;
        _valueEditor.Value = Math.Clamp(value, decimal.ToInt32(_valueEditor.Minimum), decimal.ToInt32(_valueEditor.Maximum));
        UpdateEditStatus();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        MapTilePaletteSelection? tile = _selectionService.Current as MapTilePaletteSelection;
        MapTileBrushSelection? brush = _selectionService.Current as MapTileBrushSelection;
        if (tile is null && brush is null) return;

        _selectedBrush = brush;
        _selectedValue = Math.Clamp(tile?.TileId ?? brush!.PrimaryTileId, 0, 8191);
        _selectedSheet = tile?.SheetName ?? brush!.SheetName;
        int suggestedLayer = tile?.SuggestedLayer ?? brush!.SuggestedLayer;
        _valueEditor.Maximum = 8191;
        _valueEditor.Value = _selectedValue;
        if (_tool is not MapToolKind.Region and not MapToolKind.Shadow)
        {
            _selectedLayer = Math.Clamp(suggestedLayer, 0, 3);
            _layerChoice.SelectedIndex = _selectedLayer;
            SelectTool(MapToolKind.Pencil);
        }
        UpdateEditStatus();
    }

    private async Task CopySelectionAsync()
    {
        MapClipboardPayload? payload = await BuildSelectionPayloadAsync().ConfigureAwait(true);
        if (payload is null) return;
        Clipboard.SetText(payload.Serialize(), TextDataFormat.UnicodeText);
        _editStatus.Text = $"Copied {payload.Width}×{payload.Height} map selection with {payload.Events.Count} event(s)";
    }

    private async Task<MapClipboardPayload?> BuildSelectionPayloadAsync()
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit?.Selection is not MapRect selection || context is null) return null;
        MapSelectionData data = MapSelectionData.Capture(edit.Buffer, selection, Enumerable.Range(0, 6));
        List<MapClipboardEvent> clipboardEvents = [];
        ReadOnlyMemory<byte> bytes;
        await using (Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(context.Map.RelativePath).ConfigureAwait(true))
        {
            bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        }
        System.Text.Json.Nodes.JsonArray events = (System.Text.Json.Nodes.JsonNode.Parse(bytes.Span) as System.Text.Json.Nodes.JsonObject)?["events"] as System.Text.Json.Nodes.JsonArray ?? [];
        for (int index = 1; index < events.Count; index++)
        {
            if (events[index] is not System.Text.Json.Nodes.JsonObject mapEvent) continue;
            int x = mapEvent["x"]?.GetValue<int>() ?? 0;
            int y = mapEvent["y"]?.GetValue<int>() ?? 0;
            MapPoint point = new(x, y);
            if (!selection.Contains(point)) continue;
            int id = mapEvent["id"]?.GetValue<int>() ?? index;
            clipboardEvents.Add(new MapClipboardEvent(id, x - selection.X, y - selection.Y, mapEvent.ToJsonString()));
        }
        return new MapClipboardPayload
        {
            Format = MapClipboardPayload.CurrentFormat,
            SourceProjectId = Guid.Empty,
            SourceMapId = _mapId,
            SourceTilesetId = context.Tileset.Id,
            TilesetFingerprint = string.Join("|", context.Tileset.Names),
            Width = selection.Width,
            Height = selection.Height,
            AnchorX = 0,
            AnchorY = 0,
            Layers = data.Layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
            Events = clipboardEvents
        };
    }

    private async Task CutSelectionAsync()
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit?.Selection is not MapRect selection || context is null) return;
        MapClipboardPayload? payload = await BuildSelectionPayloadAsync().ConfigureAwait(true);
        if (payload is null) return;
        Clipboard.SetText(payload.Serialize(), TextDataFormat.UnicodeText);
        List<MapDataChange> changes = [];
        for (int layer = 0; layer < edit.Buffer.LayerCount; layer++)
        {
            foreach (MapPoint point in selection.Cells())
            {
                if (!edit.Buffer.IsInside(point.X, point.Y)) continue;
                int before = edit.Buffer.Get(point.X, point.Y, layer);
                if (before == 0) continue;
                changes.Add(new MapDataChange(edit.Buffer.GetIndex(point.X, point.Y, layer), before, 0));
            }
        }
        List<IEditorCommand> extras = payload.Events
            .Select(mapEvent => (IEditorCommand)new SetJsonValueCommand(
                context.Map.RelativePath,
                JsonPath.Root.Append("events").Append(mapEvent.SourceId),
                "null",
                "Cut map event"))
            .ToList();
        if (changes.Count == 0 && extras.Count == 0) return;
        MapDataPatch? patch = changes.Count == 0 ? null : MapDataPatch.FromChanges(changes, edit.Buffer.Width, edit.Buffer.Height);
        await CommitCompositeMapEditAsync(patch, extras, "Cut map selection", reloadAfterCommit: extras.Count > 0).ConfigureAwait(true);
    }

    private async Task CommitDirectPatchAsync(MapDataPatch patch, string description) =>
        await CommitCompositeMapEditAsync(patch, [], description, reloadAfterCommit: false).ConfigureAwait(true);

    private async Task CommitCompositeMapEditAsync(
        MapDataPatch? patch,
        IReadOnlyCollection<IEditorCommand> extraCommands,
        string description,
        bool reloadAfterCommit)
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit is null || context is null || (patch is null && extraCommands.Count == 0)) return;
        if (patch is not null) edit.Buffer.ApplyPatch(patch);
        try
        {
            _committingEdit = true;
            await using ProjectTransaction transaction = _session.BeginTransaction(description);
            if (patch is not null) transaction.Add(new ApplyMapDataPatchCommand(edit.RelativePath, patch, description));
            foreach (IEditorCommand command in extraCommands) transaction.Add(command);
            await transaction.CommitAsync().ConfigureAwait(true);
            if (reloadAfterCommit)
            {
                await ReloadAsync().ConfigureAwait(true);
            }
            else if (patch is not null)
            {
                MapRenderSnapshot updated = context.Map.WithData(edit.Buffer.CaptureValues());
                _renderContext = context with { Map = updated };
                _canvas.UpdateMapSnapshot(updated);
                StartMinimapRender(_renderContext);
            }
            int changed = patch?.ChangedValueCount ?? 0;
            _editStatus.Text = $"{description}: {changed:N0} map value(s), {extraCommands.Count:N0} extra change(s)";
        }
        catch (Exception exception)
        {
            if (patch is not null) edit.Buffer.ApplyPatch(patch.Reverse());
            MessageBox.Show(this, exception.Message, description + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ScheduleReload();
        }
        finally
        {
            _committingEdit = false;
        }
    }

    private void TransformClipboard(Func<MapSelectionData, MapSelectionData> transform, string status)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;
        try
        {
            MapClipboardPayload payload = MapClipboardPayload.Deserialize(Clipboard.GetText(TextDataFormat.UnicodeText));
            MapSelectionData transformed = transform(payload.ToSelection());
            List<MapClipboardEvent> transformedEvents = payload.Events.Select(mapEvent => TransformClipboardEvent(mapEvent, payload.Width, payload.Height, transformed.Bounds.Width, transformed.Bounds.Height, status)).ToList();
            MapClipboardPayload updated = payload with
            {
                Width = transformed.Bounds.Width,
                Height = transformed.Bounds.Height,
                Layers = transformed.Layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
                Events = transformedEvents
            };
            Clipboard.SetText(updated.Serialize(), TextDataFormat.UnicodeText);
            _editStatus.Text = status;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Clipboard transform failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static MapClipboardEvent TransformClipboardEvent(
        MapClipboardEvent mapEvent,
        int oldWidth,
        int oldHeight,
        int newWidth,
        int newHeight,
        string operation)
    {
        int x = mapEvent.X;
        int y = mapEvent.Y;
        if (operation.StartsWith("Rotated", StringComparison.Ordinal))
        {
            (x, y) = (oldHeight - y - 1, x);
        }
        else if (operation.Contains("horizontally", StringComparison.OrdinalIgnoreCase))
        {
            x = oldWidth - x - 1;
        }
        else if (operation.Contains("vertically", StringComparison.OrdinalIgnoreCase))
        {
            y = oldHeight - y - 1;
        }
        x = Math.Clamp(x, 0, Math.Max(0, newWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, newHeight - 1));
        return mapEvent with { X = x, Y = y };
    }

    private async Task ExportSelectionAsync()
    {
        MapClipboardPayload? payload = await BuildSelectionPayloadAsync().ConfigureAwait(true);
        if (payload is null) return;
        using SaveFileDialog dialog = new()
        {
            Filter = "Playroma Map Section (*.playromamap)|*.playromamap|JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"Map{_mapId:D3}-selection.playromamap",
            AddExtension = true,
            DefaultExt = "playromamap"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await File.WriteAllTextAsync(dialog.FileName, payload.Serialize()).ConfigureAwait(true);
        _editStatus.Text = "Exported map selection";
    }

    private async Task SaveSelectionAsPrefabAsync()
    {
        MapClipboardPayload? payload = await BuildSelectionPayloadAsync().ConfigureAwait(true);
        if (payload is null) return;
        string? name = TextPromptDialog.Show(this, "Save Map Prefab", "Prefab name:", $"Map{_mapId:D3} Selection");
        if (string.IsNullOrWhiteSpace(name)) return;
        string safeName = string.Concat(name.Trim().Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string relativePath = $".playroma/prefabs/maps/{safeName}.playromamap";
        try
        {
            await using ProjectTransaction transaction = _session.BeginTransaction("Save map prefab");
            transaction.Add(new WriteGeneratedProjectFileCommand(relativePath, System.Text.Encoding.UTF8.GetBytes(payload.Serialize()), "Save map prefab"));
            await transaction.CommitAsync().ConfigureAwait(true);
            _editStatus.Text = $"Staged prefab {relativePath}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Save prefab failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task PasteSelectionAtViewportCentreAsync()
    {
        EditableMapSession? edit = _editSession;
        LoadedMapRenderContext? context = _renderContext;
        if (edit is null || context is null || !Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;
        try
        {
            MapClipboardPayload payload = MapClipboardPayload.Deserialize(Clipboard.GetText(TextDataFormat.UnicodeText));
            if (payload.SourceTilesetId != context.Tileset.Id || !string.Equals(payload.TilesetFingerprint, string.Join("|", context.Tileset.Names), StringComparison.Ordinal))
            {
                DialogResult result = MessageBox.Show(this,
                    "The copied area uses a different tileset identity. Paste raw tile IDs anyway?",
                    "Tileset mismatch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) return;
            }
            PixelRect visible = _canvas.VisibleMapRegion;
            MapPoint destination = new(
                Math.Clamp(visible.X / 48 + visible.Width / 96 - payload.Width / 2, 0, Math.Max(0, edit.Buffer.Width - 1)),
                Math.Clamp(visible.Y / 48 + visible.Height / 96 - payload.Height / 2, 0, Math.Max(0, edit.Buffer.Height - 1)));
            edit.BeginPreview(MapToolKind.Stamp, destination);
            payload.ToSelection().PreviewPaste(edit.Buffer, destination);
            MapDataPatch? patch = edit.Buffer.HasPreview ? edit.Buffer.BuildPreviewPatch() : null;
            edit.CancelPreview();
            List<IEditorCommand> eventCommands = await BuildPastedEventCommandsAsync(payload, destination, context).ConfigureAwait(true);
            await CommitCompositeMapEditAsync(patch, eventCommands, "Paste map selection", reloadAfterCommit: eventCommands.Count > 0).ConfigureAwait(true);
            edit.SetSelection(new MapRect(destination.X, destination.Y, payload.Width, payload.Height).Intersect(new MapRect(0, 0, edit.Buffer.Width, edit.Buffer.Height)));
            UpdateSelectionOverlay();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Paste failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<List<IEditorCommand>> BuildPastedEventCommandsAsync(
        MapClipboardPayload payload,
        MapPoint destination,
        LoadedMapRenderContext context)
    {
        List<IEditorCommand> result = [];
        if (payload.Events.Count == 0) return result;
        ReadOnlyMemory<byte> bytes;
        await using (Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(context.Map.RelativePath).ConfigureAwait(true))
        {
            bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        }
        System.Text.Json.Nodes.JsonArray events = (System.Text.Json.Nodes.JsonNode.Parse(bytes.Span) as System.Text.Json.Nodes.JsonObject)?["events"] as System.Text.Json.Nodes.JsonArray
            ?? throw new InvalidDataException("The destination map event array is missing.");
        Queue<int> holes = new(Enumerable.Range(1, Math.Max(0, events.Count - 1)).Where(index => events[index] is null));
        int appendIndex = events.Count;
        foreach (MapClipboardEvent sourceEvent in payload.Events)
        {
            int x = destination.X + sourceEvent.X;
            int y = destination.Y + sourceEvent.Y;
            if (!editBounds(context.Map).Contains(new MapPoint(x, y))) continue;
            int id = holes.Count > 0 ? holes.Dequeue() : appendIndex++;
            System.Text.Json.Nodes.JsonObject raw = System.Text.Json.Nodes.JsonNode.Parse(sourceEvent.RawJson) as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidDataException("A clipboard event is not a JSON object.");
            raw["id"] = id;
            raw["x"] = x;
            raw["y"] = y;
            string json = raw.ToJsonString();
            IEditorCommand command = id < events.Count
                ? new SetJsonValueCommand(context.Map.RelativePath, JsonPath.Root.Append("events").Append(id), json, "Paste map event")
                : new InsertJsonArrayElementCommand(context.Map.RelativePath, JsonPath.Root.Append("events"), id, json, "Paste map event");
            result.Add(command);
        }
        return result;

        static MapRect editBounds(MapRenderSnapshot map) => new(0, 0, map.Width, map.Height);
    }

    private async Task ClearSelectionAsync(MapRect selection)
    {
        EditableMapSession? edit = _editSession;
        if (edit is null) return;
        edit.BeginPreview(MapToolKind.Eraser, new MapPoint(selection.X, selection.Y));
        edit.PreviewCells(new MapPoint(selection.Right - 1, selection.Bottom - 1), selection.Cells(), EffectiveLayer(), 0, ReshapeAutotiles());
        await CommitGestureAsync("Clear map selection").ConfigureAwait(true);
    }

    private int EffectiveLayer() => _tool switch
    {
        MapToolKind.Region => 5,
        MapToolKind.Shadow => 4,
        _ when _editMode == MapEditMode.ExplicitLayer => _selectedLayer,
        _ => AutomaticLayer()
    };

    private int AutomaticLayer()
    {
        if (MvTileRules.IsTileA1(_selectedValue) || MvTileRules.IsTileA2(_selectedValue) || MvTileRules.IsTileA5(_selectedValue)) return 0;
        if (MvTileRules.IsTileA3(_selectedValue) || MvTileRules.IsTileA4(_selectedValue)) return 1;
        if (_selectedSheet is "B" or "C" or "D" or "E") return 2;
        return Math.Clamp(_selectedLayer, 0, 3);
    }

    private int EffectiveValue() => _tool == MapToolKind.Eraser ? 0 : _selectedValue;
    private bool ReshapeAutotiles() => EffectiveLayer() is >= 0 and <= 3;

    private string DescriptionForTool() => _tool switch
    {
        MapToolKind.Pencil => "Paint map tiles",
        MapToolKind.Rectangle => "Paint map rectangle",
        MapToolKind.Ellipse => "Paint map ellipse",
        MapToolKind.Line => "Paint map line",
        MapToolKind.Pattern => "Paint map pattern",
        MapToolKind.Random => "Paint randomized map tiles",
        MapToolKind.FloodFill => "Flood fill map",
        MapToolKind.Eraser => "Erase map tiles",
        MapToolKind.Region => "Paint map regions",
        MapToolKind.Shadow => "Paint map shadows",
        _ => "Edit map"
    };

    private void UpdateEditStatus()
    {
        _editStatus.Text = _tool == MapToolKind.Events
            ? $"Events — {_selectedEventIds.Count} selected"
            : $"{_tool} — {_editMode} — layer {EffectiveLayer() + 1} — value {_selectedValue}" + (_filledButton.Checked ? " — filled" : string.Empty);
    }

    private void OpenEventEditor(int eventId, string? eventName)
    {
        MvMapDescriptor? map = _session.Workspace.Snapshot.Maps.FirstOrDefault(item => item.Id == _mapId);
        string relativePath = map?.RelativePath ?? _renderContext?.Map.RelativePath ?? _relativePath ?? $"data/Map{_mapId:D3}.json";
        string mapName = map?.Name ?? _renderContext?.Map.EditorName ?? _editorName ?? $"Map {_mapId:D3}";
        DocumentRequested?.Invoke(this, new DocumentRequestEventArgs(new DocumentRequest(
            EditorDocumentKind.EventEditor,
            $"event:map:{_mapId}:{eventId}",
            relativePath,
            MapId: _mapId,
            DisplayTitle: $"{mapName} — {eventId:D3} {eventName}",
            EventSurface: "MapEvent",
            EventId: eventId,
            EventPageIndex: 0)));
    }

    private void OpenProperties()
    {
        MvMapDescriptor? map = _session.Workspace.Snapshot.Maps.FirstOrDefault(item => item.Id == _mapId);
        string relativePath = map?.RelativePath ?? _renderContext?.Map.RelativePath ?? _relativePath ?? $"data/Map{_mapId:D3}.json";
        string name = map?.Name ?? _renderContext?.Map.EditorName ?? _editorName ?? $"Map {_mapId:D3}";
        DocumentRequested?.Invoke(this, new DocumentRequestEventArgs(new DocumentRequest(
            EditorDocumentKind.MapProperties,
            $"map-properties:{_mapId}",
            relativePath,
            MapId: _mapId,
            DisplayTitle: name + " Properties")));
    }

    private void OnAssetsChanged(object? sender, EventArgs e) => ScheduleReload();

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (_committingEdit) return;
        MvMapDescriptor? map = _session.Workspace.Snapshot.Maps.FirstOrDefault(item => item.Id == _mapId);
        string? activeMapPath = map?.RelativePath ?? _renderContext?.Map.RelativePath ?? _relativePath;
        MvDatabaseFileSummary? tilesets = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Tilesets);
        if (e.Targets.Any(target =>
                string.Equals(target.NormalizedRelativePath, activeMapPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(target.NormalizedRelativePath, tilesets?.RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            ScheduleReload();
        }
    }

    private void ScheduleReload()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ScheduleReload));
            return;
        }
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private void OnCanvasZoomChanged(object? sender, EventArgs e)
    {
        _zoomStatus.Text = $"{_canvas.Zoom * 100:0.#}%";
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCanvasViewportChanged(object? sender, EventArgs e)
    {
        _minimap.SetViewport(_canvas.VisibleMapRegion);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCoordinateChanged(object? sender, MapCoordinateEventArgs e)
    {
        string eventText = e.EventId.HasValue ? $"  Event {e.EventId.Value}" : string.Empty;
        _coordinateStatus.Text = $"{e.X}, {e.Y}  Region {e.RegionId}  Shadow 0x{e.ShadowBits:X}{eventText}";
        CoordinateChanged?.Invoke(this, e);
    }

    private static PixelRect ToPixelRect(MapRect rect) => rect.IsEmpty
        ? default
        : new PixelRect(
            checked(rect.X * MvTileRules.TileWidth),
            checked(rect.Y * MvTileRules.TileHeight),
            checked(rect.Width * MvTileRules.TileWidth),
            checked(rect.Height * MvTileRules.TileHeight));

    private static MapRect Expand(MapRect rect, int width, int height, int amount)
    {
        if (rect.IsEmpty) return rect;
        int left = Math.Max(0, rect.Left - amount);
        int top = Math.Max(0, rect.Top - amount);
        int right = Math.Min(width, rect.Right + amount);
        int bottom = Math.Min(height, rect.Bottom + amount);
        return new MapRect(left, top, right - left, bottom - top);
    }

    private static MapRect BoundsOf(IReadOnlyList<MapPoint> cells)
    {
        if (cells.Count == 0) return default;
        int left = cells.Min(point => point.X);
        int top = cells.Min(point => point.Y);
        int right = cells.Max(point => point.X) + 1;
        int bottom = cells.Max(point => point.Y) + 1;
        return new MapRect(left, top, right - left, bottom - top);
    }
}

public sealed class DocumentRequestEventArgs : EventArgs
{
    public DocumentRequestEventArgs(DocumentRequest request) => Request = request;
    public DocumentRequest Request { get; }
}

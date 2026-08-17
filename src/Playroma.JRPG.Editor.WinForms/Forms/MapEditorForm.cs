using System.Diagnostics;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.MapEditing;
using Playroma.JRPG.Editor.WinForms.Rendering;
using Playroma.JRPG.Editor.WinForms.ToolWindows;
using Playroma.JRPG.Editor.WinForms.Visual;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Primary project workspace. It owns one active map surface, the RPG Maker-style
/// tileset/map browser, map and event modes, and routes every non-map editor into an
/// independent top-level window.
/// </summary>
public sealed class MapEditorForm : Form, IProjectWorkspaceHost, IEditorCommandTarget
{
    private static readonly WindowRequest PlacementRequest = new(
        EditorWindowKind.MapEditor,
        "map-editor",
        DisplayTitle: "Playroma jRPG",
        Scope: WindowScope.Project,
        PlacementId: "MapEditor");

    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly ICommandService _commands;
    private readonly ISelectionService _selection;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IOutputService _output;
    private readonly IProjectFileWatchService _fileWatcher;
    private readonly IWinFormsWindowService _windows;
    private readonly IEditorApplicationController _applicationController;
    private readonly WinFormsThemeService _theme;
    private readonly WinFormsWindowPlacementService _placement;

    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _mainToolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStrip _drawToolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _mapStatus = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "No map loaded"
    };
    private readonly ToolStripStatusLabel _coordinateStatus = new() { Text = "—" };
    private readonly ToolStripStatusLabel _modeStatus = new() { Text = "Map / Pencil" };
    private readonly ToolStripStatusLabel _zoomStatus = new() { Text = "100%" };
    private readonly ToolStripStatusLabel _dirtyStatus = new() { Text = string.Empty };

    private readonly SplitContainer _mainSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Vertical, 310, 220, 500, splitterWidth: 6);
    private readonly SplitContainer _leftSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 430, 220, 160, splitterWidth: 6);
    private readonly Panel _surfaceHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(29, 31, 34) };
    private readonly Label _surfaceMessage = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Select a map from the hierarchy.",
        BackColor = Color.FromArgb(29, 31, 34),
        ForeColor = Color.White
    };
    private readonly Panel _loadingOverlay = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(29, 31, 34),
        Visible = false
    };
    private readonly Label _loadingText = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Loading map…",
        ForeColor = Color.White,
        BackColor = Color.Transparent
    };

    private readonly MapHierarchyControl _mapHierarchy;
    private readonly TilesetPaletteControl _tilesetPalette;
    private readonly Dictionary<MapToolKind, ToolStripButton> _toolButtons = [];
    private readonly ToolStripButton _mapModeButton = new("Map") { CheckOnClick = true, Checked = true };
    private readonly ToolStripButton _eventModeButton = new("Events") { CheckOnClick = true };
    private readonly ToolStripComboBox _layerChoice = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 105
    };
    private readonly NumericUpDown _valueEditor = new()
    {
        Minimum = 0,
        Maximum = 8191,
        Width = 72
    };
    private readonly System.Windows.Forms.Timer _mapSelectionTimer = new() { Interval = 125 };
    private readonly Dictionary<string, List<ToolStripItem>> _commandItems = new(StringComparer.OrdinalIgnoreCase);

    private MapEditorController? _mapController;
    private int? _pendingMapId;
    private bool _allowClose;
    private bool _closeRequestInProgress;
    private bool _restoringSettings;
    private bool _suppressMapSelection;
    private bool _disposed;

    public MapEditorForm(
        ShellServiceContainer services,
        IEditorApplicationController applicationController,
        WinFormsThemeService theme,
        WinFormsWindowPlacementService placement)
    {
        ArgumentNullException.ThrowIfNull(services);
        _applicationController = applicationController ?? throw new ArgumentNullException(nameof(applicationController));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _workspace = services.GetRequired<IWorkspaceService>();
        _settings = services.GetRequired<ISettingsService>();
        _commands = services.GetRequired<ICommandService>();
        _selection = services.GetRequired<ISelectionService>();
        _navigation = services.GetRequired<INavigationService>();
        _notifications = services.GetRequired<INotificationService>();
        _output = services.GetRequired<IOutputService>();
        _fileWatcher = services.GetRequired<IProjectFileWatchService>();
        _windows = services.GetRequired<IWinFormsWindowService>();

        Text = "Playroma jRPG";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 700);
        Size = new Size(1440, 900);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        AllowDrop = true;
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        _mainSplit.FixedPanel = FixedPanel.Panel1;

        _mapHierarchy = AccessibilityMetadata.Name(
            new MapHierarchyControl(_workspace, _selection) { Dock = DockStyle.Fill },
            "Map hierarchy",
            "Select a project map. Press Enter to open the selected map.",
            AccessibleRole.Outline);
        _tilesetPalette = AccessibilityMetadata.Name(
            new TilesetPaletteControl(_workspace, _selection) { Dock = DockStyle.Fill },
            "Tileset palette",
            "Select tiles for the active map drawing tool.",
            AccessibleRole.Pane);
        _leftSplit.Panel1.Controls.Add(_tilesetPalette);
        _leftSplit.Panel2.Controls.Add(_mapHierarchy);
        _mainSplit.Panel1.Controls.Add(_leftSplit);

        _surfaceHost.Controls.Add(_surfaceMessage);
        _loadingOverlay.Controls.Add(_loadingText);
        _surfaceHost.Controls.Add(_loadingOverlay);
        _mainSplit.Panel2.Controls.Add(_surfaceHost);

        _status.Items.AddRange([_mapStatus, _coordinateStatus, _modeStatus, _zoomStatus, _dirtyStatus]);
        BuildMenus();
        BuildMainToolbar();
        BuildDrawingToolbar();

        Controls.Add(_mainSplit);
        Controls.Add(_status);
        Controls.Add(_drawToolbar);
        Controls.Add(_mainToolbar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        WireEvents();
        ApplyVisualTheme();
        DpiChanged += (_, _) => ApplyVisualTheme();
        Shown += (_, _) => AccessibilityMetadata.ApplyFormDefaults(this);
    }

    public Form Form => this;

    public void ApplyLoadedSettings(bool resetLayout = false)
    {
        _restoringSettings = true;
        try
        {
            _placement.Restore(this, PlacementRequest);
            MapEditorLayoutSettings layout = resetLayout
                ? new MapEditorLayoutSettings()
                : _settings.Current.EffectiveMapEditorLayout;

            ControlHelpers.SetSplitterDistanceSafe(_mainSplit, layout.LeftPanelWidth);
            ControlHelpers.SetSplitterDistanceSafe(_leftSplit, layout.PalettePanelHeight);
            _mainSplit.Panel1Collapsed = layout.LeftPanelCollapsed;
            _mapHierarchy.SearchText = layout.SearchText;
            _mapHierarchy.RestoreExpandedMapIds(layout.EffectiveExpandedMapIds);
            _tilesetPalette.PaletteZoomPercent = layout.PaletteZoomPercent;
            _tilesetPalette.SelectedPaletteTab = layout.SelectedPaletteTab;
        }
        finally
        {
            _restoringSettings = false;
        }
        ApplyVisualTheme();
        UpdateCommandStates();
    }

    public async Task InitializeProjectAsync(CancellationToken cancellationToken = default)
    {
        var session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("The Map Editor cannot initialize without an open project.");
        MapEditorLayoutSettings layout = _settings.Current.EffectiveMapEditorLayout;
        MapOverlayOptions defaultOverlays = (MapOverlayOptions)layout.OverlayFlags;
        _mapController = new MapEditorController(session, _selection, defaultOverlays, layout.MinimapVisible);
        _mapController.RestoreViewStates(layout.EffectiveMapViews.Select(ToMapViewState));
        _mapController.ActiveMapChanged += OnActiveMapChanged;
        _mapController.StateChanged += OnMapEditorStateChanged;

        _mapHierarchy.Rebuild();
        _mapHierarchy.RestoreExpandedMapIds(layout.EffectiveExpandedMapIds);
        int? requested = layout.ActiveMapId;
        int? initialMapId = session.Workspace.Snapshot.Maps.Any(map => map.Id == requested)
            ? requested
            : session.Workspace.Snapshot.Maps.OrderBy(map => map.Order).ThenBy(map => map.Id).FirstOrDefault()?.Id;
        if (initialMapId.HasValue)
        {
            SelectMapInHierarchy(initialMapId.Value);
            await OpenMapAsync(initialMapId.Value, null, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        else
        {
            _surfaceMessage.Text = "This project does not contain any maps.";
            _surfaceMessage.Visible = true;
        }
        UpdateWorkspaceUi();
    }

    public string? CapturePrimaryWorkspaceStateJson()
    {
        if (_mapController is null) return null;
        MapEditorWorkspacePayload payload = new(
            _mapController.ActiveMapId,
            _mapController.CaptureViewStates(),
            Version: 1);
        return JsonSerializer.Serialize(payload);
    }

    public async Task RestorePrimaryWorkspaceStateJsonAsync(
        string? stateJson,
        CancellationToken cancellationToken = default)
    {
        if (_mapController is null || string.IsNullOrWhiteSpace(stateJson)) return;
        MapEditorWorkspacePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MapEditorWorkspacePayload>(stateJson);
        }
        catch (JsonException)
        {
            return;
        }
        if (payload is null || payload.Version > 1) return;
        _mapController.RestoreViewStates(payload.ViewStates ?? []);
        if (payload.ActiveMapId is int mapId
            && _workspace.CurrentSession?.Workspace.Snapshot.Maps.Any(map => map.Id == mapId) == true)
        {
            SelectMapInHierarchy(mapId);
            await OpenMapAsync(
                mapId,
                new MapEditorNavigationTarget(mapId),
                recordHistory: false,
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
    }

    public Task<bool> CanCloseProjectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public async Task CloseProjectContentForcedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureSettings();
        if (_mapController is not null)
        {
            _mapController.ActiveMapChanged -= OnActiveMapChanged;
            _mapController.StateChanged -= OnMapEditorStateChanged;
            await _mapController.DisposeAsync().ConfigureAwait(true);
            _mapController = null;
        }
        _surfaceHost.Controls.Clear();
    }

    public Task OpenDocumentAsync(
        DocumentRequest request,
        CancellationToken cancellationToken = default) =>
        OpenDocumentCoreAsync(request, recordHistory: true, cancellationToken: cancellationToken);

    private async Task OpenDocumentCoreAsync(
        DocumentRequest request,
        bool recordHistory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == EditorDocumentKind.MapView)
        {
            int mapId = request.MapId
                ?? throw new InvalidOperationException("A map view request requires a map ID.");
            await OpenMapAsync(
                mapId,
                new MapEditorNavigationTarget(mapId),
                recordHistory,
                cancellationToken).ConfigureAwait(true);
            SelectMapInHierarchy(mapId);
            return;
        }

        WindowRequest windowRequest = CreateWindowRequest(request);
        if (windowRequest.Presentation == WindowPresentation.Modal)
        {
            await _windows.ShowDialogAsync(windowRequest, this, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            IEditorWindow window = await _windows.OpenAsync(windowRequest, cancellationToken).ConfigureAwait(true);
            _windows.Activate(window);
        }
        _navigation.Navigate(
            new NavigationLocation(request, RecordId: request.RecordId, MapId: request.MapId),
            recordHistory);
    }

    public async Task NavigateBackAsync(CancellationToken cancellationToken = default)
    {
        if (_navigation.GoBack() is { } location)
        {
            await NavigateAsync(location, recordHistory: false, cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task NavigateForwardAsync(CancellationToken cancellationToken = default)
    {
        if (_navigation.GoForward() is { } location)
        {
            await NavigateAsync(location, recordHistory: false, cancellationToken).ConfigureAwait(true);
        }
    }

    public void ShowCommandPalette()
    {
        using CommandPaletteForm palette = new(_commands);
        _theme.Apply(palette);
        palette.ShowDialog(this);
    }

    public void ResetLayout()
    {
        ApplyLoadedSettings(resetLayout: true);
        CaptureSettings();
    }

    public void CloseFromController()
    {
        if (IsDisposed) return;
        CaptureSettings();
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            base.OnFormClosing(e);
            return;
        }
        e.Cancel = true;
        if (_closeRequestInProgress) return;
        _closeRequestInProgress = true;
        _ = RequestProjectCloseAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            UnwireEvents();
            _mapSelectionTimer.Stop();
            _mapSelectionTimer.Dispose();
            if (_mapController is not null)
            {
                _mapController.ActiveMapChanged -= OnActiveMapChanged;
                _mapController.StateChanged -= OnMapEditorStateChanged;
                _mapController.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _mapController = null;
            }
        }
        base.Dispose(disposing);
    }

    private void BuildMenus()
    {
        ToolStripMenuItem file = new("&File");
        AddMenu(file, ShellCommandIds.FileOpenProject);
        AddMenu(file, ShellCommandIds.FileCloseProject);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(file, ShellCommandIds.FileSave);
        file.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(file, ShellCommandIds.FileExit);

        ToolStripMenuItem edit = new("&Edit");
        AddMenu(edit, ShellCommandIds.EditUndo);
        AddMenu(edit, ShellCommandIds.EditRedo);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Cut", null, async (_, _) => await ExecuteSurfaceShortcutAsync(Keys.Control | Keys.X).ConfigureAwait(true));
        edit.DropDownItems.Add("Copy", null, async (_, _) => await ExecuteSurfaceShortcutAsync(Keys.Control | Keys.C).ConfigureAwait(true));
        edit.DropDownItems.Add("Paste", null, async (_, _) => await ExecuteSurfaceShortcutAsync(Keys.Control | Keys.V).ConfigureAwait(true));
        edit.DropDownItems.Add("Delete", null, async (_, _) => await ExecuteSurfaceShortcutAsync(Keys.Delete).ConfigureAwait(true));

        ToolStripMenuItem mode = new("&Mode");
        mode.DropDownItems.Add("Map", null, (_, _) => SetEditorMode(MapEditorMode.Map));
        mode.DropDownItems.Add("Events", null, (_, _) => SetEditorMode(MapEditorMode.Events));

        ToolStripMenuItem draw = new("&Draw");
        AddDrawMenu(draw, MapToolKind.Pencil, "Pencil", Keys.P);
        AddDrawMenu(draw, MapToolKind.Rectangle, "Rectangle", Keys.R);
        AddDrawMenu(draw, MapToolKind.Ellipse, "Ellipse", Keys.O);
        AddDrawMenu(draw, MapToolKind.Line, "Line", Keys.L);
        AddDrawMenu(draw, MapToolKind.FloodFill, "Flood Fill", Keys.F);
        AddDrawMenu(draw, MapToolKind.Eraser, "Eraser", Keys.E);
        AddDrawMenu(draw, MapToolKind.Selection, "Selection", Keys.S);
        AddDrawMenu(draw, MapToolKind.MagicWand, "Magic Wand");
        AddDrawMenu(draw, MapToolKind.Eyedropper, "Eyedropper", Keys.I);
        AddDrawMenu(draw, MapToolKind.Pattern, "Pattern");
        AddDrawMenu(draw, MapToolKind.Random, "Random");
        AddDrawMenu(draw, MapToolKind.Region, "Region");
        AddDrawMenu(draw, MapToolKind.Shadow, "Shadow");

        ToolStripMenuItem scale = new("&Scale");
        ToolStripMenuItem zoomInMenu = new("Zoom In", null, (_, _) => _mapController?.ActiveSurface?.ZoomIn())
        {
            ShortcutKeys = Keys.Control | Keys.Oemplus
        };
        ToolStripMenuItem zoomOutMenu = new("Zoom Out", null, (_, _) => _mapController?.ActiveSurface?.ZoomOut())
        {
            ShortcutKeys = Keys.Control | Keys.OemMinus
        };
        ToolStripMenuItem actualSizeMenu = new("Actual Size", null, (_, _) => _mapController?.ActiveSurface?.ActualSize())
        {
            ShortcutKeys = Keys.Control | Keys.D0
        };
        scale.DropDownItems.AddRange([zoomInMenu, zoomOutMenu, actualSizeMenu]);
        scale.DropDownItems.Add("Fit Map", null, (_, _) => _mapController?.ActiveSurface?.ZoomToFit());
        scale.DropDownItems.Add(new ToolStripSeparator());
        foreach (int percentage in new[] { 25, 50, 75, 100, 150, 200, 400 })
        {
            int value = percentage;
            scale.DropDownItems.Add($"{value}%", null, (_, _) => _mapController?.ActiveSurface?.SetZoom(value / 100f));
        }

        ToolStripMenuItem tools = new("&Tools");
        ToolStripMenuItem database = new("Database");
        BuildDatabaseMenu(database);
        tools.DropDownItems.Add(database);
        tools.DropDownItems.Add("Resource Manager", null, async (_, _) => await OpenAssetInventoryAsync().ConfigureAwait(true));
        tools.DropDownItems.Add("Data Importer…", null, async (_, _) => await OpenAssetImporterAsync().ConfigureAwait(true));
        tools.DropDownItems.Add("Plugin Manager", null, async (_, _) => await OpenPluginManagerAsync().ConfigureAwait(true));
        tools.DropDownItems.Add("Playroma Music Studio…", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.MusicStudio, "music-studio", "Playroma Music Studio").ConfigureAwait(true));
        tools.DropDownItems.Add("Playroma Sound Studio…", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.SoundStudio, "sound-studio", "Playroma Sound Studio").ConfigureAwait(true));
        tools.DropDownItems.Add("Playroma Tile Studio…", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.TileStudio, "tile-studio", "Playroma Tile Studio").ConfigureAwait(true));
        tools.DropDownItems.Add("Playroma Sprite Studio…", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.SpriteStudio, "sprite-studio", "Playroma Sprite Studio").ConfigureAwait(true));
        AddMenu(tools, ShellCommandIds.ToolsOpenTextStudio);
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("Map Properties…", null, async (_, _) => await OpenMapPropertiesAsync().ConfigureAwait(true));
        tools.DropDownItems.Add("Validation", null, async (_, _) => await OpenValidationAsync().ConfigureAwait(true));
        tools.DropDownItems.Add("Reference Explorer", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.References, "references", "Reference Explorer").ConfigureAwait(true));
        tools.DropDownItems.Add("Output", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.Output, "output", "Output").ConfigureAwait(true));
        tools.DropDownItems.Add("Background Tasks", null, async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.BackgroundTasks, "tasks", "Background Tasks").ConfigureAwait(true));
        tools.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(tools, ShellCommandIds.ProjectValidate);
        AddMenu(tools, ShellCommandIds.ProjectRefreshReferences);
        AddMenu(tools, ShellCommandIds.ProjectShowSummary);
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("Preferences…", null, async (_, _) => await OpenPreferencesAsync().ConfigureAwait(true));

        ToolStripMenuItem view = new("&View");
        ToolStripMenuItem leftPanel = new("Left Editor Panel") { Checked = true, CheckOnClick = true };
        leftPanel.CheckedChanged += (_, _) => _mainSplit.Panel1Collapsed = !leftPanel.Checked;
        view.DropDownItems.Add(leftPanel);
        view.DropDownItems.Add("Toggle Minimap", null, (_, _) =>
        {
            if (_mapController?.ActiveSurface is { } surface) surface.MinimapVisible = !surface.MinimapVisible;
        });
        ToolStripMenuItem overlays = new("Overlays");
        AddOverlayMenu(overlays, "Grid", MapOverlayOptions.Grid);
        AddOverlayMenu(overlays, "Regions", MapOverlayOptions.Regions);
        AddOverlayMenu(overlays, "Shadows", MapOverlayOptions.Shadows);
        AddOverlayMenu(overlays, "Passability", MapOverlayOptions.Passability);
        AddOverlayMenu(overlays, "Terrain Tags", MapOverlayOptions.TerrainTags);
        AddOverlayMenu(overlays, "Event IDs", MapOverlayOptions.EventIds);
        AddOverlayMenu(overlays, "Event Names", MapOverlayOptions.EventNames);
        view.DropDownItems.Add(overlays);
        view.DropDownItems.Add(new ToolStripSeparator());
        AddMenu(view, ShellCommandIds.ViewResetLayout);
        ToolStripMenuItem theme = new("Theme");
        AddMenu(theme, ShellCommandIds.ViewThemeSystem);
        AddMenu(theme, ShellCommandIds.ViewThemeLight);
        AddMenu(theme, ShellCommandIds.ViewThemeDark);
        view.DropDownItems.Add(theme);

        ToolStripMenuItem navigate = new("&Navigate");
        AddMenu(navigate, ShellCommandIds.NavigateBack);
        AddMenu(navigate, ShellCommandIds.NavigateForward);
        AddMenu(navigate, ShellCommandIds.NavigateCommandPalette);

        ToolStripMenuItem game = new("&Game");
        game.DropDownItems.Add("Open Game Folder", null, (_, _) => RevealProjectFolder());
        game.DropDownItems.Add("Playtest", null, (_, _) => ShowNotYetImplemented("Playtest integration"));
        game.DropDownItems.Add("Deployment", null, (_, _) => ShowNotYetImplemented("Deployment"));

        ToolStripMenuItem help = new("&Help");
        help.DropDownItems.Add("Documentation", null, (_, _) => OpenDocumentation());
        help.DropDownItems.Add("Keyboard Shortcuts", null, (_, _) => ShowKeyboardShortcuts());
        help.DropDownItems.Add("About Playroma jRPG", null, (_, _) => MessageBox.Show(
            this,
            $"Playroma jRPG\n{Playroma.JRPG.Core.PlayromaProduct.ReleaseLabel}\n\nOpen-source RPG Maker MV-compatible editor.",
            "About",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information));

        _menu.Items.AddRange([file, edit, mode, draw, scale, tools, view, navigate, game, help]);
    }

    private void BuildMainToolbar()
    {
        AddToolbarCommand(ShellCommandIds.FileOpenProject, "Open", EditorIconKind.Open);
        AddToolbarCommand(ShellCommandIds.FileSave, "Save", EditorIconKind.Save);
        _mainToolbar.Items.Add(new ToolStripSeparator());
        AddToolbarCommand(ShellCommandIds.EditUndo, "Undo", EditorIconKind.Undo);
        AddToolbarCommand(ShellCommandIds.EditRedo, "Redo", EditorIconKind.Redo);
        _mainToolbar.Items.Add(new ToolStripSeparator());

        ConfigureToolbarButton(_mapModeButton, EditorIconKind.Map, "Map editing mode", "Map editing mode (F5)", showText: true);
        ConfigureToolbarButton(_eventModeButton, EditorIconKind.Events, "Event editing mode", "Event editing mode (F6)", showText: true);
        _mapModeButton.Click += (_, _) => SetEditorMode(MapEditorMode.Map);
        _eventModeButton.Click += (_, _) => SetEditorMode(MapEditorMode.Events);
        _mainToolbar.Items.AddRange([_mapModeButton, _eventModeButton, new ToolStripSeparator()]);

        ToolStripButton zoomOut = CreateToolbarButton("Zoom out", EditorIconKind.ZoomOut, "Zoom out (Ctrl+-)");
        ToolStripButton zoomIn = CreateToolbarButton("Zoom in", EditorIconKind.ZoomIn, "Zoom in (Ctrl++)");
        ToolStripButton actual = CreateToolbarButton("Actual size", EditorIconKind.ActualSize, "Actual size (Ctrl+0)");
        ToolStripButton fit = CreateToolbarButton("Fit map", EditorIconKind.Fit, "Fit the entire map in the editor");
        zoomOut.Click += (_, _) => _mapController?.ActiveSurface?.ZoomOut();
        zoomIn.Click += (_, _) => _mapController?.ActiveSurface?.ZoomIn();
        actual.Click += (_, _) => _mapController?.ActiveSurface?.ActualSize();
        fit.Click += (_, _) => _mapController?.ActiveSurface?.ZoomToFit();
        _mainToolbar.Items.AddRange([zoomOut, zoomIn, actual, fit, new ToolStripSeparator()]);

        ToolStripButton database = CreateToolbarButton("Database", EditorIconKind.Database, "Open the Database");
        ToolStripButton resources = CreateToolbarButton("Resource Manager", EditorIconKind.Resources, "Open Resource Manager");
        ToolStripButton music = CreateToolbarButton("Music", EditorIconKind.Music, "Open Playroma Music Studio");
        ToolStripButton sound = CreateToolbarButton("Sound", EditorIconKind.Sound, "Open Playroma Sound Studio");
        ToolStripButton tile = CreateToolbarButton("Tile", EditorIconKind.Tile, "Open Playroma Tile Studio");
        ToolStripButton sprite = CreateToolbarButton("Sprite", EditorIconKind.Sprite, "Open Playroma Sprite Studio");
        ToolStripButton textStudio = CreateToolbarButton("Text", EditorIconKind.TextStudio, "Open Playroma Text Studio");
        ToolStripButton plugins = CreateToolbarButton("Plugin Manager", EditorIconKind.Plugins, "Open Plugin Manager");
        ToolStripButton properties = CreateToolbarButton("Map Properties", EditorIconKind.Properties, "Edit active map properties");
        ToolStripButton validation = CreateToolbarButton("Validation", EditorIconKind.Validation, "Open Validation Center");
        ToolStripButton playtest = CreateToolbarButton("Playtest", EditorIconKind.Playtest, "Playtest the current project");
        database.Click += async (_, _) => await OpenDefaultDatabaseAsync().ConfigureAwait(true);
        resources.Click += async (_, _) => await OpenAssetInventoryAsync().ConfigureAwait(true);
        music.Click += async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.MusicStudio, "music-studio", "Playroma Music Studio").ConfigureAwait(true);
        sound.Click += async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.SoundStudio, "sound-studio", "Playroma Sound Studio").ConfigureAwait(true);
        tile.Click += async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.TileStudio, "tile-studio", "Playroma Tile Studio").ConfigureAwait(true);
        sprite.Click += async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.SpriteStudio, "sprite-studio", "Playroma Sprite Studio").ConfigureAwait(true);
        textStudio.Tag = ShellCommandIds.ToolsOpenTextStudio;
        textStudio.Click += async (_, _) => await _commands.ExecuteAsync(ShellCommandIds.ToolsOpenTextStudio).ConfigureAwait(true);
        TrackCommandItem(ShellCommandIds.ToolsOpenTextStudio, textStudio);
        plugins.Click += async (_, _) => await OpenPluginManagerAsync().ConfigureAwait(true);
        properties.Click += async (_, _) => await OpenMapPropertiesAsync().ConfigureAwait(true);
        validation.Click += async (_, _) => await OpenValidationAsync().ConfigureAwait(true);
        playtest.Click += (_, _) => ShowNotYetImplemented("Playtest integration");
        _mainToolbar.Items.AddRange([database, resources, music, sound, tile, sprite, textStudio, plugins, properties, new ToolStripSeparator(), validation, playtest]);
    }

    private void BuildDrawingToolbar()
    {
        AddToolButton(MapToolKind.Pencil, "Pencil", "Paint selected tile (P)");
        AddToolButton(MapToolKind.Rectangle, "Rectangle", "Paint rectangle (R)");
        AddToolButton(MapToolKind.Ellipse, "Ellipse", "Paint ellipse (O)");
        AddToolButton(MapToolKind.Line, "Line", "Paint line (L)");
        AddToolButton(MapToolKind.FloodFill, "Fill", "Flood fill (F)");
        AddToolButton(MapToolKind.Eraser, "Eraser", "Erase tiles (E)");
        AddToolButton(MapToolKind.Selection, "Select", "Select an area (S)");
        AddToolButton(MapToolKind.MagicWand, "Wand", "Select connected matching cells");
        AddToolButton(MapToolKind.Eyedropper, "Pick", "Pick a tile (I)");
        AddToolButton(MapToolKind.Pattern, "Pattern", "Pattern brush");
        AddToolButton(MapToolKind.Random, "Random", "Random brush");
        AddToolButton(MapToolKind.Region, "Region", "Paint region IDs");
        AddToolButton(MapToolKind.Shadow, "Shadow", "Paint shadow masks");

        _layerChoice.Items.AddRange(["Automatic", "Lower 1", "Lower 2", "Upper 1", "Upper 2", "Shadow", "Region"]);
        _layerChoice.SelectedIndex = 0;
        _layerChoice.SelectedIndexChanged += (_, _) =>
        {
            if (_mapController?.ActiveSurface is not { } surface) return;
            if (_layerChoice.SelectedIndex == 0)
            {
                surface.SetEditMode(MapEditMode.Automatic);
            }
            else
            {
                surface.SetEditMode(MapEditMode.ExplicitLayer);
                surface.SetExplicitLayer(_layerChoice.SelectedIndex - 1);
            }
        };
        _valueEditor.ValueChanged += (_, _) =>
            _mapController?.ActiveSurface?.SetSelectedValue(decimal.ToInt32(_valueEditor.Value));
        _drawToolbar.Items.AddRange([
            new ToolStripSeparator(),
            new ToolStripLabel("Layer:"),
            _layerChoice,
            new ToolStripLabel("Value:"),
            new ToolStripControlHost(_valueEditor)
        ]);
        SelectToolButton(MapToolKind.Pencil);
    }

    private void WireEvents()
    {
        _workspace.StateChanged += OnWorkspaceStateChanged;
        _commands.CommandStatesChanged += OnCommandStatesChanged;
        _theme.ThemeChanged += OnThemeChanged;
        _selection.SelectionChanged += OnSelectionChanged;
        _navigation.Changed += OnNavigationChanged;
        _fileWatcher.FileChanged += OnProjectFileChanged;
        _notifications.Published += OnNotificationPublished;
        _mapHierarchy.MapSelected += OnMapSelected;
        _mapHierarchy.OpenRequested += OnDocumentOpenRequested;
        _mainSplit.SplitterMoved += OnLayoutChanged;
        _leftSplit.SplitterMoved += OnLayoutChanged;
        _mapSelectionTimer.Tick += OnMapSelectionTimerTick;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyDown += OnKeyDown;
        ResizeEnd += OnLayoutChanged;
    }

    private void UnwireEvents()
    {
        _workspace.StateChanged -= OnWorkspaceStateChanged;
        _commands.CommandStatesChanged -= OnCommandStatesChanged;
        _theme.ThemeChanged -= OnThemeChanged;
        _selection.SelectionChanged -= OnSelectionChanged;
        _navigation.Changed -= OnNavigationChanged;
        _fileWatcher.FileChanged -= OnProjectFileChanged;
        _notifications.Published -= OnNotificationPublished;
        _mapHierarchy.MapSelected -= OnMapSelected;
        _mapHierarchy.OpenRequested -= OnDocumentOpenRequested;
        _mainSplit.SplitterMoved -= OnLayoutChanged;
        _leftSplit.SplitterMoved -= OnLayoutChanged;
        _mapSelectionTimer.Tick -= OnMapSelectionTimerTick;
        DragEnter -= OnDragEnter;
        DragDrop -= OnDragDrop;
        KeyDown -= OnKeyDown;
        ResizeEnd -= OnLayoutChanged;
    }

    private void OnActiveMapChanged(object? sender, ActiveMapChangedEventArgs e)
    {
        if (e.Previous is not null)
        {
            e.Previous.DocumentRequested -= OnSurfaceDocumentRequested;
            e.Previous.CoordinateChanged -= OnSurfaceCoordinateChanged;
            e.Previous.EditorStateChanged -= OnSurfaceStateChanged;
        }
        _surfaceHost.SuspendLayout();
        try
        {
            foreach (Control control in _surfaceHost.Controls.Cast<Control>().Where(control =>
                         !ReferenceEquals(control, _surfaceMessage)
                         && !ReferenceEquals(control, _loadingOverlay)).ToArray())
            {
                _surfaceHost.Controls.Remove(control);
            }
            if (e.Current is not null)
            {
                e.Current.Dock = DockStyle.Fill;
                e.Current.DocumentRequested += OnSurfaceDocumentRequested;
                e.Current.CoordinateChanged += OnSurfaceCoordinateChanged;
                e.Current.EditorStateChanged += OnSurfaceStateChanged;
                e.Current.ApplyTheme(_theme.Palette);
                _surfaceHost.Controls.Add(e.Current);
                e.Current.BringToFront();
                _surfaceMessage.Visible = false;
            }
        }
        finally
        {
            _loadingOverlay.BringToFront();
            _surfaceHost.ResumeLayout(performLayout: true);
        }
        UpdateWorkspaceUi();
    }

    private void OnMapEditorStateChanged(object? sender, MapEditorStateChangedEventArgs e)
    {
        _loadingOverlay.Visible = e.State.IsLoading;
        if (e.State.IsLoading) _loadingOverlay.BringToFront();
        if (!string.IsNullOrWhiteSpace(e.State.ErrorMessage))
        {
            _surfaceMessage.Text = "The map could not be loaded.\n\n" + e.State.ErrorMessage;
            _surfaceMessage.Visible = true;
            _surfaceMessage.BringToFront();
        }
        SetEditorModeUi(e.State.Mode);
        SelectToolButton(e.State.Tool);
        UpdateWorkspaceUi();
    }

    private void OnMapSelected(object? sender, int mapId)
    {
        if (_suppressMapSelection) return;
        _pendingMapId = mapId;
        _mapSelectionTimer.Stop();
        _mapSelectionTimer.Start();
    }

    private void SelectMapInHierarchy(int mapId)
    {
        _suppressMapSelection = true;
        try
        {
            _mapHierarchy.SelectMap(mapId);
        }
        finally
        {
            _suppressMapSelection = false;
        }
    }

    private async void OnMapSelectionTimerTick(object? sender, EventArgs e)
    {
        _mapSelectionTimer.Stop();
        if (_pendingMapId is not int mapId) return;
        _pendingMapId = null;
        try
        {
            await OpenMapAsync(mapId).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open map", exception.Message, exception);
        }
    }

    private async void OnDocumentOpenRequested(object? sender, DocumentRequest request)
    {
        try
        {
            await OpenDocumentAsync(request).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open editor", exception.Message, exception);
        }
    }

    private async void OnSurfaceDocumentRequested(object? sender, DocumentRequestEventArgs e) =>
        await OpenDocumentRequestSafelyAsync(e.Request).ConfigureAwait(true);

    private void OnSurfaceCoordinateChanged(object? sender, MapCoordinateEventArgs e)
    {
        string eventText = e.EventId.HasValue ? $" | Event {e.EventId.Value:D3}" : string.Empty;
        _coordinateStatus.Text = $"Tile {e.X}, {e.Y} | Region {e.RegionId}{eventText}";
    }

    private void OnSurfaceStateChanged(object? sender, EventArgs e) => UpdateWorkspaceUi();
    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e) => UpdateWorkspaceUi();
    private void OnCommandStatesChanged(object? sender, EventArgs e) => UpdateCommandStates();
    private void OnNavigationChanged(object? sender, EventArgs e) => UpdateCommandStates();

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ApplyVisualTheme));
            return;
        }
        ApplyVisualTheme();
    }

    private void ApplyVisualTheme()
    {
        _theme.Apply(this);
        ThemePalette palette = _theme.Palette;
        _surfaceHost.BackColor = palette.CanvasBackground;
        _surfaceMessage.BackColor = palette.CanvasBackground;
        _surfaceMessage.ForeColor = palette.PrimaryText;
        _loadingOverlay.BackColor = palette.CanvasBackground;
        _loadingText.ForeColor = palette.PrimaryText;
        _mapController?.ActiveSurface?.ApplyTheme(palette);
        RefreshToolbarImages();
    }

    private void RefreshToolbarImages()
    {
        foreach (ToolStripItem item in _mainToolbar.Items.Cast<ToolStripItem>().Concat(_drawToolbar.Items.Cast<ToolStripItem>()))
        {
            if (item is not ToolStripButton button || string.IsNullOrWhiteSpace(button.AccessibleName)) continue;
            EditorIconKind? icon = button.Tag is MapToolKind tool
                ? IconForTool(tool)
                : button.AccessibleName switch
                {
                    "Open" => EditorIconKind.Open,
                    "Save" => EditorIconKind.Save,
                    "Undo" => EditorIconKind.Undo,
                    "Redo" => EditorIconKind.Redo,
                    "Map editing mode" => EditorIconKind.Map,
                    "Event editing mode" => EditorIconKind.Events,
                    "Zoom out" => EditorIconKind.ZoomOut,
                    "Zoom in" => EditorIconKind.ZoomIn,
                    "Actual size" => EditorIconKind.ActualSize,
                    "Fit map" => EditorIconKind.Fit,
                    "Database" => EditorIconKind.Database,
                    "Resource Manager" => EditorIconKind.Resources,
                    "Music" => EditorIconKind.Music,
                    "Plugin Manager" => EditorIconKind.Plugins,
                    "Map Properties" => EditorIconKind.Properties,
                    "Validation" => EditorIconKind.Validation,
                    "Playtest" => EditorIconKind.Playtest,
                    _ => null
                };
            if (icon.HasValue) button.Image = _theme.Icon(icon.Value, this);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_selection.Current is MapTilePaletteSelection tile)
        {
            _valueEditor.Maximum = 8191;
            _valueEditor.Value = Math.Clamp(tile.TileId, 0, 8191);
            if (_mapController?.Mode == MapEditorMode.Map) SelectMapTool(MapToolKind.Pencil);
        }
        else if (_selection.Current is MapTileBrushSelection brush)
        {
            _valueEditor.Maximum = 8191;
            _valueEditor.Value = Math.Clamp(brush.PrimaryTileId, 0, 8191);
        }
    }

    private void OnProjectFileChanged(object? sender, ProjectFileChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnProjectFileChanged(sender, e)));
            return;
        }
        var active = _mapController?.ActiveSurface;
        var descriptor = _workspace.CurrentSession?.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == active?.MapId);
        if (active is not null && string.Equals(descriptor?.RelativePath, e.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            _ = active.ReloadMapAsync();
        }
        _output.Write("Files", $"External {e.ChangeType.ToString().ToLowerInvariant()} detected: {e.RelativePath}");
    }

    private void OnNotificationPublished(object? sender, ShellNotification notification)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnNotificationPublished(sender, notification)));
            return;
        }
        _mapStatus.Text = notification.Message;
        if (notification.Kind == ShellNotificationKind.Error)
        {
            MessageBox.Show(this, notification.Message, notification.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (!_restoringSettings) CaptureSettings();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        await _applicationController.OpenProjectAsync(paths[0]).ConfigureAwait(true);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ActiveControl is TextBoxBase || ActiveControl is ComboBox || ActiveControl is NumericUpDown) return;
        if (e.KeyCode == Keys.F5)
        {
            e.Handled = e.SuppressKeyPress = true;
            SetEditorMode(MapEditorMode.Map);
            return;
        }
        if (e.KeyCode == Keys.F6)
        {
            e.Handled = e.SuppressKeyPress = true;
            SetEditorMode(MapEditorMode.Events);
            return;
        }
        MapToolKind? tool = e.KeyCode switch
        {
            Keys.P => MapToolKind.Pencil,
            Keys.R => MapToolKind.Rectangle,
            Keys.O => MapToolKind.Ellipse,
            Keys.L => MapToolKind.Line,
            Keys.F => MapToolKind.FloodFill,
            Keys.E => MapToolKind.Eraser,
            Keys.S => MapToolKind.Selection,
            Keys.I => MapToolKind.Eyedropper,
            _ => null
        };
        if (tool.HasValue && !e.Control && !e.Alt)
        {
            e.Handled = e.SuppressKeyPress = true;
            SelectMapTool(tool.Value);
            return;
        }
        string? command = ShortcutCommand(e);
        if (command is not null)
        {
            e.Handled = e.SuppressKeyPress = true;
            await _commands.ExecuteAsync(command).ConfigureAwait(true);
        }
    }

    private async Task OpenMapAsync(
        int mapId,
        MapEditorNavigationTarget? target = null,
        bool recordHistory = true,
        CancellationToken cancellationToken = default)
    {
        if (_mapController is null) return;
        _loadingOverlay.Visible = true;
        _loadingOverlay.BringToFront();
        await _mapController.OpenMapAsync(mapId, target, cancellationToken).ConfigureAwait(true);
        // A newer hierarchy selection may have cancelled this load while the controller was
        // awaiting I/O. Only the surface that actually became active may enter navigation
        // history or overwrite persisted active-map state.
        if (_mapController.ActiveMapId != mapId) return;
        var descriptor = _workspace.CurrentSession?.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == mapId);
        DocumentRequest request = new(
            EditorDocumentKind.MapView,
            $"map:{mapId}",
            descriptor?.RelativePath,
            MapId: mapId,
            DisplayTitle: descriptor?.Name ?? $"Map {mapId:D3}");
        _navigation.Navigate(new NavigationLocation(request, MapId: mapId), recordHistory);
        CaptureSettings();
    }

    private async Task NavigateAsync(
        NavigationLocation location,
        bool recordHistory,
        CancellationToken cancellationToken)
    {
        if (location.Document.Kind == EditorDocumentKind.MapView || location.MapId.HasValue)
        {
            int mapId = location.MapId ?? location.Document.MapId
                ?? throw new InvalidOperationException("The map navigation location has no map ID.");
            int? eventId = location.Document.EventId;
            MapEditorNavigationTarget target = new(
                mapId,
                EventId: eventId,
                PreferredMode: eventId.HasValue ? MapEditorMode.Events : null);
            await OpenMapAsync(mapId, target, recordHistory: false, cancellationToken: cancellationToken).ConfigureAwait(true);
            SelectMapInHierarchy(mapId);
        }
        else
        {
            await OpenDocumentCoreAsync(location.Document, recordHistory: false, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        _navigation.Navigate(location, recordHistory);
    }

    private async Task OpenDocumentRequestSafelyAsync(DocumentRequest request)
    {
        try
        {
            await OpenDocumentAsync(request).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open editor", exception.Message, exception);
        }
    }

    private WindowRequest CreateWindowRequest(DocumentRequest request) => request.Kind switch
    {
        EditorDocumentKind.EventEditor => new WindowRequest(
            EditorWindowKind.EventEditor,
            EventWindowIdentity.Create(request),
            request.DisplayTitle,
            request.RelativePath,
            WindowPresentation.Modal,
            WindowScope.Project,
            PlacementId: "EventEditor",
            Document: request,
            Location: new NavigationLocation(request, MapId: request.MapId)),
        EditorDocumentKind.MapProperties => new WindowRequest(
            EditorWindowKind.MapProperties,
            request.Identity,
            request.DisplayTitle,
            request.RelativePath,
            WindowPresentation.Modal,
            WindowScope.Project,
            PlacementId: "MapProperties",
            Document: request),
        EditorDocumentKind.Database or EditorDocumentKind.SystemData => new WindowRequest(
            EditorWindowKind.Database,
            "database",
            "Database",
            request.RelativePath,
            WindowPresentation.Modeless,
            WindowScope.Project,
            PlacementId: "Database",
            Document: request,
            Location: new NavigationLocation(
                request,
                SelectionPath: request.EventSurface,
                RecordId: request.RecordId)),
        EditorDocumentKind.AssetInventory => new WindowRequest(
            EditorWindowKind.ResourceManager,
            "resource-manager",
            request.DisplayTitle ?? "Resource Manager",
            Scope: WindowScope.Project,
            PlacementId: "ResourceManager",
            Document: request),
        EditorDocumentKind.PluginInventory => new WindowRequest(
            EditorWindowKind.PluginManager,
            "plugin-manager",
            request.DisplayTitle ?? "Plugin Manager",
            Scope: WindowScope.Project,
            PlacementId: "PluginManager",
            Document: request),
        EditorDocumentKind.RawJson or EditorDocumentKind.ProjectFile => new WindowRequest(
            EditorWindowKind.RawJson,
            request.Identity,
            request.DisplayTitle,
            request.RelativePath,
            WindowPresentation.Modeless,
            WindowScope.Project,
            PlacementId: "RawJson",
            Document: request),
        EditorDocumentKind.ProjectSummary => new WindowRequest(
            EditorWindowKind.ProjectSummary,
            "project-summary",
            request.DisplayTitle ?? "Project Summary",
            Scope: WindowScope.Project,
            PlacementId: "ProjectSummary",
            Document: request),
        EditorDocumentKind.Validation => new WindowRequest(
            EditorWindowKind.Validation,
            "validation",
            request.DisplayTitle ?? "Validation",
            Scope: WindowScope.Project,
            PlacementId: "Validation",
            Document: request),
        _ => throw new NotSupportedException($"Document kind '{request.Kind}' is not available as a dedicated window.")
    };

    private async Task OpenMapPropertiesAsync()
    {
        if (_mapController?.ActiveMapId is not int mapId) return;
        var descriptor = _workspace.CurrentSession?.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == mapId);
        if (descriptor is null) return;
        await OpenDocumentAsync(new DocumentRequest(
            EditorDocumentKind.MapProperties,
            $"map-properties:{mapId}",
            descriptor.RelativePath,
            MapId: mapId,
            DisplayTitle: descriptor.Name + " Properties")).ConfigureAwait(true);
    }

    private static int DatabaseMenuOrder(MvDocumentKind kind) => kind switch
    {
        MvDocumentKind.Actors => 0,
        MvDocumentKind.Classes => 1,
        MvDocumentKind.Skills => 2,
        MvDocumentKind.Items => 3,
        MvDocumentKind.Weapons => 4,
        MvDocumentKind.Armors => 5,
        MvDocumentKind.Enemies => 6,
        MvDocumentKind.Troops => 7,
        MvDocumentKind.States => 8,
        MvDocumentKind.Animations => 9,
        MvDocumentKind.Tilesets => 10,
        MvDocumentKind.CommonEvents => 11,
        MvDocumentKind.System => 12,
        _ => 100
    };

    private async Task OpenDefaultDatabaseAsync()
    {
        if (_workspace.CurrentSession is null) return;
        await OpenDocumentAsync(MvDocumentRequestFactory.CreateDatabaseRootRequest()).ConfigureAwait(true);
    }

    private void BuildDatabaseMenu(ToolStripMenuItem database)
    {
        var files = _workspace.CurrentSession?.Workspace.Snapshot.Database.Files
            .Where(file => file.Kind is not MvDocumentKind.MapInfos)
            .OrderBy(file => DatabaseMenuOrder(file.Kind))
            .ThenBy(file => file.Kind.ToString())
            .ToArray() ?? [];
        if (files.Length == 0)
        {
            database.DropDownItems.Add("No database files", null, (_, _) => { }).Enabled = false;
            return;
        }
        foreach (var file in files)
        {
            var captured = file;
            database.DropDownItems.Add(file.Kind.ToString(), null, async (_, _) =>
                await OpenDocumentRequestSafelyAsync(MvDocumentRequestFactory.CreateDatabaseRequest(captured)).ConfigureAwait(true));
        }
    }

    private async Task OpenAssetInventoryAsync() => await OpenDocumentAsync(new DocumentRequest(
        EditorDocumentKind.AssetInventory,
        "assets",
        DisplayTitle: "Resource Manager")).ConfigureAwait(true);

    private async Task OpenPluginManagerAsync() => await OpenDocumentAsync(new DocumentRequest(
        EditorDocumentKind.PluginInventory,
        "plugins",
        DisplayTitle: "Plugin Manager")).ConfigureAwait(true);

    private async Task OpenValidationAsync() => await OpenDocumentAsync(new DocumentRequest(
        EditorDocumentKind.Validation,
        "validation",
        DisplayTitle: "Validation")).ConfigureAwait(true);

    private async Task OpenUtilityWindowAsync(EditorWindowKind kind, string identity, string title)
    {
        await _windows.OpenAsync(new WindowRequest(
            kind,
            identity,
            title,
            Scope: WindowScope.Project,
            PlacementId: title.Replace(" ", string.Empty, StringComparison.Ordinal))).ConfigureAwait(true);
    }

    private async Task OpenPreferencesAsync()
    {
        await _windows.ShowDialogAsync(new WindowRequest(
            EditorWindowKind.Preferences,
            "preferences",
            "Preferences",
            Presentation: WindowPresentation.Modal,
            Scope: WindowScope.Application,
            PlacementId: "Preferences"), this).ConfigureAwait(true);
    }

    private Task OpenAssetImporterAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return Task.CompletedTask;
        using AssetImportWizardForm dialog = new(session);
        _theme.Apply(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _notifications.Success("Assets staged", $"{dialog.ImportedCount:N0} asset(s) were imported as one undoable transaction.");
        }
        return Task.CompletedTask;
    }

    private void SetEditorMode(MapEditorMode mode)
    {
        _mapController?.SetMode(mode);
        SetEditorModeUi(mode);
    }

    private void SetEditorModeUi(MapEditorMode mode)
    {
        _mapModeButton.Checked = mode == MapEditorMode.Map;
        _eventModeButton.Checked = mode == MapEditorMode.Events;
        _tilesetPalette.Enabled = mode == MapEditorMode.Map;
        foreach ((MapToolKind tool, ToolStripButton button) in _toolButtons)
        {
            button.Enabled = mode == MapEditorMode.Map && tool != MapToolKind.Events;
        }
        _layerChoice.Enabled = mode == MapEditorMode.Map;
        _valueEditor.Enabled = mode == MapEditorMode.Map;
        _modeStatus.Text = mode == MapEditorMode.Events
            ? "Events"
            : $"Map / {_mapController?.ActiveSurface?.ActiveTool ?? MapToolKind.Pencil}";
    }

    private void SelectMapTool(MapToolKind tool)
    {
        if (_mapController is null) return;
        _mapController.SetTool(tool);
        SelectToolButton(tool);
    }

    private void SelectToolButton(MapToolKind tool)
    {
        foreach ((MapToolKind candidate, ToolStripButton button) in _toolButtons)
        {
            button.Checked = candidate == tool;
        }
        if (tool != MapToolKind.Events) _modeStatus.Text = $"Map / {tool}";
    }

    private void UpdateWorkspaceUi()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(UpdateWorkspaceUi));
            return;
        }
        var session = _workspace.CurrentSession;
        var surface = _mapController?.ActiveSurface;
        var map = session?.Workspace.Snapshot.Maps.FirstOrDefault(item => item.Id == surface?.MapId);
        string projectTitle = session?.Workspace.Snapshot.Identity.ProjectTitle
            ?? (session is null ? string.Empty : Path.GetFileName(session.Workspace.Snapshot.Identity.ProjectRoot));
        string dirty = session?.State.HasUnsavedChanges == true ? " *" : string.Empty;
        Text = session is null
            ? "Playroma jRPG"
            : $"Playroma jRPG — {projectTitle}{dirty}";
        _mapStatus.Text = map is null
            ? session?.State.BusyOperation ?? "No map loaded"
            : $"{map.Id:D3}: {map.Name} ({map.Width ?? 0}×{map.Height ?? 0})";
        _zoomStatus.Text = surface is null ? "—" : $"{surface.Zoom * 100:0.#}%";
        _dirtyStatus.Text = session?.State.HasUnsavedChanges == true ? "Modified" : string.Empty;
        if (surface is not null)
        {
            _modeStatus.Text = surface.EditorMode == MapEditorMode.Events
                ? "Events"
                : $"Map / {surface.ActiveTool} / Layer {surface.ActiveLayer + 1}";
            if (_valueEditor.Value != surface.SelectedValue
                && surface.SelectedValue >= _valueEditor.Minimum
                && surface.SelectedValue <= _valueEditor.Maximum)
            {
                _valueEditor.Value = surface.SelectedValue;
            }
        }
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        if (IsDisposed || Disposing) return;
        foreach ((string id, List<ToolStripItem> items) in _commandItems)
        {
            bool enabled = _commands.CanExecute(id);
            foreach (ToolStripItem item in items)
            {
                item.Enabled = enabled;
                if (id == ShellCommandIds.EditUndo)
                {
                    item.Text = _workspace.CurrentSession?.Undo.UndoDescription is { } undo ? "Undo " + undo : "Undo";
                }
                else if (id == ShellCommandIds.EditRedo)
                {
                    item.Text = _workspace.CurrentSession?.Undo.RedoDescription is { } redo ? "Redo " + redo : "Redo";
                }
            }
        }
    }

    private void CaptureSettings()
    {
        if (IsDisposed || Disposing || _restoringSettings) return;
        _mapController?.CaptureActiveViewState();
        _placement.Capture(this, PlacementRequest);
        MapOverlayOptions overlays = _mapController?.ActiveSurface?.Overlays ?? MapOverlayOptions.None;
        MapEditorLayoutSettings layout = new(
            LeftPanelWidth: _mainSplit.Panel1Collapsed ? 310 : _mainSplit.SplitterDistance,
            PalettePanelHeight: _leftSplit.SplitterDistance,
            LeftPanelCollapsed: _mainSplit.Panel1Collapsed,
            MinimapVisible: _mapController?.ActiveSurface?.MinimapVisible ?? true,
            PaletteZoomPercent: _tilesetPalette.PaletteZoomPercent,
            SelectedPaletteTab: _tilesetPalette.SelectedPaletteTab,
            OverlayFlags: (int)overlays,
            ExpandedMapIds: _mapHierarchy.CaptureExpandedMapIds(),
            ActiveMapId: _mapController?.ActiveMapId,
            SearchText: _mapHierarchy.SearchText,
            MapViews: _mapController?.CaptureViewStates().Select(ToMapViewSettings).ToArray()
                ?? _settings.Current.EffectiveMapEditorLayout.EffectiveMapViews);
        _settings.Update(settings => settings with
        {
            MapEditorLayout = layout,
            Version = EditorSettingsMigrator.CurrentVersion
        });
    }

    private static MapViewState ToMapViewState(MapEditorMapViewSettings settings)
    {
        if (!Enum.TryParse(settings.Mode, ignoreCase: true, out MapEditorMode mode)) mode = MapEditorMode.Map;
        if (!Enum.TryParse(settings.Tool, ignoreCase: true, out MapToolKind tool)) tool = MapToolKind.Pencil;
        if (!Enum.TryParse(settings.EditMode, ignoreCase: true, out MapEditMode editMode)) editMode = MapEditMode.Automatic;
        if (mode == MapEditorMode.Events) tool = MapToolKind.Events;
        if (mode == MapEditorMode.Map && tool == MapToolKind.Events) tool = MapToolKind.Pencil;
        return new MapViewState(
            settings.MapId,
            Math.Clamp(settings.Zoom, 0.1f, 8f),
            Math.Max(0, settings.CentreTileX),
            Math.Max(0, settings.CentreTileY),
            mode,
            tool,
            editMode,
            Math.Clamp(settings.SelectedLayer, 0, 5),
            Math.Clamp(settings.SelectedValue, 0, 8191),
            string.IsNullOrWhiteSpace(settings.SelectedSheet) ? "A5" : settings.SelectedSheet,
            (MapOverlayOptions)settings.OverlayFlags,
            settings.MinimapVisible);
    }

    private static MapEditorMapViewSettings ToMapViewSettings(MapViewState state) => new(
        state.MapId,
        state.Zoom,
        state.CentreTileX,
        state.CentreTileY,
        state.Mode.ToString(),
        state.Tool.ToString(),
        state.EditMode.ToString(),
        state.SelectedLayer,
        state.SelectedValue,
        state.SelectedSheet,
        (int)state.Overlays,
        state.MinimapVisible);

    private async Task RequestProjectCloseAsync()
    {
        try
        {
            await _applicationController.CloseProjectAsync(ProjectCloseReason.ReturnToProjectHub).ConfigureAwait(true);
        }
        finally
        {
            _closeRequestInProgress = false;
        }
    }

    private void AddMenu(ToolStripMenuItem parent, string id)
    {
        IShellCommand command = _commands.Find(id)
            ?? throw new InvalidOperationException($"Command '{id}' was not registered before Map Editor construction.");
        ToolStripMenuItem item = new(command.DisplayName) { Tag = id };
        if (TryParseShortcut(command.DefaultShortcut, out Keys keys)) item.ShortcutKeys = keys;
        item.Click += async (_, _) => await _commands.ExecuteAsync(id).ConfigureAwait(true);
        parent.DropDownItems.Add(item);
        TrackCommandItem(id, item);
    }

    private void AddToolbarCommand(string id, string text, EditorIconKind icon)
    {
        IShellCommand command = _commands.Find(id)
            ?? throw new InvalidOperationException($"Command '{id}' was not registered before Map Editor construction.");
        ToolStripButton button = CreateToolbarButton(text, icon, command.Description ?? command.DisplayName);
        button.Tag = id;
        button.Click += async (_, _) => await _commands.ExecuteAsync(id).ConfigureAwait(true);
        _mainToolbar.Items.Add(button);
        TrackCommandItem(id, button);
    }

    private void AddToolButton(MapToolKind tool, string text, string tooltip)
    {
        ToolStripButton button = CreateToolbarButton(text, IconForTool(tool), tooltip);
        button.CheckOnClick = true;
        button.Tag = tool;
        button.Click += (_, _) => SelectMapTool(tool);
        _toolButtons[tool] = button;
        _drawToolbar.Items.Add(button);
    }

    private ToolStripButton CreateToolbarButton(string text, EditorIconKind icon, string tooltip)
    {
        ToolStripButton button = new(text);
        ConfigureToolbarButton(button, icon, text, tooltip, showText: false);
        return button;
    }

    private void ConfigureToolbarButton(
        ToolStripButton button,
        EditorIconKind icon,
        string accessibleName,
        string tooltip,
        bool showText)
    {
        button.Image = _theme.Icon(icon, this);
        button.DisplayStyle = showText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
        button.ImageTransparentColor = Color.Transparent;
        button.ToolTipText = tooltip;
        button.AccessibleName = accessibleName;
        button.AccessibleDescription = tooltip;
        button.AutoToolTip = true;
    }

    private static EditorIconKind IconForTool(MapToolKind tool) => tool switch
    {
        MapToolKind.Pencil => EditorIconKind.Pencil,
        MapToolKind.Rectangle => EditorIconKind.Rectangle,
        MapToolKind.Ellipse => EditorIconKind.Ellipse,
        MapToolKind.Line => EditorIconKind.Line,
        MapToolKind.FloodFill => EditorIconKind.Fill,
        MapToolKind.Eraser => EditorIconKind.Eraser,
        MapToolKind.Selection => EditorIconKind.Selection,
        MapToolKind.MagicWand => EditorIconKind.MagicWand,
        MapToolKind.Eyedropper => EditorIconKind.Eyedropper,
        MapToolKind.Pattern => EditorIconKind.Pattern,
        MapToolKind.Random => EditorIconKind.Random,
        MapToolKind.Region => EditorIconKind.Region,
        MapToolKind.Shadow => EditorIconKind.Shadow,
        _ => EditorIconKind.Pencil
    };

    private void AddDrawMenu(ToolStripMenuItem parent, MapToolKind tool, string text, Keys shortcut = Keys.None)
    {
        ToolStripMenuItem item = new(text);
        if (shortcut != Keys.None)
        {
            // ToolStripMenuItem rejects unmodified letter keys (for example Keys.P)
            // as ShortcutKeys. The form already handles these tool-selection keys in
            // OnKeyDown, so only provide the visible shortcut label here.
            item.ShortcutKeyDisplayString = shortcut.ToString();
            item.ShowShortcutKeys = true;
        }
        item.Click += (_, _) => SelectMapTool(tool);
        parent.DropDownItems.Add(item);
    }

    private void AddOverlayMenu(ToolStripMenuItem parent, string text, MapOverlayOptions option)
    {
        ToolStripMenuItem item = new(text) { CheckOnClick = true };
        parent.DropDownOpening += (_, _) =>
        {
            item.Checked = (_mapController?.ActiveSurface?.Overlays & option) != 0;
        };
        item.Click += (_, _) =>
        {
            _mapController?.ActiveSurface?.ToggleOverlay(option);
            item.Checked = (_mapController?.ActiveSurface?.Overlays & option) != 0;
        };
        parent.DropDownItems.Add(item);
    }

    private void TrackCommandItem(string id, ToolStripItem item)
    {
        if (!_commandItems.TryGetValue(id, out List<ToolStripItem>? items))
        {
            items = [];
            _commandItems.Add(id, items);
        }
        items.Add(item);
    }

    public event EventHandler? CommandStateChanged;

    public EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        MapRenderDocumentControl? surface = _mapController?.ActiveSurface;
        if (surface is not null)
        {
            EditorCommandStatus status = surface.QueryCommand(command, context);
            if (status.Handling != CommandHandling.NotHandled) return status;
        }
        return EditorCommandStatus.NotHandled(command);
    }

    public async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        MapRenderDocumentControl? surface = _mapController?.ActiveSurface;
        if (surface is not null) await surface.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        CommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ExecuteSurfaceShortcutAsync(Keys keys)
    {
        MapRenderDocumentControl? surface = _mapController?.ActiveSurface;
        if (surface is null) return;
        EditorCommandId command = keys switch
        {
            Keys.Control | Keys.X => EditorCommandIds.EditCut,
            Keys.Control | Keys.C => EditorCommandIds.EditCopy,
            Keys.Control | Keys.V => EditorCommandIds.EditPaste,
            Keys.Delete => EditorCommandIds.EditDelete,
            _ => default
        };
        if (!string.IsNullOrEmpty(command.Value))
            await surface.ExecuteRoutedCommandAsync(command).ConfigureAwait(true);
    }

    private void RevealProjectFolder()
    {
        string? root = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
    }

    private void OpenDocumentation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "docs", "ARCHITECTURE.md");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.md");
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        MessageBox.Show(this, "Documentation is included in the source repository under docs/.", "Documentation", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowKeyboardShortcuts() => MessageBox.Show(
        this,
        "F5  Map mode\nF6  Event mode\nP  Pencil\nR  Rectangle\nO  Ellipse\nL  Line\nF  Flood fill\nE  Eraser\nS  Selection\nI  Eyedropper\nCtrl+S  Save\nCtrl+Z / Ctrl+Y  Undo / Redo\nCtrl+Shift+T  Text Studio\nCtrl++ / Ctrl+-  Zoom",
        "Map Editor Keyboard Shortcuts",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    private void ShowNotYetImplemented(string feature) => MessageBox.Show(
        this,
        $"{feature} remains available in the project services but does not yet have a dedicated interface.",
        feature,
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    private static string? ShortcutCommand(KeyEventArgs e) => e switch
    {
        { Control: true, Shift: false, KeyCode: Keys.O } => ShellCommandIds.FileOpenProject,
        { Control: true, Shift: false, KeyCode: Keys.S } => ShellCommandIds.FileSave,
        { Control: true, Shift: false, KeyCode: Keys.Z } => ShellCommandIds.EditUndo,
        { Control: true, Shift: false, KeyCode: Keys.Y } => ShellCommandIds.EditRedo,
        { Alt: true, KeyCode: Keys.Left } => ShellCommandIds.NavigateBack,
        { Alt: true, KeyCode: Keys.Right } => ShellCommandIds.NavigateForward,
        { Control: true, Shift: true, KeyCode: Keys.P } => ShellCommandIds.NavigateCommandPalette,
        { Control: true, Shift: true, KeyCode: Keys.T } => ShellCommandIds.ToolsOpenTextStudio,
        { KeyCode: Keys.F8 } => ShellCommandIds.ProjectValidate,
        _ => null
    };

    private static bool TryParseShortcut(string? shortcut, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        foreach (string part in shortcut.Split('+', StringSplitOptions.TrimEntries))
        {
            keys |= part.ToUpperInvariant() switch
            {
                "CTRL" => Keys.Control,
                "SHIFT" => Keys.Shift,
                "ALT" => Keys.Alt,
                "F8" => Keys.F8,
                "O" => Keys.O,
                "S" => Keys.S,
                "Z" => Keys.Z,
                "Y" => Keys.Y,
                "T" => Keys.T,
                "LEFT" => Keys.Left,
                "RIGHT" => Keys.Right,
                "P" => Keys.P,
                _ => Keys.None
            };
        }
        return keys != Keys.None;
    }

    private sealed record MapEditorWorkspacePayload(
        int? ActiveMapId,
        IReadOnlyList<MapViewState>? ViewStates,
        int Version = 1);

}

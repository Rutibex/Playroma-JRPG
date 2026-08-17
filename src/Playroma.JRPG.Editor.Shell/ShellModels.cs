using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;

namespace Playroma.JRPG.Editor.Shell;

public enum EditorTheme
{
    System,
    Light,
    Dark
}

public sealed record ShellBounds(int X, int Y, int Width, int Height);

public sealed record MapEditorMapViewSettings(
    int MapId,
    float Zoom = 1f,
    int CentreTileX = 0,
    int CentreTileY = 0,
    string Mode = "Map",
    string Tool = "Pencil",
    string EditMode = "Automatic",
    int SelectedLayer = 0,
    int SelectedValue = 0,
    string SelectedSheet = "A5",
    int OverlayFlags = 0,
    bool MinimapVisible = true);

public sealed record MapEditorLayoutSettings(
    int LeftPanelWidth = 310,
    int PalettePanelHeight = 430,
    bool LeftPanelCollapsed = false,
    bool MinimapVisible = true,
    int PaletteZoomPercent = 50,
    string SelectedPaletteTab = "A",
    int OverlayFlags = 17,
    IReadOnlyList<int>? ExpandedMapIds = null,
    int? ActiveMapId = null,
    string SearchText = "",
    IReadOnlyList<MapEditorMapViewSettings>? MapViews = null)
{
    public IReadOnlyList<int> EffectiveExpandedMapIds => ExpandedMapIds ?? [];
    public IReadOnlyList<MapEditorMapViewSettings> EffectiveMapViews => MapViews ?? [];
}


public sealed record DatabaseCategoryViewSettings(
    string CategoryKey,
    int? SelectedRecordId = null,
    string SearchText = "",
    string ActiveSection = "",
    int RecordPanelWidth = 330,
    bool AdvancedVisible = false);

public sealed record DatabaseWindowLayoutSettings(
    string SelectedCategory = "Actors",
    IReadOnlyList<DatabaseCategoryViewSettings>? Categories = null,
    int CategoryPanelWidth = 150,
    bool AdvancedVisible = false,
    int Version = 1)
{
    public IReadOnlyList<DatabaseCategoryViewSettings> EffectiveCategories => Categories ?? [];
}

public sealed record ResourceManagerWindowLayoutSettings(
    string SelectedCategory = "All",
    string SearchText = "",
    string ViewMode = "Details",
    string SortColumn = "Name",
    bool SortDescending = false,
    string? SelectedAsset = null,
    string SelectedPreviewTab = "Preview",
    int CategoryPanelWidth = 215,
    int PreviewPanelWidth = 380,
    int ThumbnailSize = 112,
    IReadOnlyList<string>? ExpandedGroups = null,
    int Version = 1)
{
    public IReadOnlyList<string> EffectiveExpandedGroups => ExpandedGroups ?? ["Images", "Audio", "Other Media"];
}

public sealed record ValidationWindowLayoutSettings(
    string SeverityFilter = "All",
    string Grouping = "Severity",
    string SearchText = "",
    int NavigationPanelWidth = 190,
    int DetailsPanelHeight = 180,
    string? SelectedDiagnosticKey = null,
    int Version = 1);

public sealed record ReferenceExplorerWindowLayoutSettings(
    bool FollowSelection = true,
    bool IncludeExact = true,
    bool IncludeHeuristic = true,
    string SearchText = "",
    int DetailsPanelHeight = 170,
    int SelectedTab = 0,
    int Version = 1);

public sealed record OutputWindowLayoutSettings(
    string CategoryFilter = "All",
    string SeverityFilter = "All",
    string SearchText = "",
    bool AutoScroll = true,
    bool Paused = false,
    bool WordWrap = false,
    int DetailsPanelHeight = 190,
    int Version = 1);

public sealed record BackgroundTasksWindowLayoutSettings(
    string StateFilter = "All",
    string CategoryFilter = "All",
    bool IncludeFailedWhenClearing = false,
    int DetailsPanelHeight = 190,
    int Version = 1);

public sealed record RawJsonWindowLayoutSettings(
    int SelectedTab = 0,
    string SearchText = "",
    bool WordWrap = false,
    bool DetailsVisible = true,
    int DifferencesPanelHeight = 180,
    int FontSize = 9,
    int Version = 1);

public sealed record PluginManagerWindowLayoutSettings(
    string SearchText = "",
    int ListPanelWidth = 430,
    int DetailsPanelHeight = 260,
    bool ShowDisabled = true,
    string? SelectedPluginName = null,
    int Version = 1);

public sealed record ProjectSummaryWindowLayoutSettings(
    int SelectedTab = 0,
    int Version = 1);

public enum EditorUiDensity
{
    Compact,
    Comfortable
}

public sealed record ApplicationPreferenceSettings(
    bool ShowUtilityWindowsInTaskbar = true,
    bool RestoreProjectWindows = true,
    bool ConfirmAdvancedRawJsonEditing = true,
    bool ConfirmDestructivePluginOperations = true,
    bool OpenOutputOnTaskFailure = true,
    bool OpenValidationWhenErrorsFound = false,
    string? DefaultProjectDirectory = null,
    int MonospaceEditorFontSize = 9,
    int UiScalePercent = 100,
    EditorUiDensity UiDensity = EditorUiDensity.Comfortable,
    bool FollowWindowsTextSize = true,
    int Version = 2);

public sealed record WindowPlacement(
    string PlacementId,
    ShellBounds NormalBounds,
    bool Maximized,
    string? MonitorDeviceName = null,
    int Version = 1);

public sealed record EditorSettings(
    EditorTheme Theme = EditorTheme.System,
    string? LastOpenDirectory = null,
    bool RestoreDocuments = true,
    IReadOnlyList<WindowPlacement>? WindowPlacements = null,
    bool ReopenLastProjectOnStartup = false,
    string? LastProjectRoot = null,
    MapEditorLayoutSettings? MapEditorLayout = null,
    DatabaseWindowLayoutSettings? DatabaseWindowLayout = null,
    ResourceManagerWindowLayoutSettings? ResourceManagerLayout = null,
    ValidationWindowLayoutSettings? ValidationWindowLayout = null,
    ReferenceExplorerWindowLayoutSettings? ReferenceExplorerWindowLayout = null,
    OutputWindowLayoutSettings? OutputWindowLayout = null,
    BackgroundTasksWindowLayoutSettings? BackgroundTasksWindowLayout = null,
    RawJsonWindowLayoutSettings? RawJsonWindowLayout = null,
    PluginManagerWindowLayoutSettings? PluginManagerWindowLayout = null,
    ProjectSummaryWindowLayoutSettings? ProjectSummaryWindowLayout = null,
    ApplicationPreferenceSettings? Preferences = null,
    int Version = EditorSettingsMigrator.CurrentVersion)
{
    public IReadOnlyList<WindowPlacement> EffectiveWindowPlacements => WindowPlacements ?? [];
    public MapEditorLayoutSettings EffectiveMapEditorLayout => MapEditorLayout ?? new MapEditorLayoutSettings();
    public DatabaseWindowLayoutSettings EffectiveDatabaseWindowLayout => DatabaseWindowLayout ?? new DatabaseWindowLayoutSettings();
    public ResourceManagerWindowLayoutSettings EffectiveResourceManagerLayout => ResourceManagerLayout ?? new ResourceManagerWindowLayoutSettings();
    public ValidationWindowLayoutSettings EffectiveValidationWindowLayout => ValidationWindowLayout ?? new ValidationWindowLayoutSettings();
    public ReferenceExplorerWindowLayoutSettings EffectiveReferenceExplorerWindowLayout => ReferenceExplorerWindowLayout ?? new ReferenceExplorerWindowLayoutSettings();
    public OutputWindowLayoutSettings EffectiveOutputWindowLayout => OutputWindowLayout ?? new OutputWindowLayoutSettings();
    public BackgroundTasksWindowLayoutSettings EffectiveBackgroundTasksWindowLayout => BackgroundTasksWindowLayout ?? new BackgroundTasksWindowLayoutSettings();
    public RawJsonWindowLayoutSettings EffectiveRawJsonWindowLayout => RawJsonWindowLayout ?? new RawJsonWindowLayoutSettings();
    public PluginManagerWindowLayoutSettings EffectivePluginManagerWindowLayout => PluginManagerWindowLayout ?? new PluginManagerWindowLayoutSettings();
    public ProjectSummaryWindowLayoutSettings EffectiveProjectSummaryWindowLayout => ProjectSummaryWindowLayout ?? new ProjectSummaryWindowLayoutSettings();
    public ApplicationPreferenceSettings EffectivePreferences => Preferences ?? new ApplicationPreferenceSettings();
}

public sealed record RecentProject(
    string ProjectRoot,
    string? Title,
    DateTimeOffset LastOpenedUtc,
    MvProjectLayoutKind LayoutKind,
    bool PathExists);

public enum ProjectOpenStage
{
    Locating,
    Loading,
    Inventorying,
    BuildingReferences,
    Validating,
    Ready
}

public sealed record ProjectOpenProgress(
    ProjectOpenStage Stage,
    int Completed,
    int Total,
    string? CurrentItem,
    string? Message);

public enum EditorDocumentKind
{
    StartCentre,
    ProjectSummary,
    RawJson,
    Database,
    SystemData,
    MapHierarchy,
    MapView,
    MapProperties,
    EventEditor,
    PluginInventory,
    AssetInventory,
    Validation,
    ProjectFile
}

public sealed record DocumentRequest(
    EditorDocumentKind Kind,
    string Identity,
    string? RelativePath = null,
    MvDocumentKind? DatabaseKind = null,
    int? RecordId = null,
    int? MapId = null,
    string? DisplayTitle = null,
    string? EventSurface = null,
    int? EventId = null,
    int? EventPageIndex = null);

public sealed record NavigationLocation(
    DocumentRequest Document,
    string? SelectionPath = null,
    int? RecordId = null,
    int? MapId = null);

public abstract record EditorSelection;
public sealed record EmptySelection : EditorSelection;
public sealed record ProjectSelection : EditorSelection;
public sealed record MapSelection(int MapId) : EditorSelection;
public sealed record MapTilePaletteSelection(int TileId, int SuggestedLayer, string SheetName) : EditorSelection;
public sealed record MapTileBrushSelection(
    string SheetName,
    int Width,
    int Height,
    IReadOnlyList<int> TileIds,
    int SuggestedLayer,
    int AnchorX = 0,
    int AnchorY = 0) : EditorSelection
{
    public int PrimaryTileId => TileIds.Count == 0 ? 0 : TileIds[0];
}
public sealed record DatabaseCategorySelection(MvDocumentKind Kind) : EditorSelection;
public sealed record DatabaseRecordSelection(MvDocumentKind Kind, int RecordId, string RelativePath) : EditorSelection;
public sealed record AssetSelection : EditorSelection
{
    public AssetSelection(Playroma.JRPG.Assets.AssetKey key, string? selectedVariantPath = null)
    {
        Key = key;
        SelectedVariantPath = selectedVariantPath;
    }

    public AssetSelection(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        Playroma.JRPG.Assets.AssetCategory category = Playroma.JRPG.Assets.AssetCategoryRegistry.TryResolveCategory(normalized, out Playroma.JRPG.Assets.AssetCategory parsed)
            ? parsed
            : Playroma.JRPG.Assets.AssetCategory.Unknown;
        Playroma.JRPG.Assets.AssetCategoryRegistry.TryCreateKey(normalized, category, out Playroma.JRPG.Assets.AssetKey key);
        Key = key;
        SelectedVariantPath = normalized;
    }

    public Playroma.JRPG.Assets.AssetKey Key { get; }
    public string? SelectedVariantPath { get; }
    public string RelativePath => SelectedVariantPath ?? Key.ToString();
}
public sealed record PluginSelection(int LoadOrder, string Name) : EditorSelection;
public sealed record DiagnosticSelection(ProjectValidationDiagnostic Diagnostic) : EditorSelection;
public sealed record ReferenceSelection(ProjectReference Reference) : EditorSelection;
public sealed record ProjectFileSelection(string RelativePath) : EditorSelection;

public enum ShellNotificationKind
{
    Information,
    Success,
    Warning,
    Error
}

public sealed record ShellNotification(
    ShellNotificationKind Kind,
    string Title,
    string Message,
    DateTimeOffset TimestampUtc,
    Exception? Exception = null);

public sealed record OutputEntry(
    DateTimeOffset TimestampUtc,
    string Category,
    string Message,
    DiagnosticSeverity? Severity = null,
    string? Detail = null,
    NavigationLocation? Location = null,
    Guid? RelatedTaskId = null);

public sealed class WorkspaceStateChangedEventArgs : EventArgs
{
    public WorkspaceStateChangedEventArgs(MvProjectSession? session, ProjectSessionState? state)
    {
        Session = session;
        State = state;
    }

    public MvProjectSession? Session { get; }
    public ProjectSessionState? State { get; }
}

public sealed class ValidationChangedEventArgs : EventArgs
{
    public ValidationChangedEventArgs(ProjectValidationResult? result) => Result = result;
    public ProjectValidationResult? Result { get; }
}

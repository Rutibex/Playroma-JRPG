using Playroma.JRPG.Core;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.ResourcesUI;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Navigation;

public interface IEditorNavigationCoordinator
{
    ValueTask NavigateAsync(
        NavigationLocation location,
        IWin32Window? source = null,
        bool recordHistory = true,
        CancellationToken cancellationToken = default);

    ValueTask NavigateToSelectionAsync(
        EditorSelection selection,
        IWin32Window? source = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Routes diagnostics, references and utility-window actions through the primary workspace
/// instead of allowing each tool to maintain a partial copy of window-opening logic.
/// </summary>
public sealed class EditorNavigationCoordinator : IEditorNavigationCoordinator
{
    private readonly IEditorApplicationController _application;
    private readonly IWorkspaceService _workspace;
    private readonly IWindowService _windows;
    private readonly INavigationService _history;

    public EditorNavigationCoordinator(
        IEditorApplicationController application,
        IWorkspaceService workspace,
        IWindowService windows,
        INavigationService history)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public async ValueTask NavigateAsync(
        NavigationLocation location,
        IWin32Window? source = null,
        bool recordHistory = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        IProjectWorkspaceHost workspaceHost = _application.CurrentWorkspaceHost
            ?? throw new InvalidOperationException("No project workspace is open.");

        _history.Navigate(location, recordHistory);
        await workspaceHost.OpenDocumentAsync(location.Document, cancellationToken).ConfigureAwait(true);

        WindowKey? key = ResolveWindowKey(location.Document);
        if (key.HasValue && _windows.TryGet(key.Value, out IEditorWindow? window) && window is not null)
        {
            await window.RestoreLocationAsync(location, cancellationToken).ConfigureAwait(true);
            _windows.Activate(window);
        }
        else
        {
            Form form = workspaceHost.Form;
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            form.Activate();
        }

    }

    public ValueTask NavigateToSelectionAsync(
        EditorSelection selection,
        IWin32Window? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        NavigationLocation location = selection switch
        {
            MapSelection map => new NavigationLocation(new DocumentRequest(
                EditorDocumentKind.MapView,
                $"map-view:{map.MapId}",
                MapId: map.MapId,
                DisplayTitle: $"Map {map.MapId}"), MapId: map.MapId),
            DatabaseRecordSelection record => DatabaseLocation(record.Kind, record.RecordId, record.RelativePath),
            DatabaseCategorySelection category => DatabaseLocation(category.Kind, null, null),
            AssetSelection asset => new NavigationLocation(new DocumentRequest(
                EditorDocumentKind.AssetInventory,
                "assets",
                RelativePath: asset.SelectedVariantPath,
                DisplayTitle: "Resource Manager"),
                SelectionPath: asset.Key.ToString()),
            PluginSelection plugin => new NavigationLocation(new DocumentRequest(
                EditorDocumentKind.PluginInventory,
                "plugins",
                RecordId: plugin.LoadOrder,
                DisplayTitle: "Plugin Manager",
                EventSurface: plugin.Name),
                SelectionPath: plugin.Name,
                RecordId: plugin.LoadOrder),
            DiagnosticSelection diagnostic => FromProjectLocation(diagnostic.Diagnostic.Diagnostic.Location),
            ReferenceSelection reference => FromReferenceSource(reference.Reference.Source),
            ProjectFileSelection file => new NavigationLocation(new DocumentRequest(
                EditorDocumentKind.RawJson,
                "file:" + file.RelativePath,
                file.RelativePath,
                DisplayTitle: Path.GetFileName(file.RelativePath))),
            _ => throw new NotSupportedException($"Selection '{selection.GetType().Name}' cannot be navigated.")
        };
        return NavigateAsync(location, source, recordHistory: true, cancellationToken);
    }

    public static NavigationLocation FromProjectLocation(ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.MapId.HasValue)
        {
            DocumentRequest map = new(
                EditorDocumentKind.MapView,
                $"map-view:{location.MapId.Value}",
                location.RelativePath,
                MapId: location.MapId,
                DisplayTitle: $"Map {location.MapId.Value}",
                EventId: location.EventId,
                EventPageIndex: location.PageIndex);
            return new NavigationLocation(map, location.JsonPath, location.RecordId, location.MapId);
        }

        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            MvDocumentKind? kind = GuessDatabaseKind(location.RelativePath);
            if (kind.HasValue)
            {
                return DatabaseLocation(kind.Value, location.RecordId, location.RelativePath, location.JsonPath);
            }
            DocumentRequest raw = new(
                EditorDocumentKind.RawJson,
                "file:" + location.RelativePath,
                location.RelativePath,
                RecordId: location.RecordId,
                DisplayTitle: Path.GetFileName(location.RelativePath));
            return new NavigationLocation(raw, location.JsonPath, location.RecordId, location.MapId);
        }

        return new NavigationLocation(new DocumentRequest(
            EditorDocumentKind.Validation,
            "validation",
            DisplayTitle: "Validation"));
    }

    public static NavigationLocation? FromReferenceTarget(ReferenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind == ReferenceTargetKind.Asset && !string.IsNullOrWhiteSpace(target.Name))
        {
            return new ResourceNavigationTarget(
                target.AssetCategory,
                target.Name).ToLocation();
        }

        if (target.Kind == ReferenceTargetKind.Plugin && !string.IsNullOrWhiteSpace(target.Name))
        {
            DocumentRequest plugin = new(
                EditorDocumentKind.PluginInventory,
                "plugins",
                DisplayTitle: "Plugin Manager",
                EventSurface: target.Name);
            return new NavigationLocation(plugin, SelectionPath: target.Name);
        }

        if (target.Kind == ReferenceTargetKind.Map && target.Id.HasValue)
        {
            DocumentRequest map = new(
                EditorDocumentKind.MapView,
                $"map-view:{target.Id.Value}",
                MapId: target.Id.Value,
                DisplayTitle: $"Map {target.Id.Value}");
            return new NavigationLocation(map, MapId: target.Id.Value);
        }

        MvDocumentKind? databaseKind = target.Kind switch
        {
            ReferenceTargetKind.Actor => MvDocumentKind.Actors,
            ReferenceTargetKind.Class => MvDocumentKind.Classes,
            ReferenceTargetKind.Skill => MvDocumentKind.Skills,
            ReferenceTargetKind.Item => MvDocumentKind.Items,
            ReferenceTargetKind.Weapon => MvDocumentKind.Weapons,
            ReferenceTargetKind.Armor or ReferenceTargetKind.Equipment => MvDocumentKind.Armors,
            ReferenceTargetKind.Enemy => MvDocumentKind.Enemies,
            ReferenceTargetKind.Troop or ReferenceTargetKind.TroopMember => MvDocumentKind.Troops,
            ReferenceTargetKind.State => MvDocumentKind.States,
            ReferenceTargetKind.Animation => MvDocumentKind.Animations,
            ReferenceTargetKind.Tileset => MvDocumentKind.Tilesets,
            ReferenceTargetKind.CommonEvent => MvDocumentKind.CommonEvents,
            _ => null
        };
        return databaseKind.HasValue
            ? DatabaseLocation(databaseKind.Value, target.Id, null)
            : null;
    }

    public static NavigationLocation FromReferenceSource(ReferenceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.MapId.HasValue)
        {
            DocumentRequest map = new(
                EditorDocumentKind.MapView,
                $"map-view:{source.MapId.Value}",
                source.RelativePath,
                MapId: source.MapId,
                DisplayTitle: $"Map {source.MapId.Value}",
                EventId: source.EventId,
                EventPageIndex: source.PageIndex);
            return new NavigationLocation(map, source.JsonPath, source.RecordId, source.MapId);
        }

        MvDocumentKind? kind = GuessDatabaseKind(source.RelativePath);
        return kind.HasValue
            ? DatabaseLocation(kind.Value, source.RecordId, source.RelativePath, source.JsonPath)
            : new NavigationLocation(new DocumentRequest(
                EditorDocumentKind.RawJson,
                "file:" + source.RelativePath,
                source.RelativePath,
                RecordId: source.RecordId,
                DisplayTitle: Path.GetFileName(source.RelativePath)),
                source.JsonPath,
                source.RecordId,
                source.MapId);
    }

    private static NavigationLocation DatabaseLocation(
        MvDocumentKind kind,
        int? recordId,
        string? relativePath,
        string? selectionPath = null)
    {
        string relative = relativePath ?? DefaultDatabasePath(kind);
        DocumentRequest request = new(
            kind == MvDocumentKind.System ? EditorDocumentKind.SystemData : EditorDocumentKind.Database,
            "database:" + kind,
            relative,
            kind,
            recordId,
            DisplayTitle: kind.ToString(),
            EventSurface: kind.ToString());
        return new NavigationLocation(request, selectionPath, recordId);
    }

    private static WindowKey? ResolveWindowKey(DocumentRequest request) => request.Kind switch
    {
        EditorDocumentKind.Database or EditorDocumentKind.SystemData => new WindowKey(EditorWindowKind.Database, "database"),
        EditorDocumentKind.AssetInventory => new WindowKey(EditorWindowKind.ResourceManager, "resource-manager"),
        EditorDocumentKind.PluginInventory => new WindowKey(EditorWindowKind.PluginManager, "plugin-manager"),
        EditorDocumentKind.RawJson or EditorDocumentKind.ProjectFile => new WindowKey(EditorWindowKind.RawJson, request.Identity),
        EditorDocumentKind.ProjectSummary => new WindowKey(EditorWindowKind.ProjectSummary, "project-summary"),
        EditorDocumentKind.Validation => new WindowKey(EditorWindowKind.Validation, "validation"),
        _ => null
    };

    private static MvDocumentKind? GuessDatabaseKind(string relativePath)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        return Enum.TryParse(name, ignoreCase: true, out MvDocumentKind kind)
            && kind != MvDocumentKind.Map
            && kind != MvDocumentKind.MapInfos
            && kind != MvDocumentKind.Unknown
            ? kind
            : null;
    }

    private static string DefaultDatabasePath(MvDocumentKind kind) => $"data/{kind}.json";
}

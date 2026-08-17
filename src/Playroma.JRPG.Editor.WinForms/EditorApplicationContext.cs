using System.Text;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Commands;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.ToolWindows;
using Playroma.JRPG.Editor.WinForms.Forms;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms;

/// <summary>
/// Stable process lifetime owner. The Project Hub and every project workspace can be shown,
/// hidden, created, and destroyed without replacing this application context.
/// </summary>
public sealed class EditorApplicationContext : ApplicationContext
{
    private readonly ShellServiceContainer _services;
    private readonly string[] _args;
    private readonly Control _uiDispatcher;
    private readonly WinFormsOwnerProvider _ownerProvider;
    private readonly WinFormsWindowService _windows;
    private readonly EditorApplicationController _controller;
    private readonly ProjectHubForm _projectHub;
    private bool _startupComplete;
    private bool _servicesDisposed;
    private bool _exitThreadRequested;

    public EditorApplicationContext(ShellServiceContainer services, string[] args)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _args = args ?? [];

        _uiDispatcher = new Control();
        _uiDispatcher.CreateControl();
        _ = _uiDispatcher.Handle;

        ISettingsService settings = services.GetRequired<ISettingsService>();
        IRecentProjectService recent = services.GetRequired<IRecentProjectService>();
        IRecoveryService recovery = services.GetRequired<IRecoveryService>();
        INotificationService notifications = services.GetRequired<INotificationService>();

        WinFormsThemeService theme = new(settings);
        WinFormsWindowPlacementService placement = new(settings);
        WinFormsWindowFactoryRegistry factories = new();
        _ownerProvider = new WinFormsOwnerProvider();
        _windows = new WinFormsWindowService(
            _uiDispatcher,
            theme,
            placement,
            notifications,
            factories,
            _ownerProvider);
        _ownerProvider.ActiveWindows = _windows;

        services.Add(theme);
        services.Add(placement);
        services.Add(factories);
        services.Add<IWinFormsOwnerProvider>(_ownerProvider);
        services.Add(_ownerProvider);
        services.Add<IWindowService>(_windows);
        services.Add<IActiveWindowService>(_windows);
        services.Add<IWinFormsWindowService>(_windows);

        ShellCommandService commands = new(services);
        services.Add<ICommandService>(commands);

        WinFormsCommandRouter commandRouter = new(
            _windows,
            services.GetRequired<ISelectionService>(),
            commands,
            notifications);
        services.Add<IEditorCommandRouter>(commandRouter);
        services.Add(commandRouter);

        WinFormsCommandDispatcher commandDispatcher = new(commandRouter);
        services.Add(commandDispatcher);

        _controller = new EditorApplicationController(
            services,
            theme,
            placement,
            _ownerProvider,
            CompleteApplicationExitAsync);
        services.Add<IEditorApplicationController>(_controller);
        services.Add(_controller);

        WinFormsWorkspaceRecoveryCoordinator workspaceRecovery = new(
            _uiDispatcher,
            _controller,
            services.GetRequired<IWorkspaceService>(),
            services.GetRequired<IProjectWorkspaceStateStore>(),
            services.GetRequired<WorkspaceRecoveryContextBroker>(),
            _windows,
            services.GetRequired<IOutputService>());
        services.Add<IWinFormsWorkspaceRecoveryCoordinator>(workspaceRecovery);
        services.Add(workspaceRecovery);
        _controller.AttachWorkspaceRecoveryCoordinator(workspaceRecovery);

        EditorNavigationCoordinator navigationCoordinator = new(
            _controller,
            services.GetRequired<IWorkspaceService>(),
            _windows,
            services.GetRequired<INavigationService>());
        services.Add<IEditorNavigationCoordinator>(navigationCoordinator);
        services.Add(navigationCoordinator);
        RegisterEditorWindowFactories(factories);

        UtilityWindowNotificationCoordinator utilityNotifications = new(
            services.GetRequired<IWorkspaceService>(),
            services.GetRequired<IBackgroundTaskService>(),
            _windows,
            settings,
            notifications);
        services.Add(utilityNotifications);

        ApplicationCommandRegistrar registrar = new(
            services,
            _controller,
            _ownerProvider,
            theme);
        registrar.Register();
        services.Add(registrar);

        _projectHub = new ProjectHubForm(
            _controller,
            settings,
            recent,
            recovery,
            theme,
            placement,
            _windows);
        _controller.AttachProjectHub(_projectHub);
        _projectHub.Shown += OnProjectHubShown;
        _projectHub.Show();
    }

    public ProjectHubForm ProjectHub => _projectHub;
    public EditorApplicationController Controller => _controller;
    public IWinFormsWindowService WindowService => _windows;
    public WinFormsWindowFactoryRegistry WindowFactories => _windows.Factories;

    public void ReportUnhandledException(Exception exception)
    {
        try
        {
            string path = Path.Combine(
                AppDataPaths.Logs,
                "crash-" + DateTimeOffset.Now.ToString(
                    "yyyyMMdd-HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture) + ".log");
            Directory.CreateDirectory(AppDataPaths.Logs);
            File.WriteAllText(path, exception.ToString(), Encoding.UTF8);
            _services.GetRequired<INotificationService>().Error(
                "Unexpected error",
                exception.Message,
                exception);
            MessageBox.Show(
                _ownerProvider.CurrentOwner,
                $"An unexpected error occurred.\n\n{exception.Message}\n\nA diagnostic log was written to:\n{path}",
                "Playroma jRPG",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Last-resort exception handling must not recursively fail.
        }
    }

    protected override void ExitThreadCore()
    {
        if (_exitThreadRequested) return;
        _exitThreadRequested = true;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _projectHub.Shown -= OnProjectHubShown;
            if (!_servicesDisposed)
            {
                try
                {
                    _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // The process is already leaving; startup exception reporting handled details.
                }
                _servicesDisposed = true;
            }
            _uiDispatcher.Dispose();
        }
        base.Dispose(disposing);
    }

    private async void OnProjectHubShown(object? sender, EventArgs e)
    {
        if (_startupComplete) return;
        _startupComplete = true;
        try
        {
            await _services.GetRequired<ISettingsService>().LoadAsync().ConfigureAwait(true);
            await _services.GetRequired<IRecentProjectService>().LoadAsync().ConfigureAwait(true);
            bool resetLayout = _args.Contains("--reset-layout", StringComparer.OrdinalIgnoreCase);
            bool safeMode = _args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
            await _projectHub.InitializeAsync(resetLayout).ConfigureAwait(true);
            _controller.MarkHubReady();

            string? project = ParseProjectArgument(_args);
            if (project is null && !safeMode)
            {
                EditorSettings settings = _services.GetRequired<ISettingsService>().Current;
                if (settings.ReopenLastProjectOnStartup
                    && !string.IsNullOrWhiteSpace(settings.LastProjectRoot)
                    && Directory.Exists(settings.LastProjectRoot))
                {
                    project = settings.LastProjectRoot;
                }
            }

            if (project is not null)
            {
                await _controller.OpenProjectAsync(project).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            ReportUnhandledException(exception);
            _controller.MarkHubReady();
            _projectHub.ShowHub();
        }
    }

    private async ValueTask CompleteApplicationExitAsync()
    {
        if (!_servicesDisposed)
        {
            await _services.DisposeAsync().ConfigureAwait(true);
            _servicesDisposed = true;
        }
        if (!_uiDispatcher.IsDisposed) _uiDispatcher.Dispose();
        ExitThread();
    }

    private void RegisterEditorWindowFactories(WinFormsWindowFactoryRegistry factories)
    {
        factories.Register(
            EditorWindowKind.EventEditor,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new EventEditorForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _windows,
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.Database,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new DatabaseForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISelectionService>(),
                    _windows,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.ResourceManager,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new ResourceManagerForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISelectionService>(),
                    _windows,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>(),
                    _services.GetRequired<WinFormsThemeService>())));

        factories.Register(
            EditorWindowKind.PluginManager,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new PluginManagerForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISelectionService>(),
                    _windows,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.Validation,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new ValidationForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISelectionService>(),
                    _services.GetRequired<IEditorNavigationCoordinator>(),
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.References,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new ReferenceExplorerForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISelectionService>(),
                    _services.GetRequired<IEditorNavigationCoordinator>(),
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.Output,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new OutputForm(
                    request,
                    _services.GetRequired<IOutputService>(),
                    _services.GetRequired<IEditorNavigationCoordinator>(),
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.BackgroundTasks,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new BackgroundTasksForm(
                    request,
                    _services.GetRequired<IBackgroundTaskService>(),
                    _windows,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.RawJson,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new RawJsonForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>(),
                    _services.GetRequired<IEditorNavigationCoordinator>())));

        factories.Register(
            EditorWindowKind.ProjectSummary,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new ProjectSummaryForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _controller,
                    _windows,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.Preferences,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new PreferencesForm(
                    request,
                    _services.GetRequired<ISettingsService>(),
                    _services.GetRequired<WinFormsThemeService>(),
                    _services.GetRequired<INotificationService>())));

        factories.Register(
            EditorWindowKind.RecoveredSessions,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new RecoveredSessionsForm(
                    request,
                    _services.GetRequired<IWinFormsWorkspaceRecoveryCoordinator>())));

        factories.Register(
            EditorWindowKind.MapProperties,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new MapPropertiesForm(request, _services.GetRequired<IWorkspaceService>(), _windows)));

        factories.Register(
            EditorWindowKind.MusicStudio,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new MusicStudioForm(request, _services.GetRequired<IWorkspaceService>())));

        factories.Register(
            EditorWindowKind.SoundStudio,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new SoundStudioForm(request, _services.GetRequired<IWorkspaceService>())));

        factories.Register(
            EditorWindowKind.TileStudio,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new TileStudioForm(request, _services.GetRequired<IWorkspaceService>())));

        factories.Register(
            EditorWindowKind.SpriteStudio,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new SpriteStudioForm(request, _services.GetRequired<IWorkspaceService>())));

        factories.Register(
            EditorWindowKind.TextStudio,
            (request, _) => ValueTask.FromResult<IWinFormsEditorWindow>(
                new TextStudioForm(
                    request,
                    _services.GetRequired<IWorkspaceService>(),
                    _services.GetRequired<INotificationService>(),
                    _services.GetRequired<IOutputService>())));
    }

    private static string? ParseProjectArgument(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
            if (Directory.Exists(arg) || File.Exists(arg)) return arg;
        }
        return null;
    }
}

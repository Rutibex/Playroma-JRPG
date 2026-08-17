using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        Directory.CreateDirectory(AppDataPaths.Logs);
        using SingleInstanceGuard instance = new("Playroma.JRPG.Editor.WinForms");
        if (!instance.IsPrimary)
        {
            MessageBox.Show(
                "Playroma jRPG is already running.",
                "Playroma jRPG",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ShellServiceContainer services = BuildServices();
        using EditorApplicationContext context = new(services, args);
        System.Windows.Forms.Application.ThreadException += (_, eventArgs) =>
            context.ReportUnhandledException(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            context.ReportUnhandledException(
                eventArgs.ExceptionObject as Exception
                ?? new InvalidOperationException("Unknown unhandled exception."));
        System.Windows.Forms.Application.Run(context);
    }

    internal static ShellServiceContainer BuildServices()
    {
        ShellServiceContainer services = new();
        JsonSettingsService settings = new();
        RecentProjectService recent = new();
        BackgroundTaskService tasks = new();
        NotificationService notifications = new();
        OutputService output = new();
        RecoveryService recovery = new();
        JsonProjectWorkspaceStateStore workspaceState = new();
        WorkspaceRecoveryContextBroker recoveryContext = new();
        ProjectFileWatchService fileWatcher = new();
        SelectionService selection = new();
        NavigationService navigation = new();
        WorkspaceService workspace = new(tasks, recent, notifications, output, recovery, recoveryContext);
        services.Add<ISettingsService>(settings);
        services.Add<IRecentProjectService>(recent);
        services.Add<IBackgroundTaskService>(tasks);
        services.Add<INotificationService>(notifications);
        services.Add<IOutputService>(output);
        services.Add<IRecoveryService>(recovery);
        services.Add<IProjectWorkspaceStateStore>(workspaceState);
        services.Add(recoveryContext);
        services.Add<IProjectFileWatchService>(fileWatcher);
        services.Add<ISelectionService>(selection);
        services.Add<INavigationService>(navigation);
        services.Add<IWorkspaceService>(workspace);
        return services;
    }
}

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, "Local\\" + name, out bool createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

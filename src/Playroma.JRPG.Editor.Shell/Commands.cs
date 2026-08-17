namespace Playroma.JRPG.Editor.Shell;

public sealed class ShellCommandContext
{
    public ShellCommandContext(IServiceProvider services) => Services = services ?? throw new ArgumentNullException(nameof(services));
    public IServiceProvider Services { get; }
    public T GetRequiredService<T>() where T : notnull =>
        (T)(Services.GetService(typeof(T)) ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered."));
}

public interface IShellCommand
{
    string Id { get; }
    string DisplayName { get; }
    string? Description { get; }
    string? IconKey { get; }
    string? DefaultShortcut { get; }
    bool CanExecute(ShellCommandContext context);
    ValueTask ExecuteAsync(ShellCommandContext context, CancellationToken cancellationToken = default);
}

public interface ICommandService
{
    IReadOnlyList<IShellCommand> Commands { get; }
    event EventHandler? CommandStatesChanged;
    void Register(IShellCommand command);
    IShellCommand? Find(string id);
    bool CanExecute(string id);
    ValueTask ExecuteAsync(string id, CancellationToken cancellationToken = default);
    void Invalidate();
}

public sealed class ShellCommandService : ICommandService
{
    private readonly Dictionary<string, IShellCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShellCommandContext _context;

    public ShellCommandService(IServiceProvider services) => _context = new ShellCommandContext(services);

    public event EventHandler? CommandStatesChanged;
    public IReadOnlyList<IShellCommand> Commands => _commands.Values.OrderBy(command => command.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public void Register(IShellCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command)) throw new InvalidOperationException($"A shell command with ID '{command.Id}' is already registered.");
        Invalidate();
    }

    public IShellCommand? Find(string id) => _commands.GetValueOrDefault(id);

    public bool CanExecute(string id) => Find(id)?.CanExecute(_context) == true;

    public async ValueTask ExecuteAsync(string id, CancellationToken cancellationToken = default)
    {
        IShellCommand command = Find(id) ?? throw new KeyNotFoundException($"Shell command '{id}' is not registered.");
        if (!command.CanExecute(_context)) return;
        await command.ExecuteAsync(_context, cancellationToken).ConfigureAwait(false);
        Invalidate();
    }

    public void Invalidate() => CommandStatesChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DelegateShellCommand : IShellCommand
{
    private readonly Func<ShellCommandContext, bool> _canExecute;
    private readonly Func<ShellCommandContext, CancellationToken, ValueTask> _execute;

    public DelegateShellCommand(
        string id,
        string displayName,
        Func<ShellCommandContext, CancellationToken, ValueTask> execute,
        Func<ShellCommandContext, bool>? canExecute = null,
        string? description = null,
        string? iconKey = null,
        string? defaultShortcut = null)
    {
        Id = id;
        DisplayName = displayName;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (_ => true);
        Description = description;
        IconKey = iconKey;
        DefaultShortcut = defaultShortcut;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public string? IconKey { get; }
    public string? DefaultShortcut { get; }
    public bool CanExecute(ShellCommandContext context) => _canExecute(context);
    public ValueTask ExecuteAsync(ShellCommandContext context, CancellationToken cancellationToken = default) => _execute(context, cancellationToken);
}

public static class ShellCommandIds
{
    public const string FileOpenProject = "File.OpenProject";
    public const string FileCloseProject = "File.CloseProject";
    public const string FileSave = "File.Save";
    public const string FileExit = "File.Exit";
    public const string EditUndo = "Edit.Undo";
    public const string EditRedo = "Edit.Redo";
    public const string NavigateBack = "Navigate.Back";
    public const string NavigateForward = "Navigate.Forward";
    public const string NavigateCommandPalette = "Navigate.CommandPalette";
    public const string ProjectValidate = "Project.Validate";
    public const string ProjectRefreshReferences = "Project.RefreshReferences";
    public const string ProjectShowSummary = "Project.ShowSummary";
    public const string ProjectOpenFolder = "Project.OpenFolder";
    public const string ToolsOpenTextStudio = "Tools.OpenTextStudio";
    public const string ViewResetLayout = "View.ResetLayout";
    public const string ViewThemeSystem = "View.Theme.System";
    public const string ViewThemeLight = "View.Theme.Light";
    public const string ViewThemeDark = "View.Theme.Dark";
}

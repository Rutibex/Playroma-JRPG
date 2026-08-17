using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Commands;

public sealed class WinFormsCommandRouter : IEditorCommandRouter, IDisposable
{
    private readonly IActiveWindowService _activeWindows;
    private readonly ISelectionService _selection;
    private readonly ICommandService _shellCommands;
    private readonly INotificationService _notifications;
    private readonly Dictionary<EditorCommandId, EditorCommandDescriptor> _descriptors = [];
    private readonly HashSet<EditorCommandId> _executing = [];
    private bool _disposed;

    public WinFormsCommandRouter(
        IActiveWindowService activeWindows,
        ISelectionService selection,
        ICommandService shellCommands,
        INotificationService notifications)
    {
        _activeWindows = activeWindows;
        _selection = selection;
        _shellCommands = shellCommands;
        _notifications = notifications;
        foreach (EditorCommandDescriptor descriptor in EditorCommandIds.Defaults) RegisterDescriptor(descriptor);
        _activeWindows.ActiveWindowChanged += OnRouteChanged;
        _selection.SelectionChanged += OnCommandStateChanged;
        _shellCommands.CommandStatesChanged += OnCommandStateChanged;
    }

    public event EventHandler? RouteChanged;
    public event EventHandler? CommandStatesChanged;
    public IReadOnlyList<EditorCommandDescriptor> AvailableCommands =>
        _descriptors.Values.OrderBy(x => x.Category).ThenBy(x => x.DisplayName).ToArray();

    public void RegisterDescriptor(EditorCommandDescriptor descriptor)
    {
        _descriptors[descriptor.Id] = descriptor;
        Invalidate();
    }

    public EditorCommandStatus Query(EditorCommandId command)
    {
        EditorCommandContext context = CreateContext(command, CommandInvocationSource.Menu, CancellationToken.None);
        foreach (IEditorCommandTarget target in BuildRoute())
        {
            EditorCommandStatus status = target.QueryCommand(command, context);
            if (status.Handling != CommandHandling.NotHandled) return ApplyDescriptor(command, status);
        }
        return EditorCommandStatus.NotHandled(command);
    }

    public async ValueTask<bool> ExecuteAsync(
        EditorCommandId command,
        CommandInvocationSource source,
        CancellationToken cancellationToken = default)
    {
        if (_executing.Contains(command)) return false;
        EditorCommandContext context = CreateContext(command, source, cancellationToken);
        foreach (IEditorCommandTarget target in BuildRoute())
        {
            EditorCommandStatus status = target.QueryCommand(command, context);
            if (status.Handling == CommandHandling.NotHandled) continue;
            if (status.Handling == CommandHandling.Disabled) return true;
            _executing.Add(command);
            Invalidate();
            try
            {
                await target.ExecuteCommandAsync(command, context).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _notifications.Error("Command failed", $"{GetName(command)} could not be completed.", exception);
            }
            finally
            {
                _executing.Remove(command);
                Invalidate();
            }
            return true;
        }
        return false;
    }

    public void Invalidate() => CommandStatesChanged?.Invoke(this, EventArgs.Empty);

    private IEnumerable<IEditorCommandTarget> BuildRoute()
    {
        Control? focused = WinFormsFocusTracker.FindFocusedControl();
        if (focused is not null)
        {
            NativeControlCommandTarget native = new(focused);
            yield return native;
            for (Control? current = focused; current is not null; current = current.Parent)
            {
                if (current is IEditorCommandTarget direct) yield return direct;
                if (current is IEditorCommandTargetProvider provider)
                    foreach (IEditorCommandTarget target in provider.GetCommandTargets(focused)) yield return target;
            }
        }

        Form? activeForm = Form.ActiveForm;
        if (activeForm is IEditorCommandTarget formTarget) yield return formTarget;
        if (activeForm is IEditorCommandTargetProvider formProvider)
            foreach (IEditorCommandTarget target in formProvider.GetCommandTargets(focused)) yield return target;

        yield return new WindowCommandTarget(activeForm);
        yield return new ShellCommandTarget(_shellCommands);
    }

    private EditorCommandContext CreateContext(
        EditorCommandId command,
        CommandInvocationSource source,
        CancellationToken cancellationToken) =>
        new(_activeWindows.ActiveWindow?.Key, _selection.Current, source, cancellationToken);

    private EditorCommandStatus ApplyDescriptor(EditorCommandId command, EditorCommandStatus status)
    {
        if (_executing.Contains(command)) return EditorCommandStatus.Disabled(GetName(command), "Command is already running.");
        if (!_descriptors.TryGetValue(command, out EditorCommandDescriptor? descriptor)) return status;
        return status with
        {
            DisplayName = string.IsNullOrWhiteSpace(status.DisplayName) ? descriptor.DisplayName : status.DisplayName,
            Description = status.Description ?? descriptor.Description
        };
    }

    private string GetName(EditorCommandId id) =>
        _descriptors.TryGetValue(id, out EditorCommandDescriptor? value) ? value.DisplayName : id.Value;

    private void OnRouteChanged(object? sender, EventArgs e)
    {
        RouteChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }
    private void OnCommandStateChanged(object? sender, EventArgs e) => Invalidate();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeWindows.ActiveWindowChanged -= OnRouteChanged;
        _selection.SelectionChanged -= OnCommandStateChanged;
        _shellCommands.CommandStatesChanged -= OnCommandStateChanged;
    }
}

internal sealed class ShellCommandTarget(ICommandService commands) : IEditorCommandTarget
{
    public event EventHandler? CommandStateChanged { add { } remove { } }
    public EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        string? shellId = Map(command);
        if (shellId is null || commands.Find(shellId) is not IShellCommand shell) return EditorCommandStatus.NotHandled(command);
        return commands.CanExecute(shellId)
            ? EditorCommandStatus.Enabled(shell.DisplayName, shell.Description)
            : EditorCommandStatus.Disabled(shell.DisplayName);
    }
    public ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context) =>
        Map(command) is string id ? commands.ExecuteAsync(id, context.CancellationToken) : ValueTask.CompletedTask;
    private static string? Map(EditorCommandId id) => id.Value switch
    {
        "File.OpenProject" => ShellCommandIds.FileOpenProject,
        "File.CloseProject" => ShellCommandIds.FileCloseProject,
        "File.SaveProject" => ShellCommandIds.FileSave,
        "File.Exit" => ShellCommandIds.FileExit,
        "Edit.Undo" => ShellCommandIds.EditUndo,
        "Edit.Redo" => ShellCommandIds.EditRedo,
        "Navigate.Back" => ShellCommandIds.NavigateBack,
        "Navigate.Forward" => ShellCommandIds.NavigateForward,
        "Navigate.CommandPalette" => ShellCommandIds.NavigateCommandPalette,
        "Project.Validate" => ShellCommandIds.ProjectValidate,
        "Project.RefreshReferences" => ShellCommandIds.ProjectRefreshReferences,
        "Project.ShowSummary" => ShellCommandIds.ProjectShowSummary,
        _ => null
    };
}

internal sealed class WindowCommandTarget(Form? activeForm) : IEditorCommandTarget
{
    public event EventHandler? CommandStateChanged { add { } remove { } }
    public EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context) =>
        command == EditorCommandIds.WindowClose && activeForm is not null
            ? EditorCommandStatus.Enabled("Close Window")
            : EditorCommandStatus.NotHandled(command);
    public ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.WindowClose) activeForm?.Close();
        return ValueTask.CompletedTask;
    }
}

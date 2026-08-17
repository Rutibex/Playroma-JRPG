using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Workspace;

namespace Playroma.Sound.Authoring.Commands;

public sealed record AuthoringCommandContext(
    AuthoringWorkspace? Workspace = null,
    IAuthoringDocument? ActiveDocument = null,
    object? Parameter = null);

public sealed record AuthoringCommandState(
    string Id,
    string DisplayName,
    string Category,
    string Shortcut,
    bool CanExecute);

public interface IAuthoringCommand
{
    string Id { get; }
    string DisplayName { get; }
    string Category { get; }
    string Shortcut { get; }
    bool CanExecute(AuthoringCommandContext context);
    ValueTask ExecuteAsync(AuthoringCommandContext context, CancellationToken cancellationToken = default);
}

public sealed class DelegateAuthoringCommand : IAuthoringCommand
{
    private readonly Func<AuthoringCommandContext, bool> _canExecute;
    private readonly Func<AuthoringCommandContext, CancellationToken, ValueTask> _execute;

    public DelegateAuthoringCommand(
        string id,
        string displayName,
        string category,
        string shortcut,
        Func<AuthoringCommandContext, CancellationToken, ValueTask> execute,
        Func<AuthoringCommandContext, bool>? canExecute = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
        Category = category ?? "";
        Shortcut = shortcut ?? "";
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (_ => true);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Shortcut { get; }
    public bool CanExecute(AuthoringCommandContext context) => _canExecute(context ?? throw new ArgumentNullException(nameof(context)));

    public ValueTask ExecuteAsync(AuthoringCommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanExecute(context)) throw new InvalidOperationException($"Authoring command '{Id}' is not currently available.");
        cancellationToken.ThrowIfCancellationRequested();
        return _execute(context, cancellationToken);
    }
}

public sealed class AuthoringCommandRegistry
{
    private readonly Dictionary<string, IAuthoringCommand> _commands = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IAuthoringCommand> Commands => _commands.Values.OrderBy(item => item.Category, StringComparer.Ordinal).ThenBy(item => item.DisplayName, StringComparer.Ordinal).ToArray();

    public void Register(IAuthoringCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command)) throw new InvalidOperationException($"Authoring command ID '{command.Id}' is already registered.");
    }

    public bool Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _commands.Remove(id);
    }

    public IAuthoringCommand GetRequired(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _commands.TryGetValue(id, out IAuthoringCommand? command)
            ? command
            : throw new KeyNotFoundException($"Authoring command '{id}' was not registered.");
    }

    public AuthoringCommandState GetState(string id, AuthoringCommandContext context)
    {
        IAuthoringCommand command = GetRequired(id);
        return new AuthoringCommandState(command.Id, command.DisplayName, command.Category, command.Shortcut, command.CanExecute(context));
    }

    public ValueTask ExecuteAsync(string id, AuthoringCommandContext context, CancellationToken cancellationToken = default) => GetRequired(id).ExecuteAsync(context, cancellationToken);
}

public static class BuiltInAuthoringCommands
{
    public const string SaveDocument = "document.save";
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string SaveProject = "project.save";
    public const string SaveAll = "project.save-all";

    public static AuthoringCommandRegistry CreateDefault()
    {
        var registry = new AuthoringCommandRegistry();
        registry.Register(new DelegateAuthoringCommand(
            SaveDocument,
            "Save Document",
            "File",
            "Ctrl+S",
            (context, _) =>
            {
                context.ActiveDocument!.Save();
                return ValueTask.CompletedTask;
            },
            context => context.ActiveDocument is { IsDirty: true }));
        registry.Register(new DelegateAuthoringCommand(
            Undo,
            "Undo",
            "Edit",
            "Ctrl+Z",
            (context, _) =>
            {
                context.ActiveDocument!.Undo();
                return ValueTask.CompletedTask;
            },
            context => context.ActiveDocument?.CanUndo == true));
        registry.Register(new DelegateAuthoringCommand(
            Redo,
            "Redo",
            "Edit",
            "Ctrl+Y",
            (context, _) =>
            {
                context.ActiveDocument!.Redo();
                return ValueTask.CompletedTask;
            },
            context => context.ActiveDocument?.CanRedo == true));
        registry.Register(new DelegateAuthoringCommand(
            SaveProject,
            "Save Project",
            "File",
            "Ctrl+Shift+S",
            (context, _) =>
            {
                context.Workspace!.SaveProject();
                return ValueTask.CompletedTask;
            },
            context => context.Workspace?.IsProjectDirty == true));
        registry.Register(new DelegateAuthoringCommand(
            SaveAll,
            "Save All",
            "File",
            "Ctrl+Alt+S",
            (context, _) =>
            {
                context.Workspace!.SaveAll();
                return ValueTask.CompletedTask;
            },
            context => context.Workspace?.HasUnsavedChanges == true));
        return registry;
    }
}

namespace Playroma.JRPG.Editor.Shell;

public readonly record struct EditorCommandId(string Value)
{
    public override string ToString() => Value;
    public static implicit operator EditorCommandId(string value) => new(value);
}

public enum CommandHandling { NotHandled, Disabled, Enabled }
public enum CommandInvocationSource { Keyboard, Menu, Toolbar, CommandPalette, Automation }

public sealed record EditorCommandStatus(
    CommandHandling Handling,
    string DisplayName,
    string? Description = null,
    string? DisabledReason = null,
    bool IsChecked = false)
{
    public static EditorCommandStatus NotHandled(EditorCommandId id) =>
        new(CommandHandling.NotHandled, id.Value);
    public static EditorCommandStatus Enabled(string name, string? description = null, bool isChecked = false) =>
        new(CommandHandling.Enabled, name, description, null, isChecked);
    public static EditorCommandStatus Disabled(string name, string? reason = null) =>
        new(CommandHandling.Disabled, name, null, reason);
}

public sealed record EditorCommandDescriptor(
    EditorCommandId Id,
    string DisplayName,
    string? Description = null,
    string? Category = null,
    string? DefaultShortcut = null,
    string? IconKey = null);

public sealed record EditorCommandContext(
    WindowKey? ActiveWindow,
    EditorSelection Selection,
    CommandInvocationSource Source,
    CancellationToken CancellationToken);

public interface IEditorCommandTarget
{
    event EventHandler? CommandStateChanged;
    EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context);
    ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context);
}

public interface IEditorCommandTargetProvider
{
    IEnumerable<IEditorCommandTarget> GetCommandTargets(object? focusedElement);
}

public interface IEditorCommandRouter
{
    event EventHandler? RouteChanged;
    event EventHandler? CommandStatesChanged;
    IReadOnlyList<EditorCommandDescriptor> AvailableCommands { get; }
    EditorCommandStatus Query(EditorCommandId command);
    ValueTask<bool> ExecuteAsync(EditorCommandId command, CommandInvocationSource source, CancellationToken cancellationToken = default);
    void RegisterDescriptor(EditorCommandDescriptor descriptor);
    void Invalidate();
}

public static class EditorCommandIds
{
    public static readonly EditorCommandId FileOpenProject = new("File.OpenProject");
    public static readonly EditorCommandId FileCloseProject = new("File.CloseProject");
    public static readonly EditorCommandId FileSaveProject = new("File.SaveProject");
    public static readonly EditorCommandId FileExit = new("File.Exit");
    public static readonly EditorCommandId EditUndo = new("Edit.Undo");
    public static readonly EditorCommandId EditRedo = new("Edit.Redo");
    public static readonly EditorCommandId EditCut = new("Edit.Cut");
    public static readonly EditorCommandId EditCopy = new("Edit.Copy");
    public static readonly EditorCommandId EditPaste = new("Edit.Paste");
    public static readonly EditorCommandId EditDelete = new("Edit.Delete");
    public static readonly EditorCommandId EditSelectAll = new("Edit.SelectAll");
    public static readonly EditorCommandId EditRename = new("Edit.Rename");
    public static readonly EditorCommandId EditFind = new("Edit.Find");
    public static readonly EditorCommandId EditFindNext = new("Edit.FindNext");
    public static readonly EditorCommandId SessionApply = new("Session.Apply");
    public static readonly EditorCommandId SessionApplyAndClose = new("Session.ApplyAndClose");
    public static readonly EditorCommandId SessionRevert = new("Session.Revert");
    public static readonly EditorCommandId NavigateBack = new("Navigate.Back");
    public static readonly EditorCommandId NavigateForward = new("Navigate.Forward");
    public static readonly EditorCommandId NavigateCommandPalette = new("Navigate.CommandPalette");
    public static readonly EditorCommandId ViewRefresh = new("View.Refresh");
    public static readonly EditorCommandId ViewZoomIn = new("View.ZoomIn");
    public static readonly EditorCommandId ViewZoomOut = new("View.ZoomOut");
    public static readonly EditorCommandId ViewActualSize = new("View.ActualSize");
    public static readonly EditorCommandId WindowClose = new("Window.Close");
    public static readonly EditorCommandId ProjectValidate = new("Project.Validate");
    public static readonly EditorCommandId ProjectRefreshReferences = new("Project.RefreshReferences");
    public static readonly EditorCommandId ProjectShowSummary = new("Project.ShowSummary");

    public static IReadOnlyList<EditorCommandDescriptor> Defaults { get; } =
    [
        new(FileSaveProject, "Save Project", "Save all staged project changes.", "File", "Ctrl+S"),
        new(EditUndo, "Undo", Category: "Edit", DefaultShortcut: "Ctrl+Z"),
        new(EditRedo, "Redo", Category: "Edit", DefaultShortcut: "Ctrl+Y"),
        new(EditCut, "Cut", Category: "Edit", DefaultShortcut: "Ctrl+X"),
        new(EditCopy, "Copy", Category: "Edit", DefaultShortcut: "Ctrl+C"),
        new(EditPaste, "Paste", Category: "Edit", DefaultShortcut: "Ctrl+V"),
        new(EditDelete, "Delete", Category: "Edit", DefaultShortcut: "Delete"),
        new(EditSelectAll, "Select All", Category: "Edit", DefaultShortcut: "Ctrl+A"),
        new(EditFind, "Find", Category: "Edit", DefaultShortcut: "Ctrl+F"),
        new(EditFindNext, "Find Next", Category: "Edit", DefaultShortcut: "F3"),
        new(SessionApply, "Apply", Category: "Session", DefaultShortcut: "Ctrl+Enter"),
        new(SessionApplyAndClose, "Apply and Close", Category: "Session", DefaultShortcut: "Ctrl+Shift+Enter"),
        new(SessionRevert, "Revert", Category: "Session"),
        new(NavigateBack, "Back", Category: "Navigate", DefaultShortcut: "Alt+Left"),
        new(NavigateForward, "Forward", Category: "Navigate", DefaultShortcut: "Alt+Right"),
        new(NavigateCommandPalette, "Command Palette", Category: "Navigate", DefaultShortcut: "Ctrl+Shift+P"),
        new(ViewRefresh, "Refresh", Category: "View", DefaultShortcut: "F5"),
        new(WindowClose, "Close Window", Category: "Window", DefaultShortcut: "Ctrl+W"),
        new(ProjectValidate, "Validate Project", Category: "Project"),
        new(ProjectRefreshReferences, "Refresh References", Category: "Project"),
        new(ProjectShowSummary, "Project Summary", Category: "Project")
    ];
}

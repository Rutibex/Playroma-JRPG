using System.Diagnostics;
using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Application;

/// <summary>
/// Registers application and project commands once. Handlers resolve the current workspace
/// at execution time instead of capturing a form that will later be disposed.
/// </summary>
public sealed class ApplicationCommandRegistrar
{
    private readonly ShellServiceContainer _services;
    private readonly EditorApplicationController _controller;
    private readonly WinFormsOwnerProvider _ownerProvider;
    private readonly WinFormsThemeService _theme;
    private bool _registered;

    public ApplicationCommandRegistrar(
        ShellServiceContainer services,
        EditorApplicationController controller,
        WinFormsOwnerProvider ownerProvider,
        WinFormsThemeService theme)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public void Register()
    {
        if (_registered) return;
        _registered = true;
        ICommandService commands = _services.GetRequired<ICommandService>();
        IWorkspaceService workspace = _services.GetRequired<IWorkspaceService>();
        ISettingsService settings = _services.GetRequired<ISettingsService>();
        IProjectFileWatchService fileWatcher = _services.GetRequired<IProjectFileWatchService>();
        INavigationService navigation = _services.GetRequired<INavigationService>();

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.FileOpenProject,
            "Open Project…",
            async (_, token) =>
            {
                using FolderBrowserDialog dialog = new()
                {
                    Description = "Select an RPG Maker MV project folder",
                    UseDescriptionForTitle = true,
                    InitialDirectory = settings.Current.LastOpenDirectory
                };
                IWin32Window? owner = _ownerProvider.CurrentOwner;
                DialogResult result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result != DialogResult.OK) return;
                settings.Update(current => current with
                {
                    LastOpenDirectory = dialog.SelectedPath,
                    Version = EditorSettingsMigrator.CurrentVersion
                });
                await _controller.OpenProjectAsync(dialog.SelectedPath, cancellationToken: token).ConfigureAwait(true);
            },
            _ => _controller.State is not EditorApplicationState.OpeningProject
                and not EditorApplicationState.ClosingProject
                and not EditorApplicationState.Exiting,
            defaultShortcut: "Ctrl+O"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.FileCloseProject,
            "Close Project",
            async (_, token) =>
            {
                await _controller.CloseProjectAsync(
                    ProjectCloseReason.ReturnToProjectHub,
                    token).ConfigureAwait(true);
            },
            _ => workspace.CurrentSession is not null
                && _controller.State == EditorApplicationState.ProjectOpen));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.FileSave,
            "Save",
            async (_, token) =>
            {
                fileWatcher.Suppress(TimeSpan.FromSeconds(3));
                await workspace.SaveAsync(cancellationToken: token).ConfigureAwait(true);
            },
            _ => workspace.CurrentSession?.State is { CanSave: true, HasUnsavedChanges: true }
                && _controller.State == EditorApplicationState.ProjectOpen,
            defaultShortcut: "Ctrl+S"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.FileExit,
            "Exit",
            async (_, token) =>
            {
                await _controller.ExitApplicationAsync(token).ConfigureAwait(true);
            },
            _ => _controller.State != EditorApplicationState.Exiting));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.EditUndo,
            "Undo",
            async (_, token) =>
            {
                if (workspace.CurrentSession is { } session)
                {
                    await session.Undo.UndoAsync(token).ConfigureAwait(true);
                }
            },
            _ => workspace.CurrentSession?.Undo.CanUndo == true,
            defaultShortcut: "Ctrl+Z"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.EditRedo,
            "Redo",
            async (_, token) =>
            {
                if (workspace.CurrentSession is { } session)
                {
                    await session.Undo.RedoAsync(token).ConfigureAwait(true);
                }
            },
            _ => workspace.CurrentSession?.Undo.CanRedo == true,
            defaultShortcut: "Ctrl+Y"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ProjectValidate,
            "Validate Project",
            async (_, token) =>
            {
                await workspace.ValidateAsync(token).ConfigureAwait(true);
            },
            _ => workspace.CurrentSession is not null,
            defaultShortcut: "F8"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ProjectRefreshReferences,
            "Refresh References",
            async (_, token) =>
            {
                await workspace.RefreshReferencesAsync(token).ConfigureAwait(true);
            },
            _ => workspace.CurrentSession is not null));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ProjectShowSummary,
            "Project Summary",
            async (_, token) =>
            {
                if (_controller.CurrentWorkspaceHost is { } form)
                {
                    await form.OpenDocumentAsync(
                        new DocumentRequest(
                            EditorDocumentKind.ProjectSummary,
                            "summary",
                            DisplayTitle: "Project Summary"),
                        token).ConfigureAwait(true);
                }
            },
            _ => _controller.CurrentWorkspaceHost is not null));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ProjectOpenFolder,
            "Reveal Project in Explorer",
            (_, _) =>
            {
                string? root = workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot;
                if (root is not null)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true });
                }
                return ValueTask.CompletedTask;
            },
            _ => workspace.CurrentSession is not null));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ToolsOpenTextStudio,
            "Playroma Text Studio",
            async (_, token) =>
            {
                IWindowService windows = _services.GetRequired<IWindowService>();
                IEditorWindow window = await windows.OpenAsync(new WindowRequest(
                    EditorWindowKind.TextStudio,
                    "text-studio",
                    "Playroma Text Studio",
                    Scope: WindowScope.Project,
                    PlacementId: "PlayromaTextStudio"), token).ConfigureAwait(true);
                windows.Activate(window);
            },
            _ => workspace.CurrentSession is not null && _controller.State == EditorApplicationState.ProjectOpen,
            description: "Open the project-aware procedural text generation and authoring studio.",
            defaultShortcut: "Ctrl+Shift+T"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.NavigateBack,
            "Back",
            async (_, token) =>
            {
                if (_controller.CurrentWorkspaceHost is { } form)
                {
                    await form.NavigateBackAsync(token).ConfigureAwait(true);
                }
            },
            _ => navigation.CanGoBack && _controller.CurrentWorkspaceHost is not null,
            defaultShortcut: "Alt+Left"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.NavigateForward,
            "Forward",
            async (_, token) =>
            {
                if (_controller.CurrentWorkspaceHost is { } form)
                {
                    await form.NavigateForwardAsync(token).ConfigureAwait(true);
                }
            },
            _ => navigation.CanGoForward && _controller.CurrentWorkspaceHost is not null,
            defaultShortcut: "Alt+Right"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.NavigateCommandPalette,
            "Command Palette…",
            (_, _) =>
            {
                if (_controller.CurrentWorkspaceHost is { } form)
                {
                    form.ShowCommandPalette();
                }
                return ValueTask.CompletedTask;
            },
            _ => _controller.CurrentWorkspaceHost is not null,
            defaultShortcut: "Ctrl+Shift+P"));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ViewResetLayout,
            "Reset Layout",
            (_, _) =>
            {
                _controller.CurrentWorkspaceHost?.ResetLayout();
                return ValueTask.CompletedTask;
            },
            _ => _controller.CurrentWorkspaceHost is not null));

        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ViewThemeSystem,
            "System Theme",
            (_, _) =>
            {
                _theme.SetTheme(EditorTheme.System);
                return ValueTask.CompletedTask;
            }));
        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ViewThemeLight,
            "Light Theme",
            (_, _) =>
            {
                _theme.SetTheme(EditorTheme.Light);
                return ValueTask.CompletedTask;
            }));
        commands.Register(new DelegateShellCommand(
            ShellCommandIds.ViewThemeDark,
            "Dark Theme",
            (_, _) =>
            {
                _theme.SetTheme(EditorTheme.Dark);
                return ValueTask.CompletedTask;
            }));
    }
}

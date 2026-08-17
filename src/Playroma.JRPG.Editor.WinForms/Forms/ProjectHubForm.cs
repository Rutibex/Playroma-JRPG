using System.Diagnostics;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

public sealed class ProjectHubForm : Form
{
    private static readonly WindowRequest PlacementRequest = new(
        EditorWindowKind.ProjectHub,
        "project-hub",
        DisplayTitle: "Playroma jRPG",
        Scope: WindowScope.Application,
        PlacementId: "ProjectHub");

    private readonly IEditorApplicationController _controller;
    private readonly ISettingsService _settings;
    private readonly IRecentProjectService _recent;
    private readonly IRecoveryService _recovery;
    private readonly WinFormsThemeService _theme;
    private readonly WinFormsWindowPlacementService _placement;
    private readonly IWinFormsWindowService _windows;
    private readonly StartCentreControl _startCentre;
    private bool _allowClose;
    private bool _closeRequested;

    public ProjectHubForm(
        IEditorApplicationController controller,
        ISettingsService settings,
        IRecentProjectService recent,
        IRecoveryService recovery,
        WinFormsThemeService theme,
        WinFormsWindowPlacementService placement,
        IWinFormsWindowService windows)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _recent = recent ?? throw new ArgumentNullException(nameof(recent));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));

        Text = "Playroma jRPG — Project Hub";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 650);
        Size = new Size(1240, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);

        _startCentre = new StartCentreControl(_recent, _recovery);
        Controls.Add(_startCentre);
        WireEvents();
        _theme.Apply(this);
    }

    public async Task InitializeAsync(bool resetLayout, CancellationToken cancellationToken = default)
    {
        if (!resetLayout) _placement.Restore(this, PlacementRequest);
        _theme.Apply(this);
        await _startCentre.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await _startCentre.RefreshAsync(cancellationToken).ConfigureAwait(true);

    public void SetBusy(bool busy, string? message = null) =>
        _startCentre.SetBusy(busy, message);

    public void ShowHub()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    public void CloseFromController()
    {
        if (IsDisposed) return;
        _allowClose = true;
        _placement.Capture(this, PlacementRequest);
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
        if (_closeRequested) return;
        _closeRequested = true;
        _ = RequestExitAsync();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _placement.Capture(this, PlacementRequest);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnwireEvents();
        }
        base.Dispose(disposing);
    }

    private void WireEvents()
    {
        _startCentre.OpenProjectRequested += OnOpenProjectRequested;
        _startCentre.NewProjectRequested += OnNewProjectRequested;
        _startCentre.ImportProjectRequested += OnImportProjectRequested;
        _startCentre.RecentProjectRequested += OnRecentProjectRequested;
        _startCentre.RemoveRecentProjectRequested += OnRemoveRecentProjectRequested;
        _startCentre.RelocateRecentProjectRequested += OnRelocateRecentProjectRequested;
        _startCentre.RecoveryOpenRequested += OnRecoveryOpenRequested;
        _startCentre.RecoveryDiscardRequested += OnRecoveryDiscardRequested;
        _startCentre.DocumentationRequested += OnDocumentationRequested;
        _startCentre.PreferencesRequested += OnPreferencesRequested;
        _startCentre.ExitRequested += OnExitRequested;
        _recent.Changed += OnRecentProjectsChanged;
        _theme.ThemeChanged += OnThemeChanged;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    private void UnwireEvents()
    {
        _startCentre.OpenProjectRequested -= OnOpenProjectRequested;
        _startCentre.NewProjectRequested -= OnNewProjectRequested;
        _startCentre.ImportProjectRequested -= OnImportProjectRequested;
        _startCentre.RecentProjectRequested -= OnRecentProjectRequested;
        _startCentre.RemoveRecentProjectRequested -= OnRemoveRecentProjectRequested;
        _startCentre.RelocateRecentProjectRequested -= OnRelocateRecentProjectRequested;
        _startCentre.RecoveryOpenRequested -= OnRecoveryOpenRequested;
        _startCentre.RecoveryDiscardRequested -= OnRecoveryDiscardRequested;
        _startCentre.DocumentationRequested -= OnDocumentationRequested;
        _startCentre.PreferencesRequested -= OnPreferencesRequested;
        _startCentre.ExitRequested -= OnExitRequested;
        _recent.Changed -= OnRecentProjectsChanged;
        _theme.ThemeChanged -= OnThemeChanged;
        DragEnter -= OnDragEnter;
        DragDrop -= OnDragDrop;
    }

    private async void OnOpenProjectRequested(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select an RPG Maker MV project folder",
            UseDescriptionForTitle = true,
            InitialDirectory = _settings.Current.LastOpenDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _settings.Update(settings => settings with
        {
            LastOpenDirectory = dialog.SelectedPath,
            Version = EditorSettingsMigrator.CurrentVersion
        });
        await _controller.OpenProjectAsync(dialog.SelectedPath).ConfigureAwait(true);
    }

    private void OnNewProjectRequested(object? sender, EventArgs e)
    {
        MessageBox.Show(
            this,
            "The project-creation wizard is not part of the lifetime migration. This entry point is ready for the dedicated project wizard phase.",
            "New Project",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnImportProjectRequested(object? sender, EventArgs e)
    {
        MessageBox.Show(
            this,
            "Project conversion and data importing will open from this Hub entry point when the dedicated importer window is migrated.",
            "Import or Convert Project",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async void OnRecentProjectRequested(object? sender, string path) =>
        await _controller.OpenProjectAsync(path).ConfigureAwait(true);

    private async void OnRemoveRecentProjectRequested(object? sender, string path)
    {
        await _recent.RemoveAsync(path).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnRelocateRecentProjectRequested(
        object? sender,
        (string OldPath, string NewPath) paths)
    {
        await _recent.RelocateAsync(paths.OldPath, paths.NewPath).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnRecoveryOpenRequested(object? sender, RecoveryCandidate candidate) =>
        await _controller.OpenProjectAsync(
            candidate.Manifest.ProjectRoot,
            new ProjectOpenOptions(candidate, RestoreDocuments: true)).ConfigureAwait(true);

    private async void OnRecoveryDiscardRequested(object? sender, RecoveryCandidate candidate)
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Discard this recovery session? This cannot be undone.",
            "Discard Recovery",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        await _recovery.DeleteAsync(candidate).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnDocumentationRequested(object? sender, EventArgs e) => OpenDocumentation();

    private async void OnPreferencesRequested(object? sender, EventArgs e)
    {
        await _windows.ShowDialogAsync(new WindowRequest(
            EditorWindowKind.Preferences,
            "preferences",
            "Preferences",
            Presentation: WindowPresentation.Modal,
            Scope: WindowScope.Application,
            PlacementId: "Preferences"), this).ConfigureAwait(true);
    }

    private async void OnExitRequested(object? sender, EventArgs e) =>
        await _controller.ExitApplicationAsync().ConfigureAwait(true);

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(_startCentre.RefreshRecentProjects));
            return;
        }
        _startCentre.RefreshRecentProjects();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => _theme.Apply(this)));
            return;
        }
        _theme.Apply(this);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        await _controller.OpenProjectAsync(paths[0]).ConfigureAwait(true);
    }

    private async Task RequestExitAsync()
    {
        try
        {
            await _controller.ExitApplicationAsync().ConfigureAwait(true);
        }
        finally
        {
            _closeRequested = false;
        }
    }

    private void OpenDocumentation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.md");
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        MessageBox.Show(
            this,
            "Documentation is included in the source repository under docs/.",
            "Documentation",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}

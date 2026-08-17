using System.Diagnostics;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Live, project-scoped dashboard with shortcuts into the dedicated editor windows.</summary>
public sealed class ProjectSummaryForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private readonly IEditorApplicationController _application;
    private readonly IWinFormsWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripButton _maps = new("Maps") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _database = new("Database") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _resources = new("Resources") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _plugins = new("Plugins") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _validation = new("Validation") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _references = new("References") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _refresh = new("Refresh") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _copy = new("Copy Summary") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _export = new("Export…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _reveal = new("Reveal Project") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private ProjectSummaryControl? _summary;
    private bool _disposed;

    public ProjectSummaryForm(
        WindowRequest request,
        IWorkspaceService workspace,
        IEditorApplicationController application,
        IWinFormsWindowService windows,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Name = nameof(ProjectSummaryForm);
        Text = "Project Summary";
        MinimumSize = new Size(760, 540);
        Size = new Size(1040, 720);
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        BuildUi();
        WireEvents();
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        CaptureSettings();
        return ValueTask.FromResult(true);
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_workspace.CurrentSession is null) throw new InvalidOperationException("Project Summary requires an active project.");
        _summary = new ProjectSummaryControl(_workspace);
        _summary.SelectedTabIndex = _settings.Current.EffectiveProjectSummaryWindowLayout.SelectedTab;
        _summary.SelectedTabChanged += OnSelectedTabChanged;
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_summary);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        InstallContent(root);
        UpdateStatus();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _workspace.StateChanged -= OnWorkspaceStateChanged;
        if (_summary is not null)
        {
            _summary.SelectedTabChanged -= OnSelectedTabChanged;
            _summary.Dispose();
            _summary = null;
        }
        return ValueTask.CompletedTask;
    }

    private void BuildUi()
    {
        _toolbar.Items.AddRange([
            _maps, _database, _resources, _plugins, _validation, _references,
            new ToolStripSeparator(), _refresh, _copy, _export, _reveal]);
        _status.Items.Add(_statusText);
    }

    private void WireEvents()
    {
        _workspace.StateChanged += OnWorkspaceStateChanged;
        _maps.Click += (_, _) => ActivateMapEditor();
        _database.Click += async (_, _) => await OpenDatabaseAsync().ConfigureAwait(true);
        _resources.Click += async (_, _) => await OpenDocumentWindowAsync(EditorWindowKind.ResourceManager, EditorDocumentKind.AssetInventory, "resource-manager", "Resource Manager", "ResourceManager").ConfigureAwait(true);
        _plugins.Click += async (_, _) => await OpenDocumentWindowAsync(EditorWindowKind.PluginManager, EditorDocumentKind.PluginInventory, "plugin-manager", "Plugin Manager", "PluginManager").ConfigureAwait(true);
        _validation.Click += async (_, _) => await OpenDocumentWindowAsync(EditorWindowKind.Validation, EditorDocumentKind.Validation, "validation", "Validation", "Validation").ConfigureAwait(true);
        _references.Click += async (_, _) => await OpenUtilityWindowAsync(EditorWindowKind.References, "references", "Reference Explorer", "References").ConfigureAwait(true);
        _refresh.Click += (_, _) => { _summary?.Rebuild(); UpdateStatus(); };
        _copy.Click += (_, _) => CopySummary();
        _export.Click += (_, _) => ExportSummary();
        _reveal.Click += (_, _) => RevealProject();
    }

    private async Task OpenDatabaseAsync()
    {
        await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.Database,
            "database",
            "Database",
            Scope: WindowScope.Project,
            PlacementId: "Database",
            Document: new DocumentRequest(EditorDocumentKind.Database, "database", DatabaseKind: MvDocumentKind.Actors, DisplayTitle: "Database"))).ConfigureAwait(true);
    }

    private async Task OpenDocumentWindowAsync(
        EditorWindowKind windowKind,
        EditorDocumentKind documentKind,
        string identity,
        string title,
        string placement)
    {
        await _windows.OpenAsync(new WindowRequest(
            windowKind,
            identity,
            title,
            Scope: WindowScope.Project,
            PlacementId: placement,
            Document: new DocumentRequest(documentKind, identity, DisplayTitle: title))).ConfigureAwait(true);
    }

    private async Task OpenUtilityWindowAsync(
        EditorWindowKind windowKind,
        string identity,
        string title,
        string placement)
    {
        await _windows.OpenAsync(new WindowRequest(
            windowKind,
            identity,
            title,
            Scope: WindowScope.Project,
            PlacementId: placement)).ConfigureAwait(true);
    }

    private void ActivateMapEditor()
    {
        Form? form = _application.CurrentWorkspaceHost?.Form;
        if (form is null) return;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.BringToFront();
        form.Activate();
    }

    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceStateChanged(sender, e))) return;
        UpdateStatus();
    }

    private void OnSelectedTabChanged(object? sender, EventArgs e) => CaptureSettings();

    private void UpdateStatus()
    {
        var session = _workspace.CurrentSession;
        if (session is null)
        {
            _statusText.Text = "No project is open.";
            return;
        }
        _statusText.Text = $"{session.Workspace.Snapshot.Maps.Count:N0} maps · "
            + $"{session.Workspace.Snapshot.Assets.Assets.Count:N0} assets · "
            + $"{session.Workspace.Snapshot.Plugins.Entries.Count:N0} plugins · "
            + (session.State.HasUnsavedChanges ? "unsaved changes" : "saved state");
        Text = string.IsNullOrWhiteSpace(session.Workspace.Snapshot.Identity.ProjectTitle)
            ? "Project Summary"
            : $"Project Summary — {session.Workspace.Snapshot.Identity.ProjectTitle}";
    }

    private void CopySummary()
    {
        if (_summary is null) return;
        Clipboard.SetText(_summary.BuildTextSummary());
        _notifications.Success("Summary copied", "The project summary was copied to the clipboard.");
    }

    private void ExportSummary()
    {
        if (_summary is null) return;
        using SaveFileDialog dialog = new()
        {
            Title = "Export Project Summary",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = "project-summary.txt",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _summary.BuildTextSummary());
        _notifications.Success("Summary exported", dialog.FileName);
    }

    private void RevealProject()
    {
        string? root = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
    }

    private void CaptureSettings()
    {
        if (_disposed) return;
        int tab = _summary?.SelectedTabIndex ?? 0;
        _settings.Update(current => current with
        {
            ProjectSummaryWindowLayout = new ProjectSummaryWindowLayoutSettings(tab),
            Version = EditorSettingsMigrator.CurrentVersion
        });
    }
}

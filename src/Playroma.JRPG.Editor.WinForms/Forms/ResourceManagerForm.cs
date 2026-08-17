using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Visual;
using Playroma.JRPG.Editor.WinForms.ResourcesUI;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// One project-scoped Resource Manager window. It owns browser state and async preview lifetimes,
/// while all mutations continue through the shared project transaction, undo and save services.
/// </summary>
public sealed class ResourceManagerForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private readonly IWinFormsWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly WinFormsThemeService _theme;
    private readonly ResourceManagerSelectionContext _selectionContext;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _assetStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _projectStatus = new();
    private readonly Panel _bottom = new() { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(8) };
    private readonly Button _undo = new() { Text = "&Undo", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _redo = new() { Text = "&Redo", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _save = new() { Text = "&Save Project", AutoSize = true, MinimumSize = new Size(105, 30) };
    private readonly Button _close = new() { Text = "&Close", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly ToolStripButton _importFilesButton = new("Import Files…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _importFolderButton = new("Import Folder…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _replaceButton = new("Replace") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _renameButton = new("Rename") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _deleteButton = new("Delete") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _exportButton = new("Export…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _spriteButton = new("Edit Sprite") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _refreshButton = new("Refresh") { DisplayStyle = ToolStripItemDisplayStyle.Text };

    private MvProjectSession? _session;
    private AssetInventoryDocumentControl? _browser;
    private bool _disposed;

    public ResourceManagerForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISelectionService selection,
        IWinFormsWindowService windows,
        ISettingsService settings,
        INotificationService notifications,
        WinFormsThemeService theme)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selectionContext = new ResourceManagerSelectionContext(selection ?? throw new ArgumentNullException(nameof(selection)));

        Name = nameof(ResourceManagerForm);
        Text = "Resource Manager";
        MinimumSize = new Size(900, 620);
        Size = new Size(1220, 800);
        ShowInTaskbar = true;
        KeyPreview = true;

        BuildMenu();
        MainMenuStrip = _menu;
        BuildToolbar();
        ApplyToolbarImages();
        BuildBottomAndStatus();
        WireEvents();
        _theme.ThemeChanged += OnThemeChanged;
        DpiChanged += (_, _) => ApplyToolbarImages();
        Shown += (_, _) => AccessibilityMetadata.ApplyFormDefaults(this);
    }

    public override string Title
    {
        get
        {
            string? title = _session?.Workspace.Snapshot.Identity.ProjectTitle;
            string project = !string.IsNullOrWhiteSpace(title)
                ? title
                : _session?.Workspace.Snapshot.Identity.ProjectRoot is { } root
                    ? Path.GetFileName(root)
                    : string.Empty;
            string dirty = IsDirty ? " *" : string.Empty;
            return string.IsNullOrWhiteSpace(project)
                ? "Resource Manager" + dirty
                : $"Resource Manager — {project}{dirty}";
        }
    }

    public override bool IsDirty => _session?.State.HasUnsavedChanges == true;

    public override NavigationLocation? CaptureLocation() =>
        _browser?.CaptureNavigationTarget().ToLocation() ?? base.CaptureLocation();

    public override async ValueTask RestoreLocationAsync(
        NavigationLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (_browser is null) return;
        await _browser.NavigateAsync(ResourceNavigationTarget.FromLocation(location), cancellationToken).ConfigureAwait(true);
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        _session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("Resource Manager cannot open without an active project.");
        _browser = new AssetInventoryDocumentControl(_workspace, _selectionContext, _theme);
        _browser.CommandStateChanged += OnBrowserStateChanged;
        _browser.ViewStateChanged += OnBrowserStateChanged;
        _session.StateChanged += OnSessionStateChanged;
        _session.Undo.HistoryChanged += OnUndoHistoryChanged;

        ResourceManagerWindowLayoutSettings saved = _settings.Current.EffectiveResourceManagerLayout;
        _browser.RestoreViewState(saved);
        InstallContent(BuildRoot(_browser));

        ResourceNavigationTarget initial = Request.Location is not null
            ? ResourceNavigationTarget.FromLocation(Request.Location)
            : Request.Document is not null
                ? ResourceNavigationTarget.FromLocation(new NavigationLocation(Request.Document))
                : new ResourceNavigationTarget(SmartCollection: ResourceSmartCollection.All);
        await _browser.NavigateAsync(initial, cancellationToken).ConfigureAwait(true);
        UpdateWindowState();
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
            _session.Undo.HistoryChanged -= OnUndoHistoryChanged;
        }
        if (_browser is not null)
        {
            _browser.CommandStateChanged -= OnBrowserStateChanged;
            _browser.ViewStateChanged -= OnBrowserStateChanged;
            await _browser.DisposeAsync().ConfigureAwait(true);
            _browser = null;
        }
        _theme.ThemeChanged -= OnThemeChanged;
        _selectionContext.Clear();
        _lifetimeCancellation.Dispose();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _selectionContext.Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        _selectionContext.Suspend();
        base.OnDeactivate(e);
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo)
            return _session?.Undo.CanUndo == true ? EditorCommandStatus.Enabled("Undo Resource Change") : EditorCommandStatus.Disabled("Undo Resource Change");
        if (command == EditorCommandIds.EditRedo)
            return _session?.Undo.CanRedo == true ? EditorCommandStatus.Enabled("Redo Resource Change") : EditorCommandStatus.Disabled("Redo Resource Change");
        if (command == EditorCommandIds.EditFind) return _browser is not null ? EditorCommandStatus.Enabled("Find Resource") : EditorCommandStatus.Disabled("Find Resource");
        if (command == EditorCommandIds.EditDelete)
            return _browser?.CanDelete == true ? EditorCommandStatus.Enabled("Delete Resource") : EditorCommandStatus.Disabled("Delete Resource", "Select a deletable resource.");
        if (command == EditorCommandIds.ViewRefresh) return _browser is not null ? EditorCommandStatus.Enabled("Refresh Resources") : EditorCommandStatus.Disabled("Refresh Resources");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo) await UndoSafelyAsync().ConfigureAwait(true);
        else if (command == EditorCommandIds.EditRedo) await RedoSafelyAsync().ConfigureAwait(true);
        else if (command == EditorCommandIds.EditFind) _browser?.FocusSearch();
        else if (command == EditorCommandIds.EditDelete && _browser is not null) await _browser.DeleteSelectedAsync().ConfigureAwait(true);
        else if (command == EditorCommandIds.ViewRefresh && _browser is not null) await _browser.RefreshCatalogueAsync().ConfigureAwait(true);
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { _ = SaveProjectSafelyAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { _ = UndoSafelyAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { _ = RedoSafelyAsync(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _browser?.FocusSearch(); return true; }
        if (keyData == (Keys.Control | Keys.I)) { _ = _browser?.ImportFilesAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.I)) { _ = _browser?.ImportFolderAsync(); return true; }
        if (keyData == Keys.F2 && _browser?.CanRename == true) { _ = _browser.RenameSelectedAsync(); return true; }
        if (keyData == Keys.Delete && _browser?.CanDelete == true) { _ = _browser.DeleteSelectedAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.E) && _browser?.CanExport == true) { _ = _browser.ExportSelectedAsync(); return true; }
        if (keyData == Keys.F5) { _ = _browser?.RefreshCatalogueAsync(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control BuildRoot(Control browser)
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(browser);
        root.Controls.Add(_status);
        root.Controls.Add(_bottom);
        root.Controls.Add(_toolbar);
        root.Controls.Add(_menu);
        return root;
    }

    private void BuildMenu()
    {
        ToolStripMenuItem file = new("&File");
        ToolStripMenuItem importFiles = new("Import &Files…") { ShortcutKeys = Keys.Control | Keys.I };
        ToolStripMenuItem importFolder = new("Import &Folder…") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.I };
        ToolStripMenuItem save = new("&Save Project") { ShortcutKeys = Keys.Control | Keys.S };
        ToolStripMenuItem close = new("&Close Resource Manager") { ShortcutKeys = Keys.Control | Keys.W };
        importFiles.Click += async (_, _) => { if (_browser is not null) await _browser.ImportFilesAsync().ConfigureAwait(true); };
        importFolder.Click += async (_, _) => { if (_browser is not null) await _browser.ImportFolderAsync().ConfigureAwait(true); };
        save.Click += async (_, _) => await SaveProjectSafelyAsync().ConfigureAwait(true);
        close.Click += (_, _) => Close();
        file.DropDownItems.AddRange([importFiles, importFolder, new ToolStripSeparator(), save, new ToolStripSeparator(), close]);

        ToolStripMenuItem edit = new("&Edit");
        ToolStripMenuItem undo = new("&Undo") { ShortcutKeys = Keys.Control | Keys.Z };
        ToolStripMenuItem redo = new("&Redo") { ShortcutKeys = Keys.Control | Keys.Y };
        ToolStripMenuItem rename = new("&Rename") { ShortcutKeys = Keys.F2 };
        ToolStripMenuItem delete = new("&Delete") { ShortcutKeys = Keys.Delete };
        undo.Click += async (_, _) => await UndoSafelyAsync().ConfigureAwait(true);
        redo.Click += async (_, _) => await RedoSafelyAsync().ConfigureAwait(true);
        rename.Click += async (_, _) => { if (_browser is not null) await _browser.RenameSelectedAsync().ConfigureAwait(true); };
        delete.Click += async (_, _) => { if (_browser is not null) await _browser.DeleteSelectedAsync().ConfigureAwait(true); };
        edit.DropDownItems.AddRange([undo, redo, new ToolStripSeparator(), rename, delete]);

        ToolStripMenuItem view = new("&View");
        ToolStripMenuItem find = new("&Find") { ShortcutKeys = Keys.Control | Keys.F };
        ToolStripMenuItem refresh = new("&Refresh Catalogue") { ShortcutKeys = Keys.F5 };
        find.Click += (_, _) => _browser?.FocusSearch();
        refresh.Click += async (_, _) => { if (_browser is not null) await _browser.RefreshCatalogueAsync().ConfigureAwait(true); };
        view.DropDownItems.AddRange([find, refresh]);

        ToolStripMenuItem asset = new("&Asset");
        ToolStripMenuItem replace = new("&Replace Variant…");
        ToolStripMenuItem export = new("&Export Copy…") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.E };
        ToolStripMenuItem editSprite = new("Edit in Playroma &Sprite Studio…");
        ToolStripMenuItem open = new("Open &Externally");
        ToolStripMenuItem reveal = new("Reveal in &Explorer");
        replace.Click += async (_, _) => { if (_browser is not null) await _browser.ReplaceSelectedAsync().ConfigureAwait(true); };
        export.Click += async (_, _) => { if (_browser is not null) await _browser.ExportSelectedAsync().ConfigureAwait(true); };
        editSprite.Click += async (_, _) => await OpenSelectedInSpriteStudioAsync().ConfigureAwait(true);
        open.Click += (_, _) => _browser?.OpenSelectedExternally();
        reveal.Click += (_, _) => _browser?.RevealSelected();
        asset.DropDownItems.AddRange([replace, export, new ToolStripSeparator(), editSprite, open, reveal]);

        _menu.Items.AddRange([file, edit, view, asset]);
    }

    private void BuildToolbar()
    {
        _importFilesButton.Click += async (_, _) => { if (_browser is not null) await _browser.ImportFilesAsync().ConfigureAwait(true); };
        _importFolderButton.Click += async (_, _) => { if (_browser is not null) await _browser.ImportFolderAsync().ConfigureAwait(true); };
        _replaceButton.Click += async (_, _) => { if (_browser is not null) await _browser.ReplaceSelectedAsync().ConfigureAwait(true); };
        _renameButton.Click += async (_, _) => { if (_browser is not null) await _browser.RenameSelectedAsync().ConfigureAwait(true); };
        _deleteButton.Click += async (_, _) => { if (_browser is not null) await _browser.DeleteSelectedAsync().ConfigureAwait(true); };
        _exportButton.Click += async (_, _) => { if (_browser is not null) await _browser.ExportSelectedAsync().ConfigureAwait(true); };
        _spriteButton.Click += async (_, _) => await OpenSelectedInSpriteStudioAsync().ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => { if (_browser is not null) await _browser.RefreshCatalogueAsync().ConfigureAwait(true); };
        _toolbar.Items.AddRange([
            _importFilesButton,
            _importFolderButton,
            new ToolStripSeparator(),
            _replaceButton,
            _renameButton,
            _deleteButton,
            _exportButton,
            _spriteButton,
            new ToolStripSeparator(),
            _refreshButton]);
    }

    private void ApplyToolbarImages()
    {
        ConfigureToolbarButton(_importFilesButton, EditorIconKind.Add, "Import files", "Import one or more resource files. Control I.");
        ConfigureToolbarButton(_importFolderButton, EditorIconKind.Folder, "Import folder", "Import supported resources from a folder. Control Shift I.");
        ConfigureToolbarButton(_replaceButton, EditorIconKind.Refresh, "Replace resource", "Replace the selected resource variant.");
        ConfigureToolbarButton(_renameButton, EditorIconKind.Properties, "Rename resource", "Rename the selected resource. F2.");
        ConfigureToolbarButton(_deleteButton, EditorIconKind.Delete, "Delete resource", "Delete the selected resource. Delete key.");
        ConfigureToolbarButton(_exportButton, EditorIconKind.Save, "Export resource", "Export a copy of the selected resource.");
        ConfigureToolbarButton(_spriteButton, EditorIconKind.Sprite, "Edit in Sprite Studio", "Open the selected PNG in Playroma Sprite Studio.");
        ConfigureToolbarButton(_refreshButton, EditorIconKind.Refresh, "Refresh resources", "Refresh the resource catalogue. F5.");
    }

    private void ConfigureToolbarButton(
        ToolStripButton button,
        EditorIconKind icon,
        string accessibleName,
        string toolTip)
    {
        button.Image = _theme.Icon(icon, this);
        button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        button.ImageTransparentColor = Color.Magenta;
        button.AccessibleName = accessibleName;
        button.ToolTipText = toolTip;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnThemeChanged(sender, e)));
            return;
        }
        ApplyToolbarImages();
    }

    private void BuildBottomAndStatus()
    {
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.AddRange([_undo, _redo, _save, _close]);
        _bottom.Controls.Add(buttons);
        _assetStatus.AccessibleName = "Selected resource status";
        _projectStatus.AccessibleName = "Project status";
        _status.AccessibleName = "Resource Manager status";
        _status.Items.AddRange([_assetStatus, _projectStatus]);
    }

    private void WireEvents()
    {
        _undo.Click += async (_, _) => await UndoSafelyAsync().ConfigureAwait(true);
        _redo.Click += async (_, _) => await RedoSafelyAsync().ConfigureAwait(true);
        _save.Click += async (_, _) => await SaveProjectSafelyAsync().ConfigureAwait(true);
        _close.Click += (_, _) => Close();
    }

    private bool CanEditSelectedInSpriteStudio =>
        _browser?.CurrentVariant is { IsEncrypted: false } variant &&
        Path.GetExtension(variant.RelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase);

    private async Task OpenSelectedInSpriteStudioAsync()
    {
        AssetVariant? variant = _browser?.CurrentVariant;
        if (variant is null || variant.IsEncrypted || !Path.GetExtension(variant.RelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) return;
        string relativePath = variant.NormalizedRelativePath;
        DocumentRequest document = new(
            EditorDocumentKind.ProjectFile,
            Identity: relativePath,
            RelativePath: relativePath,
            DisplayTitle: Path.GetFileName(relativePath));
        await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.SpriteStudio,
            Identity: "sprite-studio:" + relativePath,
            DisplayTitle: "Playroma Sprite Studio — " + Path.GetFileName(relativePath),
            ToolTip: relativePath,
            Document: document,
            Location: new NavigationLocation(document))).ConfigureAwait(true);
    }

    private async Task SaveProjectSafelyAsync()
    {
        try
        {
            ProjectSaveResult result = await _workspace.SaveAsync(cancellationToken: _lifetimeCancellation.Token).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                _notifications.Warning("Save not completed", "The project save was blocked. Review validation and output for details.");
            }
            UpdateWindowState();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Error("Could not save project", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Could not save project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task UndoSafelyAsync()
    {
        try
        {
            if (_session?.Undo.CanUndo == true)
            {
                await _session.Undo.UndoAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                await _session.RefreshAssetCatalogueAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _notifications.Error("Undo failed", exception.Message, exception); }
        UpdateWindowState();
    }

    private async Task RedoSafelyAsync()
    {
        try
        {
            if (_session?.Undo.CanRedo == true)
            {
                await _session.Undo.RedoAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                await _session.RefreshAssetCatalogueAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _notifications.Error("Redo failed", exception.Message, exception); }
        UpdateWindowState();
    }

    private void CaptureSettings()
    {
        if (_browser is null) return;
        ResourceManagerViewState state = _browser.CaptureViewState();
        ResourceManagerWindowLayoutSettings layout = new(
            SelectedCategory: state.SelectedCategory,
            SearchText: state.SearchText,
            ViewMode: state.ViewMode,
            SortColumn: state.SortColumn,
            SortDescending: state.SortDescending,
            SelectedAsset: state.SelectedAsset,
            SelectedPreviewTab: state.SelectedPreviewTab,
            CategoryPanelWidth: state.CategoryPanelWidth,
            PreviewPanelWidth: state.PreviewPanelWidth,
            ThumbnailSize: state.ThumbnailSize,
            ExpandedGroups: state.ExpandedGroups,
            Version: 1);
        _settings.Update(current => current with { ResourceManagerLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private void UpdateWindowState()
    {
        if (_session is null) return;
        Text = Title;
        _undo.Enabled = _session.Undo.CanUndo;
        _redo.Enabled = _session.Undo.CanRedo;
        _save.Enabled = _session.State.CanSave && _session.State.HasUnsavedChanges;
        _undo.Text = _session.Undo.CanUndo && !string.IsNullOrWhiteSpace(_session.Undo.UndoDescription)
            ? "Undo " + _session.Undo.UndoDescription
            : "Undo";
        _redo.Text = _session.Undo.CanRedo && !string.IsNullOrWhiteSpace(_session.Undo.RedoDescription)
            ? "Redo " + _session.Undo.RedoDescription
            : "Redo";
        _projectStatus.Text = _session.State.IsReadOnly
            ? "Read-only project"
            : _session.State.HasUnsavedChanges
                ? "Project modified"
                : "Saved";
        _assetStatus.Text = _browser?.CurrentRecord is { } record
            ? $"{record.Key} · {record.Status} · {record.Variants.Count:N0} variant(s)"
            : "Select an asset";
        _importFilesButton.Enabled = _browser?.CanImport == true;
        _importFolderButton.Enabled = _browser?.CanImport == true;
        _replaceButton.Enabled = _browser?.CanReplace == true;
        _renameButton.Enabled = _browser?.CanRename == true;
        _deleteButton.Enabled = _browser?.CanDelete == true;
        _exportButton.Enabled = _browser?.CanExport == true;
        _spriteButton.Enabled = CanEditSelectedInSpriteStudio;
        NotifyStateChanged();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (InvokeRequired) BeginInvoke(new Action(UpdateWindowState)); else UpdateWindowState();
    }

    private void OnUndoHistoryChanged(object? sender, EventArgs e) => OnSessionStateChanged(sender, e);
    private void OnBrowserStateChanged(object? sender, EventArgs e)
    {
        CaptureSettings();
        UpdateWindowState();
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Plugins;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV.Documents;
using Playroma.JRPG.Workspaces;

using ShellEditorCommandContext = Playroma.JRPG.Editor.Shell.EditorCommandContext;
namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// RPG Maker MV compatible plugin manifest editor. JavaScript is inspected only as text;
/// plugin code is never evaluated inside the editor process.
/// </summary>
public sealed class PluginManagerForm : EditorToolForm, IShutdownParticipant
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _selection;
    private readonly IWinFormsWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PluginEditSession _edit = new();
    private readonly YanflyPluginManagerIntelligenceService _intelligenceService = new();

    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripButton _addExisting = new("Add Existing…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _import = new("Import Script…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _remove = new("Remove") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _up = new("Move Up") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _down = new("Move Down") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _enable = new("Enable") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _disable = new("Disable") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _localUndo = new("Undo Edit") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _localRedo = new("Redo Edit") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripTextBox _search = new() { AutoSize = false, Width = 220 };
    private readonly ToolStripButton _showDisabled = new("Show Disabled") { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text };

    private readonly SplitContainer _mainSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Vertical, 430, 300, 430, splitterWidth: 6);
    private readonly DataGridView _plugins = ControlHelpers.CreateReadOnlyGrid();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Top, ReadOnly = true };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _scriptStatus = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
    private readonly Label _author = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 2, 0, 4) };
    private readonly TextBox _help = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = true
    };
    private readonly DataGridView _parameters = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };
    private readonly DataGridView _intelligenceGrid = ControlHelpers.CreateReadOnlyGrid();
    private readonly TextBox _intelligenceSummary = new()
    {
        Dock = DockStyle.Top,
        Height = 92,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true
    };
    private readonly Label _intelligenceStatus = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(0, 4, 0, 6),
        Text = "YEP status: analyzing…"
    };
    private readonly Button _fixYanflyOrder = new()
    {
        Text = "Apply Suggested YEP Order",
        AutoSize = true,
        MinimumSize = new Size(180, 30)
    };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel _buttons = new() { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
    private readonly Button _apply = new() { Text = "Apply", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _revert = new() { Text = "Revert", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _save = new() { Text = "Save Project", AutoSize = true, MinimumSize = new Size(105, 30) };
    private readonly Button _close = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 30) };

    private MvProjectSession? _session;
    private IProjectDocumentLease? _lease;
    private MvPluginManifestDocument? _manifest;
    private string _manifestPath = string.Empty;
    private IReadOnlyList<int> _visibleIndexes = [];
    private PluginMetadata _metadata = PluginMetadata.Empty(string.Empty);
    private bool _rebuilding;
    private bool _sharedConflict;
    private bool _disposed;
    private InstalledPluginAnalysis? _installedPluginAnalysis;
    private CancellationTokenSource? _intelligenceRefresh;
    private int _intelligenceRevision;

    public PluginManagerForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISelectionService selection,
        IWinFormsWindowService windows,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Name = nameof(PluginManagerForm);
        Text = "Plugin Manager";
        MinimumSize = new Size(900, 620);
        Size = new Size(1220, 800);
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        KeyPreview = true;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        BuildUi();
        WireEvents();
    }

    public override bool IsDirty => _edit.IsDirty || _session?.State.HasUnsavedChanges == true;

    public ValueTask<ShutdownContribution?> PrepareShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_edit.IsDirty) return ValueTask.FromResult<ShutdownContribution?>(null);
        ShutdownContribution contribution = new(
            Key,
            Title,
            $"{_edit.Entries.Count} plugin manifest entries contain unapplied changes.",
            CanApply: !_sharedConflict,
            CanDiscard: true,
            CanRecover: true);
        return ValueTask.FromResult<ShutdownContribution?>(contribution);
    }

    public async ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default)
    {
        if (!_edit.IsDirty) return true;
        switch (action)
        {
            case ShutdownLocalAction.Apply:
                if (_sharedConflict || _manifest is null || _session is null) return false;
                try
                {
                    byte[] bytes = _edit.Capture();
                    await using ProjectTransaction transaction = _session.BeginTransaction("Edit plugin manifest");
                    transaction.Add(new ReplaceDocumentContentCommand(_manifestPath, bytes, "Edit plugin manifest"));
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(true);
                    _edit.MarkApplied();
                    _sharedConflict = false;
                    Rebuild(SelectedEntry()?.Name);
                    RefreshWindowState();
                    return true;
                }
                catch (Exception exception)
                {
                    _notifications.Error("Could not apply plugin changes", exception.Message, exception);
                    return false;
                }
            case ShutdownLocalAction.Discard:
                _edit.RestoreBaseline();
                _sharedConflict = false;
                Rebuild();
                RefreshWindowState();
                return true;
            case ShutdownLocalAction.PreserveRecovery:
                return true;
            default:
                return false;
        }
    }

    public ValueTask<PrivateSessionRecoveryState?> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_edit.IsDirty || _manifest is null)
        {
            return ValueTask.FromResult<PrivateSessionRecoveryState?>(null);
        }
        PluginManagerRecoveryPayload payload = new(
            Convert.ToBase64String(_edit.Capture()),
            SelectedEntry()?.Name,
            Version: 1);
        string baseline = Convert.ToHexString(SHA256.HashData(_manifest.CaptureGeneratedBytes())).ToLowerInvariant();
        PrivateSessionRecoveryState state = new(
            "plugin-manager",
            Request with { Location = CaptureLocation() },
            Title,
            JsonSerializer.Serialize(payload),
            baseline,
            DateTimeOffset.UtcNow,
            Version: 1);
        return ValueTask.FromResult<PrivateSessionRecoveryState?>(state);
    }

    public ValueTask RestoreRecoveryStateAsync(
        PrivateSessionRecoveryState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (_manifest is null || !string.Equals(state.SessionType, "plugin-manager", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }
        PluginManagerRecoveryPayload? payload = JsonSerializer.Deserialize<PluginManagerRecoveryPayload>(state.DataJson);
        if (payload is null || payload.Version > 1) return ValueTask.CompletedTask;
        _edit.RestoreSnapshot(Convert.FromBase64String(payload.SnapshotBase64), clearHistory: true);
        string current = Convert.ToHexString(SHA256.HashData(_manifest.CaptureGeneratedBytes())).ToLowerInvariant();
        _sharedConflict = !string.IsNullOrWhiteSpace(state.BaselineFingerprint)
            && !string.Equals(current, state.BaselineFingerprint, StringComparison.OrdinalIgnoreCase);
        Rebuild(payload.SelectedPluginName);
        RefreshWindowState();
        return ValueTask.CompletedTask;
    }

    public override NavigationLocation? CaptureLocation()
    {
        PluginEditEntry? selected = SelectedEntry();
        DocumentRequest document = Request.Document ?? new DocumentRequest(
            EditorDocumentKind.PluginInventory,
            "plugins",
            RelativePath: _manifestPath,
            DisplayTitle: "Plugin Manager");
        return new NavigationLocation(
            document,
            SelectionPath: selected?.Name,
            RecordId: selected is null ? null : _edit.Entries.IndexOf(selected));
    }

    public override async ValueTask RestoreLocationAsync(
        NavigationLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        string? name = location.SelectionPath ?? location.Document.EventSurface;
        int? index = location.RecordId ?? location.Document.RecordId;
        if (!string.IsNullOrWhiteSpace(name)) SelectPlugin(name);
        else if (index.HasValue) SelectPlugin(index.Value);
        await ValueTask.CompletedTask;
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_edit.IsDirty)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "The Plugin Manager contains unapplied changes.\n\nApply them before closing?",
                "Unapplied Plugin Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes && !await ApplyAsync(cancellationToken).ConfigureAwait(true)) return false;
        }
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        _session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("Plugin Manager cannot open without an active project.");
        _manifestPath = _session.Workspace.Snapshot.Plugins.ManifestRelativePath
            ?? throw new FileNotFoundException("This project does not contain a recognized js/plugins.js manifest.");
        _lease = await _session.Workspace.AcquireDocumentAsync(_manifestPath, cancellationToken).ConfigureAwait(true);
        _manifest = _lease.Document as MvPluginManifestDocument
            ?? throw new InvalidDataException($"'{_manifestPath}' is not a mutable RPG Maker MV plugin manifest.");
        _edit.Load(_manifest);
        _session.DocumentChanged += OnDocumentChanged;
        _session.StateChanged += OnSessionStateChanged;
        RestoreSettings();
        InstallContent(BuildRoot());
        Rebuild();
        await RestoreInitialSelectionAsync(cancellationToken).ConfigureAwait(true);
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null)
        {
            _session.DocumentChanged -= OnDocumentChanged;
            _session.StateChanged -= OnSessionStateChanged;
        }
        _intelligenceRefresh?.Cancel();
        _intelligenceRefresh?.Dispose();
        _intelligenceRefresh = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_lease is not null) await _lease.DisposeAsync().ConfigureAwait(true);
        _lease = null;
        _manifest = null;
        _session = null;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, ShellEditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo)
            return _edit.CanUndo ? EditorCommandStatus.Enabled("Undo Plugin Edit") : EditorCommandStatus.Disabled("Undo Plugin Edit");
        if (command == EditorCommandIds.EditRedo)
            return _edit.CanRedo ? EditorCommandStatus.Enabled("Redo Plugin Edit") : EditorCommandStatus.Disabled("Redo Plugin Edit");
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Plugin");
        if (command == EditorCommandIds.EditDelete)
            return _plugins.Focused && SelectedEntry() is not null ? EditorCommandStatus.Enabled("Remove Plugin") : EditorCommandStatus.Disabled("Remove Plugin", "Select a plugin in the plugin list.");
        if (command == EditorCommandIds.SessionApply)
            return _edit.IsDirty ? EditorCommandStatus.Enabled("Apply Plugin Changes") : EditorCommandStatus.Disabled("Apply Plugin Changes");
        if (command == EditorCommandIds.SessionRevert)
            return _edit.IsDirty ? EditorCommandStatus.Enabled("Revert Plugin Changes") : EditorCommandStatus.Disabled("Revert Plugin Changes");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, ShellEditorCommandContext context)
    {
        if (command == EditorCommandIds.EditUndo) LocalUndo();
        else if (command == EditorCommandIds.EditRedo) LocalRedo();
        else if (command == EditorCommandIds.EditFind) _search.Focus();
        else if (command == EditorCommandIds.EditDelete) RemoveSelected();
        else if (command == EditorCommandIds.SessionApply) await ApplyAsync(context.CancellationToken).ConfigureAwait(true);
        else if (command == EditorCommandIds.SessionRevert) RevertLocal();
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { LocalUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { LocalRedo(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { _ = SaveProjectAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Enter)) { _ = ApplyAsync(_lifetime.Token); return true; }
        if (keyData == Keys.Delete && _plugins.Focused) { RemoveSelected(); return true; }
        if (keyData == (Keys.Alt | Keys.Up)) { MoveSelected(-1); return true; }
        if (keyData == (Keys.Alt | Keys.Down)) { MoveSelected(1); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        BuildMenu();
        _search.TextBox.PlaceholderText = "Search plugins…";
        _toolbar.Items.AddRange([
            _addExisting, _import, _remove, new ToolStripSeparator(),
            _up, _down, _enable, _disable, new ToolStripSeparator(),
            _localUndo, _localRedo, new ToolStripSeparator(),
            new ToolStripLabel("Search:"), _search, _showDisabled]);

        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "Order", Width = 58 });
        _plugins.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", Width = 45 });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Plugin", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Version", Width = 72 });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Compatibility", HeaderText = "YEP Status", Width = 125 });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Script", HeaderText = "Script", Width = 74 });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Parameters", HeaderText = "Params", Width = 65 });
        _plugins.Columns.Add(new DataGridViewTextBoxColumn { Name = "Warnings", HeaderText = "Issues", Width = 62 });
        _mainSplit.Panel1.Controls.Add(_plugins);

        Panel details = new() { Dock = DockStyle.Fill, Padding = new Padding(10) };
        Label nameLabel = new() { Text = "Name", Dock = DockStyle.Top, AutoSize = true };
        Label descLabel = new() { Text = "Description", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 8, 0, 2) };
        Panel descHost = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
        descHost.Controls.Add(_description);
        details.Controls.Add(descHost);
        details.Controls.Add(descLabel);
        details.Controls.Add(_scriptStatus);
        details.Controls.Add(_intelligenceStatus);
        details.Controls.Add(_author);
        details.Controls.Add(_name);
        details.Controls.Add(nameLabel);
        _tabs.TabPages.Add(new TabPage("Details") { Controls = { details } });

        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Parameter", HeaderText = "Parameter", ReadOnly = true, Width = 170 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", ReadOnly = true, Width = 115 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Default", HeaderText = "Default", ReadOnly = true, Width = 130 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description", ReadOnly = true, Width = 220 });
        ContextMenuStrip parameterMenu = new();
        parameterMenu.Items.Add("Edit Value…", null, (_, _) => EditParameterValue(_parameters.CurrentRow?.Index ?? -1));
        parameterMenu.Items.Add("Reset to Default", null, (_, _) => ResetParameterToDefault(_parameters.CurrentRow?.Index ?? -1));
        _parameters.ContextMenuStrip = parameterMenu;
        _tabs.TabPages.Add(new TabPage("Parameters") { Controls = { _parameters } });
        _tabs.TabPages.Add(new TabPage("Help") { Controls = { _help } });

        _intelligenceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", Width = 82 });
        _intelligenceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Code", Width = 72 });
        _intelligenceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plugin", HeaderText = "Plugin", Width = 175 });
        _intelligenceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Message", HeaderText = "Diagnostic", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        Panel intelligenceButtons = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8) };
        intelligenceButtons.Controls.Add(_fixYanflyOrder);
        Panel intelligence = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        intelligence.Controls.Add(_intelligenceGrid);
        intelligence.Controls.Add(intelligenceButtons);
        intelligence.Controls.Add(_intelligenceSummary);
        _tabs.TabPages.Add(new TabPage("YEP Diagnostics") { Controls = { intelligence } });
        _mainSplit.Panel2.Controls.Add(_tabs);

        FlowLayoutPanel buttonFlow = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttonFlow.Controls.AddRange([_apply, _revert, _save, _close]);
        _buttons.Controls.Add(buttonFlow);
        _status.Items.Add(_statusText);
    }

    private void BuildMenu()
    {
        ToolStripMenuItem file = new("&File");
        file.DropDownItems.Add("&Add Existing Script…", null, async (_, _) => await AddExistingScriptsAsync().ConfigureAwait(true));
        file.DropDownItems.Add("&Import Plugin Script…", null, async (_, _) => await ImportScriptAsync().ConfigureAwait(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Apply", null, async (_, _) => await ApplyAsync(_lifetime.Token).ConfigureAwait(true));
        file.DropDownItems.Add("Save &Project", null, async (_, _) => await SaveProjectAsync().ConfigureAwait(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Close", null, (_, _) => Close());
        ToolStripMenuItem edit = new("&Edit");
        edit.DropDownItems.Add("Undo Plugin Edit", null, (_, _) => LocalUndo());
        edit.DropDownItems.Add("Redo Plugin Edit", null, (_, _) => LocalRedo());
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Remove Manifest Entry", null, (_, _) => RemoveSelected());
        ToolStripMenuItem tools = new("&Tools");
        tools.DropDownItems.Add("Open Script", null, (_, _) => OpenScript());
        tools.DropDownItems.Add("Reveal Script in Explorer", null, (_, _) => RevealScript());
        tools.DropDownItems.Add("Open Manifest as Raw Data", null, async (_, _) => await OpenRawManifestAsync().ConfigureAwait(true));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add("Refresh YEP Diagnostics", null, (_, _) => ScheduleIntelligenceRefresh(immediate: true));
        tools.DropDownItems.Add("Apply Suggested YEP Order", null, (_, _) => ApplySuggestedYanflyOrder());
        _menu.Items.AddRange([file, edit, tools]);
        MainMenuStrip = _menu;
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_mainSplit);
        root.Controls.Add(_buttons);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        root.Controls.Add(_menu);
        return root;
    }

    private void WireEvents()
    {
        _addExisting.Click += async (_, _) => await AddExistingScriptsAsync().ConfigureAwait(true);
        _import.Click += async (_, _) => await ImportScriptAsync().ConfigureAwait(true);
        _remove.Click += (_, _) => RemoveSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _enable.Click += (_, _) => SetSelectedEnabled(true);
        _disable.Click += (_, _) => SetSelectedEnabled(false);
        _localUndo.Click += (_, _) => LocalUndo();
        _localRedo.Click += (_, _) => LocalRedo();
        _search.TextChanged += (_, _) => Rebuild();
        _showDisabled.CheckedChanged += (_, _) => { Rebuild(); CaptureSettings(); };
        _plugins.SelectionChanged += async (_, _) => await UpdateSelectionAsync().ConfigureAwait(true);
        _plugins.CellDoubleClick += (_, _) => OpenScript();
        _description.Leave += (_, _) => CommitDescriptionToSession();
        _parameters.CellEndEdit += (_, e) => CommitParameterCell(e.RowIndex);
        _parameters.CellDoubleClick += (_, e) => EditParameterValue(e.RowIndex);
        _fixYanflyOrder.Click += (_, _) => ApplySuggestedYanflyOrder();
        _intelligenceGrid.CellDoubleClick += (_, e) => SelectDiagnosticPlugin(e.RowIndex);
        _apply.Click += async (_, _) => await ApplyAsync(_lifetime.Token).ConfigureAwait(true);
        _revert.Click += (_, _) => RevertLocal();
        _save.Click += async (_, _) => await SaveProjectAsync().ConfigureAwait(true);
        _close.Click += (_, _) => Close();
        _mainSplit.SplitterMoved += (_, _) => { if (!_rebuilding) CaptureSettings(); };
        _tabs.SelectedIndexChanged += (_, _) => CaptureSettings();
    }

    private void Rebuild(string? preserveName = null, bool scheduleIntelligence = true)
    {
        if (_disposed || IsDisposed) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, () => Rebuild(preserveName, scheduleIntelligence))) return;
        preserveName ??= SelectedEntry()?.Name;
        _rebuilding = true;
        try
        {
            string search = _search.Text.Trim();
            _visibleIndexes = _edit.Entries.Select((entry, index) => (entry, index))
                .Where(item => _showDisabled.Checked || item.entry.Enabled)
                .Where(item => search.Length == 0
                    || item.entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .ToArray();
            _plugins.Rows.Clear();
            foreach (int index in _visibleIndexes)
            {
                PluginEditEntry entry = _edit.Entries[index];
                string scriptPath = GetScriptRelativePath(entry.Name);
                bool exists = _session?.Editing.Files.Exists(scriptPath) == true;
                bool duplicate = _edit.Entries.Count(candidate => string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) > 1;
                InstalledPluginAnalysisEntry? intelligence = _installedPluginAnalysis?.Find(entry.Name);
                string version = intelligence?.Installed.Version ?? string.Empty;
                string compatibility = intelligence?.PrimaryState.ToString() ?? (entry.Name.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase) ? "Analyzing" : string.Empty);
                int issueCount = intelligence?.Issues.Count ?? (duplicate || !exists ? 1 : 0);
                int row = _plugins.Rows.Add(index + 1, entry.Enabled, entry.Name, version, compatibility,
                    exists ? "Found" : "Missing", entry.Parameters.Count, issueCount == 0 ? string.Empty : issueCount.ToString());
                _plugins.Rows[row].Tag = index;
            }
            if (!string.IsNullOrWhiteSpace(preserveName)) SelectPlugin(preserveName);
            if (_plugins.SelectedRows.Count == 0 && _plugins.Rows.Count > 0) _plugins.Rows[0].Selected = true;
            UpdateButtons();
            UpdateStatus();
        }
        finally { _rebuilding = false; }
        if (scheduleIntelligence) ScheduleIntelligenceRefresh();
    }

    private async Task UpdateSelectionAsync()
    {
        if (_rebuilding) return;
        PluginEditEntry? entry = SelectedEntry();
        _rebuilding = true;
        try
        {
            if (entry is null)
            {
                _name.Clear();
                _description.Clear();
                _author.Text = string.Empty;
                _scriptStatus.Text = string.Empty;
                _intelligenceStatus.Text = "YEP status: no plugin selected";
                _help.Clear();
                _parameters.Rows.Clear();
                UpdateButtons();
                return;
            }
            _name.Text = entry.Name;
            _description.Text = entry.Description;
            string scriptRelative = GetScriptRelativePath(entry.Name);
            string scriptFull = GetFullPath(scriptRelative);
            _metadata = await MvPluginMetadataParser.ParseFileAsync(scriptFull, _lifetime.Token).ConfigureAwait(true);
            _author.Text = string.IsNullOrWhiteSpace(_metadata.Author) ? "Author: (not declared)" : "Author: " + _metadata.Author;
            _scriptStatus.Text = File.Exists(scriptFull)
                ? $"Script: {scriptRelative}"
                : $"Script missing: {scriptRelative}";
            InstalledPluginAnalysisEntry? intelligence = _installedPluginAnalysis?.Find(entry.Name);
            _intelligenceStatus.Text = intelligence is null
                ? "YEP status: not yet analyzed"
                : $"YEP status: {intelligence.PrimaryState} · version {intelligence.Installed.Version ?? "unknown"} · {intelligence.Issues.Count} issue(s)";
            _help.Text = _metadata.Help;
            RebuildParameters(entry);
            int index = _edit.Entries.IndexOf(entry);
            _selection.Select(new PluginSelection(index, entry.Name));
            CaptureSettings();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _rebuilding = false;
            UpdateButtons();
            UpdateStatus();
        }
    }

    private void RebuildParameters(PluginEditEntry entry)
    {
        _parameters.Rows.Clear();
        HashSet<string> names = new(entry.Parameters.Select(pair => pair.Key), StringComparer.Ordinal);
        names.UnionWith(_metadata.Parameters.Keys);
        foreach (string name in names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            _metadata.Parameters.TryGetValue(name, out PluginParameterMetadata? metadata);
            string value = entry.GetParameter(name);
            int row = _parameters.Rows.Add(
                metadata?.DisplayName ?? name,
                value,
                metadata?.Type ?? "string",
                metadata?.DefaultValue ?? string.Empty,
                metadata?.Description ?? string.Empty);
            _parameters.Rows[row].Tag = name;
        }
    }

    private void CommitDescriptionToSession()
    {
        if (_rebuilding) return;
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null || string.Equals(entry.Description, _description.Text, StringComparison.Ordinal)) return;
        _edit.Mutate(() => entry.Description = _description.Text);
        UpdateButtons();
        UpdateStatus();
    }

    private void CommitParameterCell(int rowIndex)
    {
        if (_rebuilding || rowIndex < 0 || rowIndex >= _parameters.Rows.Count) return;
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null || _parameters.Rows[rowIndex].Tag is not string name) return;
        string value = Convert.ToString(_parameters.Rows[rowIndex].Cells["Value"].Value) ?? string.Empty;
        if (string.Equals(entry.GetParameter(name), value, StringComparison.Ordinal)) return;
        _edit.Mutate(() => entry.SetParameter(name, value));
        UpdateButtons();
        UpdateStatus();
    }

    private void EditParameterValue(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _parameters.Rows.Count || _parameters.Rows[rowIndex].Tag is not string name) return;
        PluginEditEntry? entry = SelectedEntry();
        MvProjectSession? session = _session;
        if (entry is null || session is null) return;
        PluginParameterMetadata metadata = _metadata.Parameters.TryGetValue(name, out PluginParameterMetadata? declared)
            ? declared
            : new PluginParameterMetadata(name, name, string.Empty, "string", entry.GetParameter(name));
        using PluginParameterValueDialog dialog = new(session, metadata, entry.GetParameter(name));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string value = dialog.Value;
        if (string.Equals(entry.GetParameter(name), value, StringComparison.Ordinal)) return;
        _edit.Mutate(() => entry.SetParameter(name, value));
        _parameters.Rows[rowIndex].Cells["Value"].Value = value;
        UpdateButtons();
        UpdateStatus();
    }

    private void ResetParameterToDefault(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _parameters.Rows.Count || _parameters.Rows[rowIndex].Tag is not string name) return;
        if (!_metadata.Parameters.TryGetValue(name, out PluginParameterMetadata? metadata)) return;
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null) return;
        _edit.Mutate(() => entry.SetParameter(name, metadata.DefaultValue));
        _parameters.Rows[rowIndex].Cells["Value"].Value = metadata.DefaultValue;
        UpdateButtons();
        UpdateStatus();
    }

    private void SetSelectedEnabled(bool enabled)
    {
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null || entry.Enabled == enabled) return;
        string name = entry.Name;
        _edit.Mutate(() => entry.Enabled = enabled);
        Rebuild(name);
    }

    private void MoveSelected(int offset)
    {
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null) return;
        int current = _edit.Entries.IndexOf(entry);
        int destination = current + offset;
        if (destination < 0 || destination >= _edit.Entries.Count) return;
        string name = entry.Name;
        _edit.Mutate(() =>
        {
            _edit.Entries.RemoveAt(current);
            _edit.Entries.Insert(destination, entry);
        });
        Rebuild(name);
    }

    private void RemoveSelected()
    {
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null) return;
        if (_settings.Current.EffectivePreferences.ConfirmDestructivePluginOperations)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"Remove '{entry.Name}' from the plugin manifest?\n\nThe script file will not be deleted.",
                "Remove Plugin Entry",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        int index = _edit.Entries.IndexOf(entry);
        _edit.Mutate(() => _edit.Entries.Remove(entry));
        Rebuild();
        if (_plugins.Rows.Count > 0) _plugins.Rows[Math.Min(index, _plugins.Rows.Count - 1)].Selected = true;
    }

    private async Task AddExistingScriptsAsync()
    {
        string pluginFolder = GetPluginFolderFullPath();
        Directory.CreateDirectory(pluginFolder);
        using OpenFileDialog dialog = new()
        {
            Title = "Add Existing RPG Maker MV Plugin Scripts",
            Filter = "JavaScript plugins (*.js)|*.js|All files (*.*)|*.*",
            InitialDirectory = pluginFolder,
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (string file in dialog.FileNames)
        {
            string full = Path.GetFullPath(file);
            if (!full.StartsWith(Path.GetFullPath(pluginFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, Path.GetFullPath(pluginFolder), StringComparison.OrdinalIgnoreCase))
            {
                await ImportScriptFileAsync(full).ConfigureAwait(true);
                continue;
            }
            await AddEntryForScriptAsync(full).ConfigureAwait(true);
        }
    }

    private async Task ImportScriptAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Import RPG Maker MV Plugin Script",
            Filter = "JavaScript plugins (*.js)|*.js|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ImportScriptFileAsync(dialog.FileName).ConfigureAwait(true);
    }

    private async Task AddEntryForScriptAsync(string scriptFullPath)
    {
        string name = Path.GetFileNameWithoutExtension(scriptFullPath);
        if (_edit.Entries.Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _notifications.Warning("Plugin already listed", $"'{name}' already has a manifest entry.");
            SelectPlugin(name);
            return;
        }
        PluginMetadata metadata = await MvPluginMetadataParser.ParseFileAsync(scriptFullPath, _lifetime.Token).ConfigureAwait(true);
        _edit.Add(name, enabled: true, metadata);
        Rebuild(name);
    }

    private async Task ImportScriptFileAsync(string sourceFile)
    {
        MvProjectSession session = RequireSession();
        string name = Path.GetFileNameWithoutExtension(sourceFile);
        string destination = GetScriptRelativePath(name);
        bool exists = session.Editing.Files.Exists(destination);
        if (exists)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"'{destination}' already exists. Replace the existing script?",
                "Plugin Script Conflict",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        byte[] before = _edit.Capture();
        bool committed = false;
        try
        {
            PluginEditEntry? existing = _edit.Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                PluginMetadata metadata = await MvPluginMetadataParser.ParseFileAsync(sourceFile, _lifetime.Token).ConfigureAwait(true);
                _edit.Add(name, enabled: true, metadata);
            }
            byte[] manifestBytes = _edit.Capture();
            await using ProjectTransaction transaction = session.BeginTransaction($"Import plugin {name}");
            transaction.Add(exists
                ? new ReplaceAssetFileCommand(sourceFile, destination)
                : new ImportAssetFileCommand(sourceFile, destination));
            transaction.Add(new ReplaceDocumentContentCommand(_manifestPath, manifestBytes, $"Update plugin manifest for {name}"));
            TransactionPreview preview = await transaction.PreviewAsync(_lifetime.Token).ConfigureAwait(true);
            using TransactionPreviewForm dialog = new($"Import Plugin — {name}", preview, "Import");
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                _edit.RestoreSnapshot(before, clearHistory: true);
                Rebuild();
                return;
            }
            await transaction.CommitAsync(_lifetime.Token).ConfigureAwait(true);
            committed = true;
            _edit.MarkApplied();
            _sharedConflict = false;
            Rebuild(name);
            _notifications.Success("Plugin imported", $"Staged '{destination}' and updated the plugin manifest.");
        }
        catch
        {
            if (!committed)
            {
                _edit.RestoreSnapshot(before, clearHistory: true);
                Rebuild();
            }
            throw;
        }
    }

    private async Task<bool> ApplyAsync(CancellationToken cancellationToken)
    {
        if (!_edit.IsDirty) return true;
        MvProjectSession session = RequireSession();
        if (_sharedConflict)
        {
            DialogResult conflict = MessageBox.Show(
                this,
                "The shared plugin manifest changed after this Plugin Manager began editing. Applying now will replace those newer manifest changes with this working copy.\n\nContinue?",
                "Plugin Manifest Changed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (conflict != DialogResult.Yes) return false;
        }
        byte[] bytes = _edit.Capture();
        await using ProjectTransaction transaction = session.BeginTransaction("Edit plugin manifest");
        transaction.Add(new ReplaceDocumentContentCommand(_manifestPath, bytes, "Edit plugin manifest"));
        TransactionPreview preview = await transaction.PreviewAsync(cancellationToken).ConfigureAwait(true);
        using TransactionPreviewForm dialog = new("Plugin Manifest Changes", preview, "Apply");
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(true);
        _edit.MarkApplied();
        _sharedConflict = false;
        UpdateButtons();
        UpdateStatus();
        RefreshWindowState();
        return true;
    }

    private void RevertLocal()
    {
        if (!_edit.IsDirty) return;
        DialogResult answer = MessageBox.Show(
            this,
            "Discard every unapplied Plugin Manager change?",
            "Revert Plugin Changes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        _edit.RestoreBaseline();
        _sharedConflict = false;
        Rebuild();
    }

    private void LocalUndo()
    {
        string? name = SelectedEntry()?.Name;
        _edit.Undo();
        Rebuild(name);
    }

    private void LocalRedo()
    {
        string? name = SelectedEntry()?.Name;
        _edit.Redo();
        Rebuild(name);
    }

    private async Task SaveProjectAsync()
    {
        try
        {
            if (!await ApplyAsync(_lifetime.Token).ConfigureAwait(true)) return;
            await _workspace.SaveAsync(cancellationToken: _lifetime.Token).ConfigureAwait(true);
            RefreshWindowState();
            UpdateStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Error("Plugin save failed", exception.Message, exception);
        }
    }

    private void OpenScript()
    {
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null) return;
        string path = GetFullPath(GetScriptRelativePath(entry.Name));
        if (!File.Exists(path))
        {
            _notifications.Warning("Plugin script missing", path);
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void RevealScript()
    {
        PluginEditEntry? entry = SelectedEntry();
        if (entry is null) return;
        string path = GetFullPath(GetScriptRelativePath(entry.Name));
        if (!File.Exists(path))
        {
            _notifications.Warning("Plugin script missing", path);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private async Task OpenRawManifestAsync()
    {
        await _windows.OpenAsync(new WindowRequest(
            EditorWindowKind.RawJson,
            "file:" + _manifestPath,
            "Raw Data — " + Path.GetFileName(_manifestPath),
            Scope: WindowScope.Project,
            PlacementId: "RawJson",
            Document: new DocumentRequest(
                EditorDocumentKind.RawJson,
                "file:" + _manifestPath,
                _manifestPath,
                DisplayTitle: Path.GetFileName(_manifestPath))),
            _lifetime.Token).ConfigureAwait(true);
    }

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (!e.Targets.Any(target => string.Equals(target.NormalizedRelativePath, _manifestPath, StringComparison.OrdinalIgnoreCase))) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnDocumentChanged(sender, e))) return;
        if (_edit.IsDirty)
        {
            _sharedConflict = true;
            UpdateStatus();
        }
        else if (_manifest is not null)
        {
            string? name = SelectedEntry()?.Name;
            _edit.Load(_manifest);
            Rebuild(name);
        }
        RefreshWindowState();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnSessionStateChanged(sender, e))) return;
        UpdateStatus();
        RefreshWindowState();
    }

    private void UpdateButtons()
    {
        PluginEditEntry? entry = SelectedEntry();
        int index = entry is null ? -1 : _edit.Entries.IndexOf(entry);
        _remove.Enabled = entry is not null;
        _enable.Enabled = entry is not null && !entry.Enabled;
        _disable.Enabled = entry is not null && entry.Enabled;
        _up.Enabled = index > 0;
        _down.Enabled = index >= 0 && index < _edit.Entries.Count - 1;
        _localUndo.Enabled = _edit.CanUndo;
        _localRedo.Enabled = _edit.CanRedo;
        _apply.Enabled = _edit.IsDirty;
        _revert.Enabled = _edit.IsDirty;
    }

    private void UpdateStatus()
    {
        int enabled = _edit.Entries.Count(entry => entry.Enabled);
        int missing = _edit.Entries.Count(entry => _session?.Editing.Files.Exists(GetScriptRelativePath(entry.Name)) != true);
        string state = _edit.IsDirty ? " · unapplied changes" : string.Empty;
        if (_sharedConflict) state += " · shared manifest changed";
        if (_installedPluginAnalysis is not null)
        {
            int errors = _installedPluginAnalysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Error);
            int warnings = _installedPluginAnalysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Warning);
            state += $" · YEP {errors} errors/{warnings} warnings";
        }
        _statusText.Text = $"{_edit.Entries.Count:N0} plugins · {enabled:N0} enabled · {missing:N0} missing scripts{state}";
        Text = string.IsNullOrWhiteSpace(_session?.Workspace.Snapshot.Identity.ProjectTitle)
            ? "Plugin Manager" + (_edit.IsDirty ? " *" : string.Empty)
            : $"Plugin Manager — {_session.Workspace.Snapshot.Identity.ProjectTitle}{(_edit.IsDirty ? " *" : string.Empty)}";
    }

    private void ScheduleIntelligenceRefresh(bool immediate = false)
    {
        if (_disposed || _session is null || string.IsNullOrWhiteSpace(_manifestPath)) return;
        int revision = ++_intelligenceRevision;
        PluginEditEntry[] entries = _edit.Entries.Select(entry => entry.Clone()).ToArray();
        string folder = GetPluginFolderFullPath();
        _intelligenceRefresh?.Cancel();
        _intelligenceRefresh?.Dispose();
        _intelligenceRefresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token = _intelligenceRefresh.Token;
        _ = RefreshPluginIntelligenceAsync(revision, entries, folder, immediate ? TimeSpan.Zero : TimeSpan.FromMilliseconds(250), token);
    }

    private async Task RefreshPluginIntelligenceAsync(
        int revision,
        PluginEditEntry[] entries,
        string folder,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            InstalledPluginAnalysis analysis = await _intelligenceService.AnalyzeAsync(
                entries,
                name => Path.Combine(folder, name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name : name + ".js"),
                folder,
                cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || revision != _intelligenceRevision) return;
            await InvokeAsync(() =>
            {
                if (_disposed || revision != _intelligenceRevision) return;
                string? selected = SelectedEntry()?.Name;
                _installedPluginAnalysis = analysis;
                RebuildIntelligenceDiagnostics();
                Rebuild(selected, scheduleIntelligence: false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_disposed) return;
            await InvokeAsync(() =>
            {
                _intelligenceSummary.Text = "YEP intelligence could not be refreshed: " + exception.Message;
                _intelligenceGrid.Rows.Clear();
            }).ConfigureAwait(false);
        }
    }

    private void RebuildIntelligenceDiagnostics()
    {
        InstalledPluginAnalysis? analysis = _installedPluginAnalysis;
        _intelligenceGrid.Rows.Clear();
        if (analysis is null)
        {
            _intelligenceSummary.Text = "YEP plugin intelligence has not run yet.";
            _fixYanflyOrder.Enabled = false;
            return;
        }
        int errors = analysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Error);
        int warnings = analysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Warning);
        int known = analysis.Entries.Count(entry => entry.IsKnown);
        _intelligenceSummary.Text =
            $"{known} recognized YEP plugin entries. {errors} error(s), {warnings} warning(s), " +
            $"{analysis.UnregisteredScriptPluginIds.Count} unregistered script(s), and {analysis.SuggestedMoves.Count} suggested load-order move(s).\r\n" +
            "Double-click a diagnostic to select its plugin. Unknown non-YEP plugins are preserved and reported only as informational.";
        foreach (InstalledPluginIssue issue in analysis.Issues)
        {
            int row = _intelligenceGrid.Rows.Add(issue.Severity, issue.Code, issue.PluginId ?? string.Empty, issue.Message);
            _intelligenceGrid.Rows[row].Tag = issue.PluginId;
        }
        _fixYanflyOrder.Enabled = analysis.SuggestedMoves.Count > 0;
        UpdateButtons();
        UpdateStatus();
    }

    private void SelectDiagnosticPlugin(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _intelligenceGrid.Rows.Count) return;
        if (_intelligenceGrid.Rows[rowIndex].Tag is string pluginId && pluginId.Length > 0) SelectPlugin(pluginId);
    }

    private void ApplySuggestedYanflyOrder()
    {
        IReadOnlyList<string> order = _intelligenceService.CreateStableCorrectedOrder(_edit.Entries);
        if (order.Count != _edit.Entries.Count) return;
        bool changed = order.Where((name, index) => !string.Equals(name, _edit.Entries[index].Name, StringComparison.OrdinalIgnoreCase)).Any();
        if (!changed)
        {
            MessageBox.Show(this, "The current plugin order already satisfies the known YEP dependency rules.", "YEP Load Order", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? selected = SelectedEntry()?.Name;
        Dictionary<string, Queue<PluginEditEntry>> byName = _edit.Entries
            .GroupBy(entry => InstalledPluginIntelligenceAnalyzer.NormalizeId(entry.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<PluginEditEntry>(group), StringComparer.OrdinalIgnoreCase);
        _edit.Mutate(() =>
        {
            List<PluginEditEntry> reordered = order
                .Select(name => byName[InstalledPluginIntelligenceAnalyzer.NormalizeId(name)].Dequeue())
                .ToList();
            _edit.Entries.Clear();
            _edit.Entries.AddRange(reordered);
        });
        Rebuild(selected);
    }

    private PluginEditEntry? SelectedEntry()
    {
        if (_plugins.SelectedRows.Count != 1 || _plugins.SelectedRows[0].Tag is not int index) return null;
        return index >= 0 && index < _edit.Entries.Count ? _edit.Entries[index] : null;
    }

    private void SelectPlugin(string name)
    {
        for (int row = 0; row < _plugins.Rows.Count; row++)
        {
            if (_plugins.Rows[row].Tag is int index
                && string.Equals(_edit.Entries[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _plugins.ClearSelection();
                _plugins.Rows[row].Selected = true;
                _plugins.CurrentCell = _plugins.Rows[row].Cells[0];
                return;
            }
        }
    }

    private void SelectPlugin(int index)
    {
        for (int row = 0; row < _plugins.Rows.Count; row++)
        {
            if (_plugins.Rows[row].Tag is int actual && actual == index)
            {
                _plugins.ClearSelection();
                _plugins.Rows[row].Selected = true;
                _plugins.CurrentCell = _plugins.Rows[row].Cells[0];
                return;
            }
        }
    }

    private string GetScriptRelativePath(string pluginName)
    {
        string directory = Path.GetDirectoryName(_manifestPath)?.Replace('\\', '/') ?? "js";
        return (directory.TrimEnd('/') + "/plugins/" + pluginName + ".js").TrimStart('/');
    }

    private string GetPluginFolderFullPath()
    {
        string manifestDirectory = Path.GetDirectoryName(GetFullPath(_manifestPath)) ?? RequireSession().Workspace.Snapshot.Identity.ProjectRoot;
        return Path.Combine(manifestDirectory, "plugins");
    }

    private string GetFullPath(string relativePath) => Path.Combine(
        RequireSession().Workspace.Snapshot.Identity.ProjectRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private MvProjectSession RequireSession() => _session
        ?? throw new InvalidOperationException("No plugin editing session is active.");

    private async Task RestoreInitialSelectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? name = Request.Location?.SelectionPath
            ?? Request.Document?.EventSurface
            ?? _settings.Current.EffectivePluginManagerWindowLayout.SelectedPluginName;
        int? index = Request.Location?.RecordId ?? Request.Document?.RecordId;
        if (!string.IsNullOrWhiteSpace(name)) SelectPlugin(name);
        else if (index.HasValue) SelectPlugin(index.Value);
        await Task.CompletedTask;
    }

    private void RestoreSettings()
    {
        PluginManagerWindowLayoutSettings value = _settings.Current.EffectivePluginManagerWindowLayout;
        _rebuilding = true;
        try
        {
            _search.Text = value.SearchText;
            _showDisabled.Checked = value.ShowDisabled;
            ControlHelpers.SetSplitterDistanceSafe(_mainSplit, value.ListPanelWidth);
        }
        finally { _rebuilding = false; }
    }

    private void CaptureSettings()
    {
        if (_rebuilding || _disposed) return;
        _settings.Update(current => current with
        {
            PluginManagerWindowLayout = new PluginManagerWindowLayoutSettings(
                _search.Text,
                _mainSplit.SplitterDistance,
                DetailsPanelHeight: 260,
                _showDisabled.Checked,
                SelectedEntry()?.Name),
            Version = EditorSettingsMigrator.CurrentVersion
        });
    }

    private sealed record PluginManagerRecoveryPayload(
        string SnapshotBase64,
        string? SelectedPluginName,
        int Version = 1);

}

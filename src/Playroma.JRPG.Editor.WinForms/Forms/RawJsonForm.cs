using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Editing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.RpgMakerMV.Documents;
using Playroma.JRPG.Workspaces;

using ShellEditorCommandContext = Playroma.JRPG.Editor.Shell.EditorCommandContext;
namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Per-file advanced data window. Inspection is always available; editing uses a private
/// buffer and commits through one undoable project transaction.
/// </summary>
public sealed class RawJsonForm : EditorToolForm, IShutdownParticipant
{
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IEditorNavigationCoordinator _navigation;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripButton _enableEditing = new("Enable Advanced Editing") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _apply = new("Apply") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _revert = new("Revert Buffer") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _reload = new("Reload Shared State") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _typedEditor = new("Open Typed Editor") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _wrap = new("Word Wrap") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripTextBox _find = new() { AutoSize = false, Width = 220, ToolTipText = "Find text" };
    private readonly ToolStripButton _findNext = new("Find Next") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _copyPath = new("Copy File Path") { DisplayStyle = ToolStripItemDisplayStyle.Text };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _current = CreateEditor(readOnly: true);
    private readonly RichTextBox _working = CreateEditor(readOnly: false);
    private readonly RichTextBox _original = CreateEditor(readOnly: true);
    private readonly DataGridView _differences = ControlHelpers.CreateReadOnlyGrid();
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _state = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _syntax = new();

    private MvProjectSession? _session;
    private IProjectDocumentLease? _lease;
    private IMutableProjectDocument? _document;
    private string _relativePath = string.Empty;
    private string _sharedText = string.Empty;
    private bool _advancedEnabled;
    private bool _localDirty;
    private bool _sharedChangedWhileEditing;
    private bool _suppressWorkingChange;
    private bool _disposed;

    public RawJsonForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISettingsService settings,
        INotificationService notifications,
        IEditorNavigationCoordinator navigation)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Name = nameof(RawJsonForm);
        Text = request.DisplayTitle ?? "Raw JSON";
        MinimumSize = new Size(820, 580);
        Size = new Size(1120, 760);
        KeyPreview = true;
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        BuildUi();
        WireEvents();
    }

    public override bool IsDirty => _localDirty || _document?.State == MutableDocumentState.Modified;

    public ValueTask<ShutdownContribution?> PrepareShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_localDirty) return ValueTask.FromResult<ShutdownContribution?>(null);
        ShutdownContribution contribution = new(
            Key,
            Title,
            $"The advanced data buffer for '{_relativePath}' contains unapplied JSON changes.",
            CanApply: !_sharedChangedWhileEditing && IsWorkingSyntaxValid(),
            CanDiscard: true,
            CanRecover: true);
        return ValueTask.FromResult<ShutdownContribution?>(contribution);
    }

    public async ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default)
    {
        if (!_localDirty) return true;
        switch (action)
        {
            case ShutdownLocalAction.Apply:
                if (_sharedChangedWhileEditing || !IsWorkingSyntaxValid() || _session is null) return false;
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(_working.Text);
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                        parsed.RootElement,
                        new JsonSerializerOptions { WriteIndented = true });
                    await using ProjectTransaction transaction = _session.BeginTransaction("Edit raw data: " + _relativePath);
                    transaction.Add(new ReplaceDocumentContentCommand(_relativePath, bytes, "Edit raw data: " + _relativePath));
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(true);
                    await ReloadSharedStateAsync(cancellationToken, resetWorking: true).ConfigureAwait(true);
                    RefreshWindowState();
                    return true;
                }
                catch (Exception exception)
                {
                    _notifications.Error("Could not apply raw data changes", exception.Message, exception);
                    return false;
                }
            case ShutdownLocalAction.Discard:
                ResetWorkingBuffer();
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
        if (!_localDirty) return ValueTask.FromResult<PrivateSessionRecoveryState?>(null);
        RawJsonRecoveryPayload payload = new(
            _working.Text,
            _working.SelectionStart,
            _tabs.SelectedIndex,
            Version: 1);
        string baseline = HashText(NormalizeText(_sharedText));
        PrivateSessionRecoveryState state = new(
            "raw-json",
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
        if (!string.Equals(state.SessionType, "raw-json", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }
        RawJsonRecoveryPayload? payload = JsonSerializer.Deserialize<RawJsonRecoveryPayload>(state.DataJson);
        if (payload is null || payload.Version > 1) return ValueTask.CompletedTask;
        _advancedEnabled = true;
        _working.Enabled = true;
        _enableEditing.Text = "Advanced Editing Enabled";
        _enableEditing.Enabled = false;
        _suppressWorkingChange = true;
        try
        {
            _working.Text = payload.WorkingText;
            _working.SelectionStart = Math.Clamp(payload.CaretPosition, 0, _working.TextLength);
            _working.SelectionLength = 0;
            _tabs.SelectedIndex = Math.Clamp(payload.SelectedTab, 0, _tabs.TabCount - 1);
        }
        finally
        {
            _suppressWorkingChange = false;
        }
        _localDirty = !string.Equals(NormalizeText(_working.Text), NormalizeText(_sharedText), StringComparison.Ordinal);
        string currentBaseline = HashText(NormalizeText(_sharedText));
        _sharedChangedWhileEditing = !string.IsNullOrWhiteSpace(state.BaselineFingerprint)
            && !string.Equals(currentBaseline, state.BaselineFingerprint, StringComparison.OrdinalIgnoreCase);
        ValidateWorkingSyntax();
        _apply.Enabled = _localDirty && !_sharedChangedWhileEditing && IsWorkingSyntaxValid();
        _revert.Enabled = _localDirty;
        RefreshWindowState();
        UpdateStatus();
        return ValueTask.CompletedTask;
    }

    public override NavigationLocation? CaptureLocation()
    {
        DocumentRequest document = Request.Document ?? new DocumentRequest(
            EditorDocumentKind.RawJson,
            "file:" + _relativePath,
            _relativePath,
            DisplayTitle: Path.GetFileName(_relativePath));
        RichTextBox editor = ActiveEditor();
        return new NavigationLocation(document, SelectionPath: editor.SelectionStart.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public override ValueTask RestoreLocationAsync(NavigationLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(location.SelectionPath)) GoToPath(location.SelectionPath);
        if (location.RecordId.HasValue) GoToPath('/' + location.RecordId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_localDirty)
        {
            DialogResult result = MessageBox.Show(
                this,
                "The advanced data buffer contains unapplied changes.\n\nYes: Apply and close.\nNo: Discard the local buffer and close.\nCancel: Continue editing.",
                "Unapplied Raw Data Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes && !await ApplyAsync(cancellationToken).ConfigureAwait(true)) return false;
        }
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        _session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("Raw data cannot open without an active project.");
        _relativePath = Request.Document?.RelativePath ?? Request.ToolTip
            ?? throw new InvalidOperationException("A project-relative file path is required.");
        _lease = await _session.Workspace.AcquireDocumentAsync(_relativePath, cancellationToken).ConfigureAwait(true);
        _document = _lease.Document;
        _session.DocumentChanged += OnDocumentChanged;
        _typedEditor.Enabled = CanOpenTypedEditor(_relativePath, Request.Document);
        RestoreSettings();
        InstallContent(BuildRoot());
        await ReloadSharedStateAsync(cancellationToken, resetWorking: true).ConfigureAwait(true);
        if (Request.Location is not null) await RestoreLocationAsync(Request.Location, cancellationToken).ConfigureAwait(true);
    }

    protected override async ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null) _session.DocumentChanged -= OnDocumentChanged;
        _lifetime.Cancel();
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(true);
            _lease = null;
        }
        _document = null;
        _session = null;
        _lifetime.Dispose();
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, ShellEditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find in Raw Data");
        if (command == EditorCommandIds.SessionApply)
            return _advancedEnabled && _localDirty && !_sharedChangedWhileEditing && IsWorkingSyntaxValid()
                ? EditorCommandStatus.Enabled("Apply Raw Data Changes")
                : EditorCommandStatus.Disabled("Apply Raw Data Changes", _sharedChangedWhileEditing ? "The shared document changed." : "There are no valid unapplied changes.");
        if (command == EditorCommandIds.SessionRevert)
            return _localDirty ? EditorCommandStatus.Enabled("Revert Raw Data Buffer") : EditorCommandStatus.Disabled("Revert Raw Data Buffer");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, ShellEditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) _find.Focus();
        else if (command == EditorCommandIds.SessionApply) await ApplyAsync(context.CancellationToken).ConfigureAwait(true);
        else if (command == EditorCommandIds.SessionRevert) ResetWorkingBuffer();
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter) && _advancedEnabled) { _ = ApplyAsync(_lifetime.Token); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _find.Focus(); return true; }
        if (keyData == Keys.F3) { FindNext(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        _find.TextBox.PlaceholderText = "Find…";
        _toolbar.Items.AddRange([
            _enableEditing, _apply, _revert, _reload,
            new ToolStripSeparator(), _typedEditor, _wrap,
            new ToolStripSeparator(), new ToolStripLabel("Find:"), _find, _findNext,
            new ToolStripSeparator(), _copyPath]);

        _working.Enabled = false;
        _tabs.TabPages.Add(new TabPage("Current Shared State") { Controls = { _current } });
        _tabs.TabPages.Add(new TabPage("Advanced Edit Buffer") { Controls = { _working } });
        _tabs.TabPages.Add(new TabPage("Original Baseline") { Controls = { _original } });
        _differences.Columns.Add("Kind", "Kind");
        _differences.Columns.Add("Path", "JSON Path");
        _differences.Columns.Add("Before", "Before");
        _differences.Columns.Add("After", "After");
        _differences.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _tabs.TabPages.Add(new TabPage("Semantic Differences") { Controls = { _differences } });
        _status.Items.AddRange([_state, _syntax]);
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_tabs);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        return root;
    }

    private void WireEvents()
    {
        _enableEditing.Click += (_, _) => EnableAdvancedEditing();
        _apply.Click += async (_, _) => await ApplyAsync(_lifetime.Token).ConfigureAwait(true);
        _revert.Click += (_, _) => ResetWorkingBuffer();
        _reload.Click += async (_, _) => await ReloadSharedStateAsync(_lifetime.Token, resetWorking: !_localDirty).ConfigureAwait(true);
        _typedEditor.Click += async (_, _) => await OpenTypedEditorAsync().ConfigureAwait(true);
        _wrap.CheckedChanged += (_, _) => ApplyWordWrap();
        _findNext.Click += (_, _) => FindNext();
        _copyPath.Click += (_, _) => Clipboard.SetText(_relativePath);
        _working.TextChanged += (_, _) => OnWorkingTextChanged();
        _tabs.SelectedIndexChanged += (_, _) => UpdateStatus();
    }

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (!e.Targets.Any(target => string.Equals(target.NormalizedRelativePath, _relativePath, StringComparison.OrdinalIgnoreCase))) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnDocumentChanged(sender, e))) return;
        if (_localDirty)
        {
            _sharedChangedWhileEditing = true;
            UpdateStatus();
            return;
        }
        _ = ReloadSharedStateAsync(_lifetime.Token, resetWorking: true);
    }

    private async Task ReloadSharedStateAsync(CancellationToken cancellationToken, bool resetWorking)
    {
        if (_document is null) return;
        byte[] currentBytes = _document is ITransactionalDocumentSnapshot snapshot
            ? snapshot.CaptureGeneratedBytes()
            : (await _document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true)).ToArray();
        byte[] originalBytes = _document switch
        {
            MvPluginManifestDocument plugin => plugin.GetArrayOriginalBytes().ToArray(),
            _ => _document.GetOriginalBytes().ToArray()
        };
        _sharedText = Pretty(currentBytes);
        string originalText = Pretty(originalBytes);
        IReadOnlyList<JsonSemanticDifference> differences = JsonSemanticDiffer.Compare(originalBytes, currentBytes);
        _current.Text = _sharedText;
        _original.Text = originalText;
        PopulateDifferences(differences);
        if (resetWorking) ResetWorkingBuffer();
        _sharedChangedWhileEditing = false;
        UpdateStatus();
    }

    private void EnableAdvancedEditing()
    {
        if (_advancedEnabled) { _tabs.SelectedIndex = 1; _working.Focus(); return; }
        ApplicationPreferenceSettings preferences = _settings.Current.EffectivePreferences;
        if (preferences.ConfirmAdvancedRawJsonEditing)
        {
            DialogResult result = MessageBox.Show(
                this,
                "Advanced data editing can change any value in this file. Playroma will validate syntax and show a semantic transaction preview, but it cannot guarantee that arbitrary values are valid for RPG Maker MV.\n\nEnable advanced editing for this window?",
                "Enable Advanced Editing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }
        _advancedEnabled = true;
        _working.Enabled = true;
        _enableEditing.Text = "Advanced Editing Enabled";
        _enableEditing.Enabled = false;
        _tabs.SelectedIndex = 1;
        _working.Focus();
        UpdateStatus();
    }

    private void OnWorkingTextChanged()
    {
        if (_suppressWorkingChange) return;
        _localDirty = !string.Equals(NormalizeText(_working.Text), NormalizeText(_sharedText), StringComparison.Ordinal);
        ValidateWorkingSyntax();
        _apply.Enabled = _advancedEnabled && _localDirty && !_sharedChangedWhileEditing && IsWorkingSyntaxValid();
        _revert.Enabled = _localDirty;
        RefreshWindowState();
        UpdateStatus();
    }

    private async Task<bool> ApplyAsync(CancellationToken cancellationToken)
    {
        if (!_advancedEnabled || !_localDirty || _document is null) return true;
        if (_sharedChangedWhileEditing)
        {
            MessageBox.Show(this, "The shared document changed after this edit buffer was created. Reload the shared state and merge your changes before applying.", "Raw Data Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        byte[] bytes;
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(_working.Text);
            bytes = JsonSerializer.SerializeToUtf8Bytes(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        try
        {
            var session = _workspace.CurrentSession ?? throw new InvalidOperationException("No project is open.");
            await using ProjectTransaction previewTransaction = session.BeginTransaction("Edit raw data: " + _relativePath);
            previewTransaction.Add(new ReplaceDocumentContentCommand(_relativePath, bytes, "Edit raw data: " + _relativePath));
            var preview = await previewTransaction.PreviewAsync(cancellationToken).ConfigureAwait(true);
            using TransactionPreviewForm dialog = new(preview, "Apply Raw Data Edit");
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;

            await using ProjectTransaction commit = session.BeginTransaction("Edit raw data: " + _relativePath);
            commit.Add(new ReplaceDocumentContentCommand(_relativePath, bytes, "Edit raw data: " + _relativePath));
            await commit.CommitAsync(cancellationToken).ConfigureAwait(true);
            await ReloadSharedStateAsync(cancellationToken, resetWorking: true).ConfigureAwait(true);
            _notifications.Success("Raw data edit applied", _relativePath);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            _notifications.Error("Raw data edit failed", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Raw Data Edit Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ResetWorkingBuffer()
    {
        _suppressWorkingChange = true;
        try
        {
            _working.Text = _sharedText;
            _localDirty = false;
            _revert.Enabled = false;
            _apply.Enabled = false;
        }
        finally { _suppressWorkingChange = false; }
        ValidateWorkingSyntax();
        RefreshWindowState();
        UpdateStatus();
    }

    private async Task OpenTypedEditorAsync()
    {
        try
        {
            DocumentRequest? document = BuildTypedDocumentRequest(_relativePath, Request.Document);
            if (document is null)
            {
                _notifications.Information("No typed editor", "This project file currently has only the Raw Data editor.");
                return;
            }
            await _navigation.NavigateAsync(
                new NavigationLocation(document, RecordId: Request.Document?.RecordId, MapId: Request.Document?.MapId),
                this,
                cancellationToken: _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception) { _notifications.Error("Could not open typed editor", exception.Message, exception); }
    }

    private static bool CanOpenTypedEditor(string relativePath, DocumentRequest? source) =>
        BuildTypedDocumentRequest(relativePath, source) is not null;

    private static DocumentRequest? BuildTypedDocumentRequest(string relativePath, DocumentRequest? source)
    {
        MvDocumentKind? databaseKind = source?.DatabaseKind ?? GuessDatabaseKind(relativePath);
        if (databaseKind.HasValue)
        {
            return new DocumentRequest(
                databaseKind == MvDocumentKind.System ? EditorDocumentKind.SystemData : EditorDocumentKind.Database,
                "database:" + databaseKind.Value,
                relativePath,
                databaseKind,
                source?.RecordId,
                DisplayTitle: databaseKind.Value.ToString());
        }

        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("MapInfos", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fileName.AsSpan(3), out int mapId))
        {
            return new DocumentRequest(
                EditorDocumentKind.MapView,
                $"map-view:{mapId}",
                relativePath,
                MapId: mapId,
                DisplayTitle: $"Map {mapId}");
        }

        if (relativePath.EndsWith("plugins.js", StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentRequest(
                EditorDocumentKind.PluginInventory,
                "plugins",
                relativePath,
                DisplayTitle: "Plugin Manager");
        }

        return null;
    }

    private static MvDocumentKind? GuessDatabaseKind(string relativePath)
    {
        string file = Path.GetFileNameWithoutExtension(relativePath);
        return file.ToLowerInvariant() switch
        {
            "actors" => MvDocumentKind.Actors,
            "animations" => MvDocumentKind.Animations,
            "armors" => MvDocumentKind.Armors,
            "classes" => MvDocumentKind.Classes,
            "commonevents" => MvDocumentKind.CommonEvents,
            "enemies" => MvDocumentKind.Enemies,
            "items" => MvDocumentKind.Items,
            "skills" => MvDocumentKind.Skills,
            "states" => MvDocumentKind.States,
            "system" => MvDocumentKind.System,
            "tilesets" => MvDocumentKind.Tilesets,
            "troops" => MvDocumentKind.Troops,
            "weapons" => MvDocumentKind.Weapons,
            _ => null
        };
    }

    public void GoToPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        RichTextBox editor = ActiveEditor();
        if (int.TryParse(path, out int caret))
        {
            editor.SelectionStart = Math.Clamp(caret, 0, editor.TextLength);
            editor.SelectionLength = 0;
            editor.ScrollToCaret();
            return;
        }
        string token = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        int index = editor.Text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;
        editor.SelectionStart = index;
        editor.SelectionLength = Math.Min(token.Length, editor.TextLength - index);
        editor.ScrollToCaret();
        editor.Focus();
    }

    private void FindNext()
    {
        RichTextBox editor = ActiveEditor();
        string term = _find.Text;
        if (term.Length == 0) return;
        int start = Math.Min(editor.SelectionStart + Math.Max(editor.SelectionLength, 1), editor.TextLength);
        int index = editor.Find(term, start, RichTextBoxFinds.None);
        if (index < 0) index = editor.Find(term, 0, RichTextBoxFinds.None);
        if (index >= 0)
        {
            editor.Select(index, term.Length);
            editor.ScrollToCaret();
            editor.Focus();
        }
    }

    private RichTextBox ActiveEditor() => _tabs.SelectedIndex switch
    {
        1 => _working,
        2 => _original,
        _ => _current
    };

    private void ValidateWorkingSyntax()
    {
        if (!_advancedEnabled)
        {
            _syntax.Text = "Inspection mode";
            return;
        }
        try
        {
            using JsonDocument _ = JsonDocument.Parse(_working.Text);
            _syntax.Text = "Valid JSON";
            _syntax.ForeColor = Color.ForestGreen;
        }
        catch (JsonException exception)
        {
            _syntax.Text = $"Invalid JSON at line {exception.LineNumber}, byte {exception.BytePositionInLine}";
            _syntax.ForeColor = Color.Firebrick;
        }
    }

    private bool IsWorkingSyntaxValid()
    {
        try { using JsonDocument _ = JsonDocument.Parse(_working.Text); return true; }
        catch (JsonException) { return false; }
    }

    private void PopulateDifferences(IReadOnlyList<JsonSemanticDifference> differences)
    {
        _differences.Rows.Clear();
        foreach (JsonSemanticDifference difference in differences)
            _differences.Rows.Add(difference.Kind, difference.Path, difference.Before ?? string.Empty, difference.After ?? string.Empty);
    }

    private void UpdateStatus()
    {
        string mode = _advancedEnabled ? "Advanced edit session" : "Read-only inspection";
        string dirty = _localDirty ? " · local buffer modified" : string.Empty;
        string conflict = _sharedChangedWhileEditing ? " · CONFLICT: shared state changed" : string.Empty;
        string wrapper = _document is MvPluginManifestDocument ? " · editing extracted $plugins array; JavaScript wrapper preserved" : string.Empty;
        _state.Text = $"{_relativePath} · {_document?.State} · {mode}{dirty}{conflict}{wrapper}";
        Text = (Request.DisplayTitle ?? Path.GetFileName(_relativePath)) + (_localDirty ? " *" : string.Empty);
    }

    private void ApplyWordWrap()
    {
        foreach (RichTextBox editor in new[] { _current, _working, _original }) editor.WordWrap = _wrap.Checked;
        CaptureSettings();
    }

    private void RestoreSettings()
    {
        RawJsonWindowLayoutSettings saved = _settings.Current.EffectiveRawJsonWindowLayout;
        _tabs.SelectedIndex = Math.Clamp(saved.SelectedTab, 0, 3);
        _find.Text = saved.SearchText;
        _wrap.Checked = saved.WordWrap;
        foreach (RichTextBox editor in new[] { _current, _working, _original })
        {
            editor.WordWrap = saved.WordWrap;
            editor.Font = new Font(FontFamily.GenericMonospace, Math.Clamp(saved.FontSize, 7, 24));
        }
    }

    private void CaptureSettings()
    {
        if (IsDisposed) return;
        RawJsonWindowLayoutSettings layout = new(
            SelectedTab: _tabs.SelectedIndex,
            SearchText: _find.Text,
            WordWrap: _wrap.Checked,
            FontSize: (int)Math.Round(_current.Font.Size));
        _settings.Update(current => current with { RawJsonWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private static RichTextBox CreateEditor(bool readOnly) => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = readOnly,
        Font = new Font(FontFamily.GenericMonospace, 9f),
        WordWrap = false,
        DetectUrls = false,
        BorderStyle = BorderStyle.None,
        AcceptsTab = !readOnly
    };

    private static string Pretty(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes);
            return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return Encoding.UTF8.GetString(bytes.Span); }
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string NormalizeText(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private sealed record RawJsonRecoveryPayload(
        string WorkingText,
        int CaretPosition,
        int SelectedTab,
        int Version = 1);

}

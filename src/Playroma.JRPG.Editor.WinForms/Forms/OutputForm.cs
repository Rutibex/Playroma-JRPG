using System.Diagnostics;
using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Virtualized project output console with filtering, details and optional navigation.</summary>
public sealed class OutputForm : EditorToolForm
{
    private readonly IOutputService _output;
    private readonly IEditorNavigationCoordinator _navigation;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Threading.Timer _refreshTimer;

    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ToolStripComboBox _severity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ToolStripTextBox _search = new() { AutoSize = false, Width = 230, ToolTipText = "Search output" };
    private readonly ToolStripButton _pause = new("Pause") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _follow = new("Follow Newest") { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _wrap = new("Word Wrap") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _copy = new("Copy") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _save = new("Save Log…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _openLogs = new("Open Logs Folder") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _clear = new("Clear") { DisplayStyle = ToolStripItemDisplayStyle.Text };

    private readonly SplitContainer _split = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 450, 220, 100, splitterWidth: 6);
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        AutoGenerateColumns = false,
        VirtualMode = true,
        BorderStyle = BorderStyle.None,
        BackgroundColor = SystemColors.Window
    };
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private IReadOnlyList<OutputEntry> _visible = [];
    private int _refreshQueued;
    private bool _disposed;
    private bool _restoring;
    private bool _rebuildingCategories;
    private string _savedCategoryFilter = "All";

    public OutputForm(
        WindowRequest request,
        IOutputService output,
        IEditorNavigationCoordinator navigation,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _refreshTimer = new(_ => PostRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        Name = nameof(OutputForm);
        Text = "Output";
        MinimumSize = new Size(780, 520);
        Size = new Size(1080, 700);
        KeyPreview = true;
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        BuildUi();
        WireEvents();
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        RestoreSettings();
        InstallContent(BuildRoot());
        Rebuild();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _output.Changed -= OnOutputChanged;
        _refreshTimer.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Output Entry");
        if (command == EditorCommandIds.EditCopy)
            return _grid.SelectedRows.Count > 0 ? EditorCommandStatus.Enabled("Copy Output Entry") : EditorCommandStatus.Disabled("Copy Output Entry");
        return base.QueryCommand(command, context);
    }

    public override ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) _search.Focus();
        else if (command == EditorCommandIds.EditCopy) CopySelected();
        else return base.ExecuteCommandAsync(command, context);
        NotifyCommandStateChanged();
        return ValueTask.CompletedTask;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.C) && _grid.Focused) { CopySelected(); return true; }
        if (keyData == Keys.Enter && _grid.Focused) { _ = NavigateSelectedAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveLog(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        _category.Items.Add("All");
        _severity.Items.AddRange(["All", "Errors", "Warnings", "Information", "Unspecified"]);
        _category.SelectedIndex = 0;
        _severity.SelectedIndex = 0;
        _search.TextBox.PlaceholderText = "Search output…";
        _toolbar.Items.AddRange([
            new ToolStripLabel("Category:"), _category,
            new ToolStripLabel("Severity:"), _severity,
            new ToolStripSeparator(), new ToolStripLabel("Search:"), _search,
            new ToolStripSeparator(), _pause, _follow, _wrap,
            new ToolStripSeparator(), _copy, _save, _openLogs, _clear]);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", Width = 135 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Message", HeaderText = "Message", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(_details);
        _status.Items.Add(_statusText);
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_split);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        return root;
    }

    private void WireEvents()
    {
        _output.Changed += OnOutputChanged;
        _category.SelectedIndexChanged += (_, _) => { if (!_rebuildingCategories) { Rebuild(); CaptureSettings(); } };
        _severity.SelectedIndexChanged += (_, _) => { Rebuild(); CaptureSettings(); };
        _search.TextChanged += (_, _) => { Rebuild(); CaptureSettings(); };
        _pause.CheckedChanged += (_, _) => { _pause.Text = _pause.Checked ? "Resume" : "Pause"; if (!_pause.Checked) Rebuild(); };
        _follow.CheckedChanged += (_, _) => CaptureSettings();
        _wrap.CheckedChanged += (_, _) => { _details.WordWrap = _wrap.Checked; CaptureSettings(); };
        _copy.Click += (_, _) => CopySelected();
        _save.Click += (_, _) => SaveLog();
        _openLogs.Click += (_, _) => OpenLogsFolder();
        _clear.Click += (_, _) => _output.Clear();
        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.SelectionChanged += (_, _) => UpdateDetails();
        _grid.CellDoubleClick += async (_, _) => await NavigateSelectedAsync().ConfigureAwait(true);
        _grid.CellFormatting += OnCellFormatting;
        _split.SplitterMoved += (_, _) => { if (!_restoring) CaptureSettings(); };
    }

    private void OnOutputChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
        try { _refreshTimer.Change(100, Timeout.Infinite); }
        catch (ObjectDisposedException) { Volatile.Write(ref _refreshQueued, 0); }
    }

    private void PostRefresh()
    {
        if (_disposed || IsDisposed) { Volatile.Write(ref _refreshQueued, 0); return; }
        try
        {
            BeginInvoke(new Action(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                if (!_disposed && !_pause.Checked) Rebuild();
            }));
        }
        catch (ObjectDisposedException) { Volatile.Write(ref _refreshQueued, 0); }
        catch (InvalidOperationException) { Volatile.Write(ref _refreshQueued, 0); }
    }

    private void Rebuild()
    {
        if (_disposed || IsDisposed) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, Rebuild)) return;
        int? selectedIndex = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : null;
        RebuildCategories();
        string category = _category.SelectedItem?.ToString() ?? "All";
        string severity = _severity.SelectedItem?.ToString() ?? "All";
        string search = _search.Text.Trim();
        IEnumerable<OutputEntry> query = _output.Entries;
        if (!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        query = severity switch
        {
            "Errors" => query.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal),
            "Warnings" => query.Where(item => item.Severity == DiagnosticSeverity.Warning),
            "Information" => query.Where(item => item.Severity == DiagnosticSeverity.Information),
            "Unspecified" => query.Where(item => item.Severity is null),
            _ => query
        };
        if (search.Length > 0)
        {
            query = query.Where(item => item.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (item.Detail?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        _visible = query.ToArray();
        _grid.RowCount = _visible.Count;
        _grid.Invalidate();
        if (_visible.Count > 0)
        {
            int index = _follow.Checked ? _visible.Count - 1 : Math.Clamp(selectedIndex ?? 0, 0, _visible.Count - 1);
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
            _grid.Rows[index].Selected = true;
            if (_follow.Checked) _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, index);
        }
        _statusText.Text = $"{_visible.Count:N0} shown · {_output.Entries.Count:N0} retained";
        UpdateDetails();
    }

    private void RebuildCategories()
    {
        string selected = _category.SelectedItem?.ToString() ?? _savedCategoryFilter;
        string[] categories = _output.Entries.Select(item => item.Category)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _rebuildingCategories = true;
        try
        {
            _category.Items.Clear();
            _category.Items.Add("All");
            _category.Items.AddRange(categories.Cast<object>().ToArray());
            int index = _category.Items.Cast<object>().Select(item => item.ToString()).ToList()
                .FindIndex(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
            _category.SelectedIndex = index >= 0 ? index : 0;
            _savedCategoryFilter = _category.SelectedItem?.ToString() ?? "All";
        }
        finally { _rebuildingCategories = false; }
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
        OutputEntry entry = _visible[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"),
            1 => entry.Severity?.ToString() ?? string.Empty,
            2 => entry.Category,
            3 => entry.Message,
            _ => string.Empty
        };
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visible.Count || e.ColumnIndex != 1) return;
        e.CellStyle.ForeColor = _visible[e.RowIndex].Severity switch
        {
            DiagnosticSeverity.Fatal or DiagnosticSeverity.Error => Color.Firebrick,
            DiagnosticSeverity.Warning => Color.DarkGoldenrod,
            DiagnosticSeverity.Information => Color.SteelBlue,
            _ => _grid.ForeColor
        };
    }

    private void UpdateDetails()
    {
        OutputEntry? entry = SelectedEntry();
        if (entry is null) { _details.Clear(); return; }
        StringBuilder text = new();
        text.AppendLine(entry.TimestampUtc.ToLocalTime().ToString("O"));
        text.AppendLine($"[{entry.Category}] {entry.Severity?.ToString() ?? "Output"}");
        text.AppendLine();
        text.AppendLine(entry.Message);
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            text.AppendLine();
            text.AppendLine(entry.Detail);
        }
        if (entry.Location is not null)
        {
            text.AppendLine();
            text.AppendLine("Location: " + entry.Location.Document.RelativePath);
            text.AppendLine("Selection: " + entry.Location.SelectionPath);
        }
        _details.Text = text.ToString();
    }

    private async Task NavigateSelectedAsync()
    {
        OutputEntry? entry = SelectedEntry();
        if (entry?.Location is null) return;
        try { await _navigation.NavigateAsync(entry.Location, this, cancellationToken: _lifetime.Token).ConfigureAwait(true); }
        catch (Exception exception) { _notifications.Error("Could not navigate", exception.Message, exception); }
    }

    private OutputEntry? SelectedEntry()
    {
        if (_grid.SelectedRows.Count != 1) return null;
        int index = _grid.SelectedRows[0].Index;
        return index >= 0 && index < _visible.Count ? _visible[index] : null;
    }

    private void CopySelected()
    {
        OutputEntry[] entries = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Index)
            .Where(index => index >= 0 && index < _visible.Count)
            .OrderBy(index => index)
            .Select(index => _visible[index])
            .ToArray();
        if (entries.Length == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine, entries.Select(FormatEntry)));
    }

    private void SaveLog()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Save Output Log",
            Filter = "Text log (*.log)|*.log|Text (*.txt)|*.txt",
            FileName = "playroma-output.log"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, _visible.Select(FormatEntry)), new UTF8Encoding(false));
        _notifications.Success("Output saved", dialog.FileName);
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppDataPaths.Logs);
        Process.Start(new ProcessStartInfo(AppDataPaths.Logs) { UseShellExecute = true });
    }

    private static string FormatEntry(OutputEntry entry)
    {
        string header = $"[{entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}] [{entry.Category}]" +
                        (entry.Severity.HasValue ? $" [{entry.Severity}]" : string.Empty) +
                        " " + entry.Message;
        return string.IsNullOrWhiteSpace(entry.Detail) ? header : header + Environment.NewLine + entry.Detail;
    }

    private void RestoreSettings()
    {
        _restoring = true;
        try
        {
            OutputWindowLayoutSettings saved = _settings.Current.EffectiveOutputWindowLayout;
            _savedCategoryFilter = saved.CategoryFilter;
            _pause.Checked = saved.Paused;
            _follow.Checked = saved.AutoScroll;
            _wrap.Checked = saved.WordWrap;
            _details.WordWrap = saved.WordWrap;
            _search.Text = saved.SearchText;
            SelectCombo(_severity, saved.SeverityFilter);
            ControlHelpers.SetSplitterDistanceSafe(_split, Height - saved.DetailsPanelHeight - 150);
        }
        finally { _restoring = false; }
    }

    private void CaptureSettings()
    {
        if (_restoring || IsDisposed) return;
        OutputWindowLayoutSettings layout = new(
            CategoryFilter: _category.SelectedItem?.ToString() ?? "All",
            SeverityFilter: _severity.SelectedItem?.ToString() ?? "All",
            SearchText: _search.Text,
            AutoScroll: _follow.Checked,
            Paused: _pause.Checked,
            WordWrap: _wrap.Checked,
            DetailsPanelHeight: _split.Panel2.Height);
        _settings.Update(current => current with { OutputWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private static void SelectCombo(ToolStripComboBox combo, string value)
    {
        int index = combo.Items.Cast<object>().Select(item => item.ToString()).ToList()
            .FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = index >= 0 ? index : 0;
    }
}

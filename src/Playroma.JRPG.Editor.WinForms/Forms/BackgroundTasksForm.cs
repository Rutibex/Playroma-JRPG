using System.Text;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Modeless task monitor for project loading, saving, validation, indexing and imports.</summary>
public sealed class BackgroundTasksForm : EditorToolForm
{
    private readonly IBackgroundTaskService _tasks;
    private readonly IWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly System.Threading.Timer _refreshTimer;

    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripComboBox _state = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ToolStripComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ToolStripButton _cancelSelected = new("Cancel Selected") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _cancelAll = new("Cancel All") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _clear = new("Clear Finished") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _clearFailed = new("Include Failed") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _openOutput = new("Open Output") { DisplayStyle = ToolStripItemDisplayStyle.Text };

    private readonly SplitContainer _split = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 430, 220, 100, splitterWidth: 6);
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
    private readonly ToolStripProgressBar _overallProgress = new() { Width = 170, Minimum = 0, Maximum = 100 };

    private IReadOnlyList<BackgroundTaskInfo> _visible = [];
    private int _refreshQueued;
    private bool _disposed;
    private bool _restoring;
    private Font? _boldGridFont;
    private bool _rebuildingCategories;
    private string _savedCategoryFilter = "All";

    public BackgroundTasksForm(
        WindowRequest request,
        IBackgroundTaskService tasks,
        IWindowService windows,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _refreshTimer = new(_ => PostRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        Name = nameof(BackgroundTasksForm);
        Text = "Background Tasks";
        MinimumSize = new Size(800, 540);
        Size = new Size(1100, 700);
        KeyPreview = true;
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        BuildUi();
        _boldGridFont = new Font(_grid.Font, FontStyle.Bold);
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
        _tasks.TasksChanged -= OnTasksChanged;
        _refreshTimer.Dispose();
        _boldGridFont?.Dispose();
        _boldGridFont = null;
        return ValueTask.CompletedTask;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        BackgroundTaskInfo[] selected = SelectedTasks().ToArray();
        if (command == EditorCommandIds.EditCopy)
            return selected.Length > 0 ? EditorCommandStatus.Enabled("Copy Task Details") : EditorCommandStatus.Disabled("Copy Task Details");
        if (command == EditorCommandIds.EditDelete)
            return selected.Any(task => task.Definition.CanCancel) ? EditorCommandStatus.Enabled("Cancel Selected Task") : EditorCommandStatus.Disabled("Cancel Selected Task");
        if (command == EditorCommandIds.ViewRefresh) return EditorCommandStatus.Enabled("Refresh Tasks");
        return base.QueryCommand(command, context);
    }

    public override ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditCopy) CopySelected();
        else if (command == EditorCommandIds.EditDelete) CancelSelected();
        else if (command == EditorCommandIds.ViewRefresh) Rebuild();
        else return base.ExecuteCommandAsync(command, context);
        NotifyCommandStateChanged();
        return ValueTask.CompletedTask;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Delete) { CancelSelected(); return true; }
        if (keyData == Keys.F5) { Rebuild(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopySelected(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        _state.Items.AddRange(["All", "Queued", "Running", "Completed", "Cancelled", "Failed"]);
        _category.Items.Add("All");
        _state.SelectedIndex = 0;
        _category.SelectedIndex = 0;
        _toolbar.Items.AddRange([
            new ToolStripLabel("State:"), _state,
            new ToolStripLabel("Category:"), _category,
            new ToolStripSeparator(), _cancelSelected, _cancelAll,
            new ToolStripSeparator(), _clear, _clearFailed,
            new ToolStripSeparator(), _openOutput]);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Task", HeaderText = "Task", Width = 230 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "Category", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "State", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Progress", HeaderText = "Progress", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Elapsed", HeaderText = "Elapsed", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Current", HeaderText = "Current Item / Message", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(_details);
        _status.Items.AddRange([_statusText, _overallProgress]);
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
        _tasks.TasksChanged += OnTasksChanged;
        _state.SelectedIndexChanged += (_, _) => { Rebuild(); CaptureSettings(); };
        _category.SelectedIndexChanged += (_, _) => { if (!_rebuildingCategories) { Rebuild(); CaptureSettings(); } };
        _cancelSelected.Click += (_, _) => CancelSelected();
        _cancelAll.Click += (_, _) => CancelAll();
        _clear.Click += (_, _) => _tasks.ClearFinished(_clearFailed.Checked);
        _clearFailed.CheckedChanged += (_, _) => CaptureSettings();
        _openOutput.Click += async (_, _) => await OpenOutputAsync().ConfigureAwait(true);
        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.SelectionChanged += (_, _) => UpdateDetails();
        _grid.CellFormatting += OnCellFormatting;
        _split.SplitterMoved += (_, _) => { if (!_restoring) CaptureSettings(); };
    }

    private void OnTasksChanged(object? sender, EventArgs e)
    {
        if (_disposed || Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
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
                if (!_disposed) Rebuild();
            }));
        }
        catch (ObjectDisposedException) { Volatile.Write(ref _refreshQueued, 0); }
        catch (InvalidOperationException) { Volatile.Write(ref _refreshQueued, 0); }
    }

    private void Rebuild()
    {
        if (_disposed || IsDisposed) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, Rebuild)) return;
        RebuildCategories();
        Guid[] selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Index)
            .Where(index => index >= 0 && index < _visible.Count)
            .Select(index => _visible[index].Id)
            .ToArray();
        string state = _state.SelectedItem?.ToString() ?? "All";
        string category = _category.SelectedItem?.ToString() ?? "All";
        IEnumerable<BackgroundTaskInfo> query = _tasks.Tasks;
        if (!string.Equals(state, "All", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(state, out BackgroundTaskState parsed))
            query = query.Where(task => task.State == parsed);
        if (!string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(task => string.Equals(task.Definition.Category, category, StringComparison.OrdinalIgnoreCase));
        _visible = query.OrderByDescending(task => task.CreatedUtc).ToArray();
        _grid.RowCount = _visible.Count;
        _grid.Invalidate();
        foreach (Guid id in selected)
        {
            int index = _visible.ToList().FindIndex(task => task.Id == id);
            if (index >= 0) _grid.Rows[index].Selected = true;
        }
        UpdateSummary();
        UpdateDetails();
    }

    private void RebuildCategories()
    {
        string selected = _category.SelectedItem?.ToString() ?? _savedCategoryFilter;
        string[] categories = _tasks.Tasks.Select(task => task.Definition.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
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
        BackgroundTaskInfo task = _visible[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => task.Definition.Name,
            1 => task.Definition.Category,
            2 => task.State,
            3 => FormatProgress(task.Progress),
            4 => FormatElapsed(task.Elapsed),
            5 => task.Progress?.CurrentItem ?? task.Progress?.Message ?? task.Error?.Message ?? string.Empty,
            _ => string.Empty
        };
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visible.Count || e.ColumnIndex != 2) return;
        BackgroundTaskState state = _visible[e.RowIndex].State;
        e.CellStyle.Font = _boldGridFont ?? _grid.Font;
        e.CellStyle.ForeColor = state switch
        {
            BackgroundTaskState.Failed => Color.Firebrick,
            BackgroundTaskState.Cancelled => Color.DarkGoldenrod,
            BackgroundTaskState.Completed => Color.ForestGreen,
            BackgroundTaskState.Running => Color.SteelBlue,
            _ => _grid.ForeColor
        };
    }

    private void UpdateSummary()
    {
        int running = _tasks.Tasks.Count(task => task.State == BackgroundTaskState.Running);
        int queued = _tasks.Tasks.Count(task => task.State == BackgroundTaskState.Queued);
        int failed = _tasks.Tasks.Count(task => task.State == BackgroundTaskState.Failed);
        _statusText.Text = $"{_visible.Count:N0} shown · {running:N0} running · {queued:N0} queued · {failed:N0} failed";
        TaskProgress[] activeProgress = _tasks.Tasks
            .Where(task => task.State == BackgroundTaskState.Running && task.Progress?.Percentage is not null)
            .Select(task => task.Progress!)
            .ToArray();
        if (activeProgress.Length == 0)
        {
            _overallProgress.Value = 0;
            _overallProgress.Style = running > 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        }
        else
        {
            _overallProgress.Style = ProgressBarStyle.Blocks;
            _overallProgress.Value = Math.Clamp((int)Math.Round(activeProgress.Average(progress => progress.Percentage ?? 0)), 0, 100);
        }
        _cancelSelected.Enabled = SelectedTasks().Any(task => task.Definition.CanCancel && task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running);
        _cancelAll.Enabled = _tasks.Tasks.Any(task => task.Definition.CanCancel && task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running);
    }

    private void UpdateDetails()
    {
        IReadOnlyList<BackgroundTaskInfo> selected = SelectedTasks();
        BackgroundTaskInfo? task = selected.Count == 1 ? selected[0] : null;
        if (task is null)
        {
            _details.Clear();
            UpdateSummary();
            return;
        }
        StringBuilder text = new();
        text.AppendLine(task.Definition.Name);
        text.AppendLine($"Category: {task.Definition.Category}");
        text.AppendLine($"State: {task.State}");
        text.AppendLine($"Created: {task.CreatedUtc.ToLocalTime():O}");
        if (task.StartedUtc.HasValue) text.AppendLine($"Started: {task.StartedUtc.Value.ToLocalTime():O}");
        if (task.CompletedUtc.HasValue) text.AppendLine($"Completed: {task.CompletedUtc.Value.ToLocalTime():O}");
        text.AppendLine($"Elapsed: {task.Elapsed}");
        if (task.Progress is not null)
        {
            text.AppendLine();
            text.AppendLine($"Progress: {FormatProgress(task.Progress)}");
            if (!string.IsNullOrWhiteSpace(task.Progress.CurrentItem)) text.AppendLine("Current item: " + task.Progress.CurrentItem);
            if (!string.IsNullOrWhiteSpace(task.Progress.Message)) text.AppendLine("Message: " + task.Progress.Message);
        }
        if (task.Error is not null)
        {
            text.AppendLine();
            text.AppendLine("Error:");
            text.AppendLine(task.Error.ToString());
        }
        _details.Text = text.ToString();
        UpdateSummary();
    }

    private IReadOnlyList<BackgroundTaskInfo> SelectedTasks() => _grid.SelectedRows.Cast<DataGridViewRow>()
        .Select(row => row.Index)
        .Where(index => index >= 0 && index < _visible.Count)
        .Select(index => _visible[index])
        .ToArray();

    private void CancelSelected()
    {
        foreach (BackgroundTaskInfo task in SelectedTasks().Where(task => task.Definition.CanCancel))
            _tasks.Cancel(task.Id);
    }

    private void CancelAll()
    {
        foreach (BackgroundTaskInfo task in _tasks.Tasks.Where(task => task.Definition.CanCancel && task.State is BackgroundTaskState.Queued or BackgroundTaskState.Running))
            _tasks.Cancel(task.Id);
    }

    private void CopySelected()
    {
        BackgroundTaskInfo[] selected = SelectedTasks().ToArray();
        if (selected.Length == 0) return;
        Clipboard.SetText(string.Join(Environment.NewLine + Environment.NewLine, selected.Select(task =>
            $"{task.Definition.Name} [{task.State}] {FormatProgress(task.Progress)} {task.Progress?.CurrentItem ?? task.Error?.Message}")));
    }

    private async Task OpenOutputAsync()
    {
        try
        {
            await _windows.OpenAsync(new WindowRequest(
                EditorWindowKind.Output,
                "output",
                "Output",
                Scope: WindowScope.Project,
                PlacementId: "Output")).ConfigureAwait(true);
        }
        catch (Exception exception) { _notifications.Error("Could not open Output", exception.Message, exception); }
    }

    private void RestoreSettings()
    {
        _restoring = true;
        try
        {
            BackgroundTasksWindowLayoutSettings saved = _settings.Current.EffectiveBackgroundTasksWindowLayout;
            _savedCategoryFilter = saved.CategoryFilter;
            SelectCombo(_state, saved.StateFilter);
            _clearFailed.Checked = saved.IncludeFailedWhenClearing;
            ControlHelpers.SetSplitterDistanceSafe(_split, Height - saved.DetailsPanelHeight - 150);
        }
        finally { _restoring = false; }
    }

    private void CaptureSettings()
    {
        if (_restoring || IsDisposed) return;
        BackgroundTasksWindowLayoutSettings layout = new(
            StateFilter: _state.SelectedItem?.ToString() ?? "All",
            CategoryFilter: _category.SelectedItem?.ToString() ?? "All",
            IncludeFailedWhenClearing: _clearFailed.Checked,
            DetailsPanelHeight: _split.Panel2.Height);
        _settings.Update(current => current with { BackgroundTasksWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private static string FormatProgress(TaskProgress? progress)
    {
        if (progress is null) return string.Empty;
        return progress.Percentage.HasValue
            ? $"{progress.Percentage.Value:0}% ({progress.Completed:N0}/{progress.Total:N0})"
            : $"{progress.Completed:N0}/{progress.Total:N0}";
    }

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? elapsed.ToString(@"hh\:mm\:ss")
        : elapsed.ToString(@"mm\:ss");

    private static void SelectCombo(ToolStripComboBox combo, string value)
    {
        int index = combo.Items.Cast<object>().Select(item => item.ToString()).ToList()
            .FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = index >= 0 ? index : 0;
    }
}

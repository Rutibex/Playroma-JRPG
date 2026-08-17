using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Validation;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Project-scoped validation center with filtering, details, export and deep navigation.</summary>
public sealed class ValidationForm : EditorToolForm
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _selection;
    private readonly IEditorNavigationCoordinator _navigation;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripButton _validate = new("Validate") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _cancel = new("Cancel") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _fix = new("Fix Selected") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _export = new("Export…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripComboBox _severity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ToolStripComboBox _grouping = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ToolStripTextBox _search = new() { AutoSize = false, Width = 230, ToolTipText = "Search diagnostics" };

    private readonly SplitContainer _mainSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Vertical, 190, 150, 520, splitterWidth: 6);
    private readonly ListBox _groups = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly SplitContainer _detailSplit = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 420, 200, 110, splitterWidth: 6);
    private readonly DataGridView _grid = ControlHelpers.CreateReadOnlyGrid();
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = true,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _summary = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Minimum = 0, Maximum = 100, Visible = false, Width = 160 };

    private IReadOnlyList<ProjectValidationDiagnostic> _all = [];
    private CancellationTokenSource? _validationCancellation;
    private bool _disposed;
    private bool _restoring;
    private Font? _boldGridFont;

    public ValidationForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISelectionService selection,
        IEditorNavigationCoordinator navigation,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Name = nameof(ValidationForm);
        Text = "Validation";
        MinimumSize = new Size(860, 600);
        Size = new Size(1160, 760);
        KeyPreview = true;
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        _mainSplit.FixedPanel = FixedPanel.Panel1;

        BuildUi();
        _boldGridFont = new Font(_grid.Font, FontStyle.Bold);
        WireEvents();
    }

    public override string Title
    {
        get
        {
            string project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle
                ?? (_workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectRoot is { } root
                    ? Path.GetFileName(root)
                    : string.Empty);
            return string.IsNullOrWhiteSpace(project) ? "Validation" : $"Validation — {project}";
        }
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        _validationCancellation?.Cancel();
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        if (_workspace.CurrentSession is null)
        {
            throw new InvalidOperationException("Validation cannot open without an active project.");
        }
        RestoreSettings();
        _all = _workspace.LastValidation?.Diagnostics ?? [];
        InstallContent(BuildRoot());
        Rebuild();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _validationCancellation?.Cancel();
        _validationCancellation?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _workspace.ValidationChanged -= OnValidationChanged;
        _workspace.StateChanged -= OnWorkspaceStateChanged;
        _boldGridFont?.Dispose();
        _boldGridFont = null;
        return ValueTask.CompletedTask;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Validation Result");
        if (command == EditorCommandIds.ViewRefresh || command == EditorCommandIds.ProjectValidate)
            return EditorCommandStatus.Enabled("Validate Project");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) _search.Focus();
        else if (command == EditorCommandIds.ViewRefresh || command == EditorCommandIds.ProjectValidate)
            await RunValidationAsync().ConfigureAwait(true);
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RunValidationAsync(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); return true; }
        if (keyData == Keys.Enter && _grid.Focused) { _ = NavigateSelectedAsync(); return true; }
        if (keyData == (Keys.Control | Keys.E)) { ExportDiagnostics(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        _severity.Items.AddRange(["All", "Errors", "Warnings", "Information"]);
        _grouping.Items.AddRange(["Severity", "Document", "Code", "Subsystem"]);
        _severity.SelectedIndex = 0;
        _grouping.SelectedIndex = 0;
        _search.TextBox.PlaceholderText = "Search diagnostics…";
        _toolbar.Items.AddRange([
            _validate, _cancel, new ToolStripSeparator(), _fix, _export,
            new ToolStripSeparator(), new ToolStripLabel("Severity:"), _severity,
            new ToolStripLabel("Group:"), _grouping,
            new ToolStripSeparator(), new ToolStripLabel("Search:"), _search]);

        _grid.Columns.Add("Severity", "Severity");
        _grid.Columns.Add("Code", "Code");
        _grid.Columns.Add("File", "File");
        _grid.Columns.Add("Location", "Location");
        _grid.Columns.Add("Message", "Message");
        _grid.Columns.Add("Fix", "Fix");
        _grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;

        _detailSplit.Panel1.Controls.Add(_grid);
        _detailSplit.Panel2.Controls.Add(_details);
        _mainSplit.Panel1.Controls.Add(_groups);
        _mainSplit.Panel2.Controls.Add(_detailSplit);
        _status.Items.AddRange([_summary, _progress]);
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_mainSplit);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        return root;
    }

    private void WireEvents()
    {
        _workspace.ValidationChanged += OnValidationChanged;
        _workspace.StateChanged += OnWorkspaceStateChanged;
        _validate.Click += async (_, _) => await RunValidationAsync().ConfigureAwait(true);
        _cancel.Click += (_, _) => _validationCancellation?.Cancel();
        _fix.Click += async (_, _) => await PreviewAndApplyFixAsync().ConfigureAwait(true);
        _export.Click += (_, _) => ExportDiagnostics();
        _severity.SelectedIndexChanged += (_, _) => Rebuild();
        _grouping.SelectedIndexChanged += (_, _) => Rebuild();
        _search.TextChanged += (_, _) => Rebuild();
        _groups.SelectedIndexChanged += (_, _) => RebuildGrid();
        _grid.SelectionChanged += (_, _) => UpdateDetails();
        _grid.CellDoubleClick += async (_, _) => await NavigateSelectedAsync().ConfigureAwait(true);
        _grid.CellFormatting += OnCellFormatting;
        _mainSplit.SplitterMoved += (_, _) => { if (!_restoring) CaptureSettings(); };
        _detailSplit.SplitterMoved += (_, _) => { if (!_restoring) CaptureSettings(); };
    }

    private void OnValidationChanged(object? sender, ValidationChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnValidationChanged(sender, e))) return;
        _all = e.Result?.Diagnostics ?? [];
        Rebuild();
    }

    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceStateChanged(sender, e))) return;
        Text = Title;
    }

    private async Task RunValidationAsync()
    {
        if (_validationCancellation is not null) return;
        _validationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _validate.Enabled = false;
        _cancel.Enabled = true;
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Marquee;
        _summary.Text = "Validating project…";
        try
        {
            ProjectValidationResult result = await _workspace.ValidateAsync(_validationCancellation.Token).ConfigureAwait(true);
            _all = result.Diagnostics;
            Rebuild();
        }
        catch (OperationCanceledException)
        {
            _summary.Text = "Validation cancelled.";
        }
        catch (Exception exception)
        {
            _notifications.Error("Validation failed", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _validationCancellation.Dispose();
            _validationCancellation = null;
            _validate.Enabled = true;
            _cancel.Enabled = false;
            _progress.Visible = false;
            _progress.Style = ProgressBarStyle.Blocks;
        }
    }

    private void Rebuild()
    {
        if (_disposed || IsDisposed) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, Rebuild)) return;
        string? selectedKey = SelectedDiagnostic() is { } selected ? DiagnosticKey(selected) : null;
        PopulateGroups();
        RebuildGrid(selectedKey);
    }

    private void PopulateGroups()
    {
        string previous = _groups.SelectedItem?.ToString() ?? "All";
        IReadOnlyList<ProjectValidationDiagnostic> filtered = ApplyBaseFilter(_all).ToArray();
        IEnumerable<(string Name, int Count)> groups = (_grouping.SelectedItem?.ToString() ?? "Severity") switch
        {
            "Document" => filtered.GroupBy(item => item.Diagnostic.Location.RelativePath ?? "Project")
                .Select(group => (group.Key, group.Count())),
            "Code" => filtered.GroupBy(item => item.Code).Select(group => (group.Key, group.Count())),
            "Subsystem" => filtered.GroupBy(item => Subsystem(item.Diagnostic.Location.RelativePath, item.Code))
                .Select(group => (group.Key, group.Count())),
            _ => filtered.GroupBy(item => item.Severity.ToString()).Select(group => (group.Key, group.Count()))
        };
        List<string> entries = ["All"];
        entries.AddRange(groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Name} ({group.Count})"));
        _groups.BeginUpdate();
        try
        {
            _groups.Items.Clear();
            _groups.Items.AddRange(entries.Cast<object>().ToArray());
            int index = entries.FindIndex(item => string.Equals(StripCount(item), StripCount(previous), StringComparison.OrdinalIgnoreCase));
            _groups.SelectedIndex = Math.Max(0, index);
        }
        finally { _groups.EndUpdate(); }
    }

    private void RebuildGrid(string? selectedKey = null)
    {
        string selectedGroup = StripCount(_groups.SelectedItem?.ToString() ?? "All");
        IEnumerable<ProjectValidationDiagnostic> query = ApplyBaseFilter(_all);
        if (!string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = (_grouping.SelectedItem?.ToString() ?? "Severity") switch
            {
                "Document" => query.Where(item => string.Equals(item.Diagnostic.Location.RelativePath ?? "Project", selectedGroup, StringComparison.OrdinalIgnoreCase)),
                "Code" => query.Where(item => string.Equals(item.Code, selectedGroup, StringComparison.OrdinalIgnoreCase)),
                "Subsystem" => query.Where(item => string.Equals(Subsystem(item.Diagnostic.Location.RelativePath, item.Code), selectedGroup, StringComparison.OrdinalIgnoreCase)),
                _ => query.Where(item => string.Equals(item.Severity.ToString(), selectedGroup, StringComparison.OrdinalIgnoreCase))
            };
        }

        ProjectValidationDiagnostic[] visible = query
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenBy(item => item.Diagnostic.Location.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (ProjectValidationDiagnostic item in visible)
            {
                var location = item.Diagnostic.Location;
                int row = _grid.Rows.Add(
                    item.Severity,
                    item.Code,
                    location.RelativePath ?? "Project",
                    FormatLocation(location),
                    item.Diagnostic.Message,
                    item.Fix?.Title ?? string.Empty);
                _grid.Rows[row].Tag = item;
                if (selectedKey is not null && DiagnosticKey(item) == selectedKey)
                {
                    _grid.Rows[row].Selected = true;
                    _grid.CurrentCell = _grid.Rows[row].Cells[0];
                }
            }
        }
        finally { _grid.ResumeLayout(); }
        int errors = visible.Count(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
        int warnings = visible.Count(item => item.Severity == DiagnosticSeverity.Warning);
        _summary.Text = $"{visible.Length:N0} shown · {errors:N0} error(s) · {warnings:N0} warning(s) · {_all.Count:N0} total";
        UpdateDetails();
    }

    private IEnumerable<ProjectValidationDiagnostic> ApplyBaseFilter(IEnumerable<ProjectValidationDiagnostic> source)
    {
        IEnumerable<ProjectValidationDiagnostic> query = source;
        query = (_severity.SelectedItem?.ToString() ?? "All") switch
        {
            "Errors" => query.Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal),
            "Warnings" => query.Where(item => item.Severity == DiagnosticSeverity.Warning),
            "Information" => query.Where(item => item.Severity == DiagnosticSeverity.Information),
            _ => query
        };
        string text = _search.Text.Trim();
        if (text.Length > 0)
        {
            query = query.Where(item =>
                item.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
                || item.Diagnostic.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (item.Diagnostic.Location.RelativePath?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.Diagnostic.TechnicalDetail?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return query;
    }

    private void UpdateDetails()
    {
        ProjectValidationDiagnostic? diagnostic = SelectedDiagnostic();
        _fix.Enabled = diagnostic?.Fix is { IsSafe: true };
        if (diagnostic is null)
        {
            _details.Clear();
            return;
        }
        _selection.Select(new DiagnosticSelection(diagnostic));
        StringBuilder text = new();
        text.AppendLine($"{diagnostic.Severity}: {diagnostic.Code}");
        text.AppendLine(diagnostic.Diagnostic.Message);
        text.AppendLine();
        text.AppendLine("Location: " + FormatLocation(diagnostic.Diagnostic.Location));
        if (!string.IsNullOrWhiteSpace(diagnostic.Diagnostic.Location.RelativePath))
            text.AppendLine("File: " + diagnostic.Diagnostic.Location.RelativePath);
        if (!string.IsNullOrWhiteSpace(diagnostic.Diagnostic.TechnicalDetail))
        {
            text.AppendLine();
            text.AppendLine("Technical detail:");
            text.AppendLine(diagnostic.Diagnostic.TechnicalDetail);
        }
        if (diagnostic.Fix is not null)
        {
            text.AppendLine();
            text.AppendLine($"Fix: {diagnostic.Fix.Title}");
            text.AppendLine(diagnostic.Fix.IsSafe ? "This fix is marked safe." : "This fix requires manual review.");
        }
        _details.Text = text.ToString();
    }

    private async Task NavigateSelectedAsync()
    {
        ProjectValidationDiagnostic? diagnostic = SelectedDiagnostic();
        if (diagnostic is null) return;
        try
        {
            NavigationLocation location = EditorNavigationCoordinator.FromProjectLocation(diagnostic.Diagnostic.Location);
            await _navigation.NavigateAsync(location, this, cancellationToken: _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not navigate", exception.Message, exception);
        }
    }

    private async Task PreviewAndApplyFixAsync()
    {
        ProjectValidationDiagnostic? diagnostic = SelectedDiagnostic();
        var session = _workspace.CurrentSession;
        if (diagnostic?.Fix is not { IsSafe: true } || session is null) return;
        try
        {
            var preview = await session.PreviewFixAsync(diagnostic, _lifetime.Token).ConfigureAwait(true);
            using TransactionPreviewForm dialog = new(preview, "Apply Safe Fix");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await session.ApplyFixAsync(diagnostic, _lifetime.Token).ConfigureAwait(true);
            await _workspace.ValidateAsync(_lifetime.Token).ConfigureAwait(true);
            _notifications.Success("Validation fix applied", diagnostic.Fix.Title);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Error("Safe fix failed", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Safe fix failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportDiagnostics()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Validation Results",
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json|Text (*.txt)|*.txt",
            FileName = "playroma-validation.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ProjectValidationDiagnostic[] diagnostics = VisibleDiagnostics().ToArray();
        string extension = Path.GetExtension(dialog.FileName);
        string content = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(diagnostics.Select(ToExportObject), new JsonSerializerOptions { WriteIndented = true })
            : extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? string.Join(Environment.NewLine, diagnostics.Select(item => $"[{item.Severity}] {item.Code} {item.Diagnostic.Location.RelativePath} {item.Diagnostic.Message}"))
                : BuildCsv(diagnostics);
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _notifications.Success("Validation exported", dialog.FileName);
    }

    private IEnumerable<ProjectValidationDiagnostic> VisibleDiagnostics() =>
        _grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag).OfType<ProjectValidationDiagnostic>();

    private static object ToExportObject(ProjectValidationDiagnostic item) => new
    {
        severity = item.Severity.ToString(),
        item.Code,
        item.Diagnostic.Message,
        location = item.Diagnostic.Location,
        item.Diagnostic.TechnicalDetail,
        fix = item.Fix?.Title
    };

    private static string BuildCsv(IEnumerable<ProjectValidationDiagnostic> diagnostics)
    {
        StringBuilder text = new("Severity,Code,File,Location,Message,Fix\r\n");
        foreach (ProjectValidationDiagnostic item in diagnostics)
        {
            text.Append(Csv(item.Severity.ToString())).Append(',')
                .Append(Csv(item.Code)).Append(',')
                .Append(Csv(item.Diagnostic.Location.RelativePath ?? string.Empty)).Append(',')
                .Append(Csv(FormatLocation(item.Diagnostic.Location))).Append(',')
                .Append(Csv(item.Diagnostic.Message)).Append(',')
                .Append(Csv(item.Fix?.Title ?? string.Empty)).Append("\r\n");
        }
        return text.ToString();
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    private void RestoreSettings()
    {
        _restoring = true;
        try
        {
            ValidationWindowLayoutSettings saved = _settings.Current.EffectiveValidationWindowLayout;
            SelectCombo(_severity, saved.SeverityFilter);
            SelectCombo(_grouping, saved.Grouping);
            _search.Text = saved.SearchText;
            ControlHelpers.SetSplitterDistanceSafe(_mainSplit, saved.NavigationPanelWidth);
            ControlHelpers.SetSplitterDistanceSafe(_detailSplit, Height - saved.DetailsPanelHeight - 180);
        }
        finally { _restoring = false; }
    }

    private void CaptureSettings()
    {
        if (_restoring || IsDisposed) return;
        int detailsHeight = _detailSplit.Panel2.Height;
        ValidationWindowLayoutSettings layout = new(
            SeverityFilter: _severity.SelectedItem?.ToString() ?? "All",
            Grouping: _grouping.SelectedItem?.ToString() ?? "Severity",
            SearchText: _search.Text,
            NavigationPanelWidth: _mainSplit.SplitterDistance,
            DetailsPanelHeight: detailsHeight,
            SelectedDiagnosticKey: SelectedDiagnostic() is { } item ? DiagnosticKey(item) : null);
        _settings.Update(current => current with { ValidationWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private ProjectValidationDiagnostic? SelectedDiagnostic() =>
        _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ProjectValidationDiagnostic : null;

    private static void SelectCombo(ToolStripComboBox combo, string value)
    {
        int index = combo.Items.Cast<object>().Select(item => item.ToString()).ToList()
            .FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string DiagnosticKey(ProjectValidationDiagnostic item) =>
        $"{item.Code}|{item.Diagnostic.Location.RelativePath}|{item.Diagnostic.Location.JsonPath}|{item.Diagnostic.Message}";

    private static int SeverityRank(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Fatal => 4,
        DiagnosticSeverity.Error => 3,
        DiagnosticSeverity.Warning => 2,
        _ => 1
    };

    private static string FormatLocation(ProjectLocation location)
    {
        List<string> parts = [];
        if (location.MapId.HasValue) parts.Add("Map " + location.MapId.Value);
        if (location.RecordId.HasValue) parts.Add("Record " + location.RecordId.Value);
        if (location.EventId.HasValue) parts.Add("Event " + location.EventId.Value);
        if (location.PageIndex.HasValue) parts.Add("Page " + (location.PageIndex.Value + 1));
        if (location.CommandIndex.HasValue) parts.Add("Command " + location.CommandIndex.Value);
        if (!string.IsNullOrWhiteSpace(location.JsonPath)) parts.Add(location.JsonPath);
        return parts.Count == 0 ? "Project" : string.Join(" / ", parts);
    }

    private static string Subsystem(string? relativePath, string code)
    {
        string path = relativePath ?? string.Empty;
        if (path.StartsWith("data/Map", StringComparison.OrdinalIgnoreCase)) return "Maps";
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return "Database";
        if (path.StartsWith("img/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "Assets";
        if (path.Contains("plugins", StringComparison.OrdinalIgnoreCase) || code.StartsWith("PJRPG14", StringComparison.Ordinal)) return "Plugins";
        return "Project";
    }

    private static string StripCount(string value)
    {
        int index = value.LastIndexOf(" (", StringComparison.Ordinal);
        return index > 0 && value.EndsWith(')') ? value[..index] : value;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not ProjectValidationDiagnostic item) return;
        if (e.ColumnIndex == 0)
        {
            e.CellStyle.Font = _boldGridFont ?? _grid.Font;
            e.CellStyle.ForeColor = item.Severity switch
            {
                DiagnosticSeverity.Fatal or DiagnosticSeverity.Error => Color.Firebrick,
                DiagnosticSeverity.Warning => Color.DarkGoldenrod,
                _ => _grid.ForeColor
            };
        }
    }
}

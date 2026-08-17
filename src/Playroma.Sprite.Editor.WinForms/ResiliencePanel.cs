using Playroma.Sprite.Resilience;
using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class ResiliencePanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly SpritePreferencesStore preferencesStore;
    private readonly SpriteWorkspaceProfileStore workspaceStore;
    private readonly SpriteRecoveryService recoveryService;
    private readonly ResilientBackgroundJobQueue jobs;
    private readonly SpritePerformanceMonitor performance;
    private readonly Func<SpriteWorkspaceProfile> captureWorkspace;
    private readonly Action<SpriteWorkspaceProfile> applyWorkspace;
    private readonly Action<SpriteEditorPreferences> applyPreferences;
    private SpriteEditorPreferences preferences;

    private readonly ComboBox themeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown uiScaleBox = Number(75, 300, 100);
    private readonly NumericUpDown fontScaleBox = Number(75, 200, 100);
    private readonly NumericUpDown autosaveBox = Number(15, 3600, 60);
    private readonly NumericUpDown undoMemoryBox = Number(16, 8192, 512);
    private readonly NumericUpDown retentionBox = Number(2, 100, 12);
    private readonly NumericUpDown exportConcurrencyBox = Number(1, 8, 2);
    private readonly CheckBox highContrastBox = Check("High-contrast colors");
    private readonly CheckBox reduceMotionBox = Check("Reduce animation and UI motion");
    private readonly CheckBox largeDocumentWarningsBox = Check("Warn when documents exceed performance thresholds", true);
    private readonly ListBox workspaceList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox workspaceNameBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox mirrorWorkspaceBox = Check("Mirror inspector to the left");
    private readonly ListView recoveryList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly DataGridView shortcutGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false };
    private readonly ListView jobList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly ListView performanceList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly TextBox diagnosticsBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private IReadOnlyList<SpriteRecoverySnapshot> recoveries = [];
    private IReadOnlyList<SpriteWorkspaceProfile> workspaces = [];

    public ResiliencePanel(
        SpriteEditorSession session,
        SpriteEditorPreferences preferences,
        SpritePreferencesStore preferencesStore,
        SpriteWorkspaceProfileStore workspaceStore,
        SpriteRecoveryService recoveryService,
        ResilientBackgroundJobQueue jobs,
        SpritePerformanceMonitor performance,
        Func<SpriteWorkspaceProfile> captureWorkspace,
        Action<SpriteWorkspaceProfile> applyWorkspace,
        Action<SpriteEditorPreferences> applyPreferences)
    {
        this.session = session;
        this.preferences = preferences;
        this.preferencesStore = preferencesStore;
        this.workspaceStore = workspaceStore;
        this.recoveryService = recoveryService;
        this.jobs = jobs;
        this.performance = performance;
        this.captureWorkspace = captureWorkspace;
        this.applyWorkspace = applyWorkspace;
        this.applyPreferences = applyPreferences;
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(BuildTabs());
        jobs.Changed += Jobs_Changed;
        session.Changed += (_, _) => RefreshDiagnostics();
        LoadPreferences();
        RefreshAll();
    }

    public void RefreshDocumentState() => RefreshDiagnostics();

    public void RefreshAll()
    {
        RefreshWorkspaces();
        RefreshRecoveries();
        RefreshJobs();
        RefreshPerformance();
        RefreshDiagnostics();
    }

    private TabControl BuildTabs()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildPreferencesTab());
        tabs.TabPages.Add(BuildWorkspaceTab());
        tabs.TabPages.Add(BuildRecoveryTab());
        tabs.TabPages.Add(BuildShortcutsTab());
        tabs.TabPages.Add(BuildJobsTab());
        tabs.TabPages.Add(BuildPerformanceTab());
        tabs.TabPages.Add(BuildDiagnosticsTab());
        return tabs;
    }

    private TabPage BuildPreferencesTab()
    {
        foreach (SpriteUiTheme theme in Enum.GetValues<SpriteUiTheme>()) themeBox.Items.Add(theme);
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddRow(table, ref row, "Theme", themeBox);
        AddRow(table, ref row, "UI scale (%)", uiScaleBox);
        AddRow(table, ref row, "Font scale (%)", fontScaleBox);
        AddRow(table, ref row, "Autosave interval (seconds)", autosaveBox);
        AddRow(table, ref row, "Undo memory limit (MiB)", undoMemoryBox);
        AddRow(table, ref row, "Recovery snapshots", retentionBox);
        AddRow(table, ref row, "Background export jobs", exportConcurrencyBox);
        AddFullRow(table, ref row, highContrastBox);
        AddFullRow(table, ref row, reduceMotionBox);
        AddFullRow(table, ref row, largeDocumentWarningsBox);
        Button apply = new() { Text = "Apply and Save Preferences", AutoSize = true };
        apply.Click += (_, _) => SavePreferences();
        AddFullRow(table, ref row, apply);
        Label note = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Text = "Changes are stored atomically. Undo, autosave, display, and reduced-motion settings apply immediately. Background concurrency applies on the next launch." };
        AddFullRow(table, ref row, note);
        TabPage page = new("Preferences"); page.Controls.Add(WrapScrollable(table)); return page;
    }

    private TabPage BuildWorkspaceTab()
    {
        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 240 };
        split.Panel1.Controls.Add(workspaceList);
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddRow(table, ref row, "Workspace name", workspaceNameBox);
        AddFullRow(table, ref row, mirrorWorkspaceBox);
        FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill };
        Button save = new() { Text = "Save Current", AutoSize = true };
        Button apply = new() { Text = "Apply Selected", AutoSize = true };
        Button remove = new() { Text = "Delete Selected", AutoSize = true };
        Button reset = new() { Text = "Apply Default", AutoSize = true };
        save.Click += (_, _) => SaveWorkspace(); apply.Click += (_, _) => ApplySelectedWorkspace(); remove.Click += (_, _) => DeleteWorkspace(); reset.Click += (_, _) => applyWorkspace(SpriteWorkspaceProfileStore.Default());
        buttons.Controls.AddRange([save, apply, remove, reset]);
        AddFullRow(table, ref row, buttons);
        split.Panel2.Controls.Add(WrapScrollable(table));
        workspaceList.SelectedIndexChanged += (_, _) => SelectWorkspace();
        TabPage page = new("Workspaces"); page.Controls.Add(split); return page;
    }

    private TabPage BuildRecoveryTab()
    {
        recoveryList.Columns.Add("Captured", 150); recoveryList.Columns.Add("Document", 160); recoveryList.Columns.Add("Reason", 90); recoveryList.Columns.Add("Size", 80); recoveryList.Columns.Add("Original", 260);
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 42 };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill };
        Button capture = new() { Text = "Capture Now", AutoSize = true };
        Button restore = new() { Text = "Restore Selected", AutoSize = true };
        Button delete = new() { Text = "Delete Selected", AutoSize = true };
        Button refresh = new() { Text = "Refresh", AutoSize = true };
        capture.Click += async (_, _) => await CaptureRecoveryAsync("manual"); restore.Click += async (_, _) => await RestoreSelectedRecoveryAsync(); delete.Click += (_, _) => DeleteSelectedRecovery(); refresh.Click += (_, _) => RefreshRecoveries();
        buttons.Controls.AddRange([capture, restore, delete, refresh]); footer.Controls.Add(buttons);
        TabPage page = new("Recovery"); page.Controls.Add(recoveryList); page.Controls.Add(footer); return page;
    }

    private TabPage BuildShortcutsTab()
    {
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Command", DataPropertyName = "Command", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 45 });
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gesture", DataPropertyName = "Gesture", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Scope", DataPropertyName = "Scope", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 15 });
        shortcutGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Enabled", DataPropertyName = "Enabled", Width = 70 });
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 42 };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill };
        Button save = new() { Text = "Validate and Save", AutoSize = true };
        Button reset = new() { Text = "Reset Defaults", AutoSize = true };
        save.Click += (_, _) => SaveShortcuts(); reset.Click += (_, _) => { preferences.Shortcuts = SpriteEditorPreferences.DefaultShortcuts(); LoadShortcutRows(); };
        buttons.Controls.AddRange([save, reset]); footer.Controls.Add(buttons);
        TabPage page = new("Shortcuts"); page.Controls.Add(shortcutGrid); page.Controls.Add(footer); return page;
    }

    private TabPage BuildJobsTab()
    {
        jobList.Columns.Add("Job", 210); jobList.Columns.Add("Status", 90); jobList.Columns.Add("Progress", 80); jobList.Columns.Add("Message", 280); jobList.Columns.Add("Attempt", 70);
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 42 };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill };
        Button cancel = new() { Text = "Cancel Selected", AutoSize = true }; Button clear = new() { Text = "Clear Finished", AutoSize = true };
        cancel.Click += (_, _) => CancelSelectedJob(); clear.Click += (_, _) => jobs.ClearFinished(); buttons.Controls.AddRange([cancel, clear]); footer.Controls.Add(buttons);
        TabPage page = new("Background Jobs"); page.Controls.Add(jobList); page.Controls.Add(footer); return page;
    }

    private TabPage BuildPerformanceTab()
    {
        performanceList.Columns.Add("Operation", 220); performanceList.Columns.Add("Samples", 70); performanceList.Columns.Add("Average ms", 100); performanceList.Columns.Add("Maximum ms", 100); performanceList.Columns.Add("Peak managed", 120);
        Panel footer = new() { Dock = DockStyle.Bottom, Height = 42 };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill };
        Button refresh = new() { Text = "Refresh", AutoSize = true }; Button clear = new() { Text = "Clear", AutoSize = true };
        refresh.Click += (_, _) => RefreshPerformance(); clear.Click += (_, _) => { performance.Clear(); RefreshPerformance(); }; buttons.Controls.AddRange([refresh, clear]); footer.Controls.Add(buttons);
        TabPage page = new("Performance"); page.Controls.Add(performanceList); page.Controls.Add(footer); return page;
    }

    private TabPage BuildDiagnosticsTab()
    {
        TabPage page = new("Diagnostics"); page.Controls.Add(diagnosticsBox); return page;
    }

    private void LoadPreferences()
    {
        themeBox.SelectedItem = preferences.Theme;
        uiScaleBox.Value = ClampDecimal((decimal)(preferences.UiScale * 100d), uiScaleBox.Minimum, uiScaleBox.Maximum);
        fontScaleBox.Value = ClampDecimal((decimal)(preferences.FontScale * 100d), fontScaleBox.Minimum, fontScaleBox.Maximum);
        autosaveBox.Value = ClampDecimal(preferences.AutosaveSeconds, autosaveBox.Minimum, autosaveBox.Maximum);
        undoMemoryBox.Value = ClampDecimal(preferences.UndoMemoryLimitBytes / 1024m / 1024m, undoMemoryBox.Minimum, undoMemoryBox.Maximum);
        retentionBox.Value = ClampDecimal(preferences.RecoverySnapshotsPerDocument, retentionBox.Minimum, retentionBox.Maximum);
        exportConcurrencyBox.Value = ClampDecimal(preferences.BackgroundExportConcurrency, exportConcurrencyBox.Minimum, exportConcurrencyBox.Maximum);
        highContrastBox.Checked = preferences.HighContrast;
        reduceMotionBox.Checked = preferences.ReduceMotion;
        largeDocumentWarningsBox.Checked = preferences.WarnOnLargeDocuments;
        LoadShortcutRows();
    }

    private void SavePreferences()
    {
        ReadPreferenceControls();
        preferencesStore.Save(preferences);
        applyPreferences(preferences);
        RefreshDiagnostics();
    }

    private void ReadPreferenceControls()
    {
        preferences.Theme = themeBox.SelectedItem is SpriteUiTheme theme ? theme : SpriteUiTheme.System;
        preferences.UiScale = Decimal.ToDouble(uiScaleBox.Value) / 100d;
        preferences.FontScale = Decimal.ToDouble(fontScaleBox.Value) / 100d;
        preferences.AutosaveSeconds = Decimal.ToInt32(autosaveBox.Value);
        preferences.UndoMemoryLimitBytes = Decimal.ToInt64(undoMemoryBox.Value) * 1024L * 1024L;
        preferences.RecoverySnapshotsPerDocument = Decimal.ToInt32(retentionBox.Value);
        preferences.BackgroundExportConcurrency = Decimal.ToInt32(exportConcurrencyBox.Value);
        preferences.HighContrast = highContrastBox.Checked;
        preferences.ReduceMotion = reduceMotionBox.Checked;
        preferences.WarnOnLargeDocuments = largeDocumentWarningsBox.Checked;
    }

    private void RefreshWorkspaces()
    {
        workspaces = workspaceStore.LoadAll();
        workspaceList.BeginUpdate(); try { workspaceList.Items.Clear(); foreach (SpriteWorkspaceProfile workspace in workspaces) workspaceList.Items.Add(workspace.Name); } finally { workspaceList.EndUpdate(); }
    }

    private void SelectWorkspace()
    {
        if (workspaceList.SelectedIndex < 0 || workspaceList.SelectedIndex >= workspaces.Count) return;
        SpriteWorkspaceProfile workspace = workspaces[workspaceList.SelectedIndex];
        workspaceNameBox.Text = workspace.Name; mirrorWorkspaceBox.Checked = workspace.MirrorInspector;
    }

    private void SaveWorkspace()
    {
        SpriteWorkspaceProfile profile = captureWorkspace();
        string name = string.IsNullOrWhiteSpace(workspaceNameBox.Text) ? "Workspace" : workspaceNameBox.Text.Trim();
        profile.Name = name; profile.Id = MakeId(name); profile.MirrorInspector = mirrorWorkspaceBox.Checked;
        workspaceStore.Save(profile); preferences.ActiveWorkspaceId = profile.Id; preferencesStore.Save(preferences); RefreshWorkspaces();
    }

    private void ApplySelectedWorkspace()
    {
        if (workspaceList.SelectedIndex < 0 || workspaceList.SelectedIndex >= workspaces.Count) return;
        SpriteWorkspaceProfile workspace = workspaces[workspaceList.SelectedIndex];
        applyWorkspace(workspace); preferences.ActiveWorkspaceId = workspace.Id; preferencesStore.Save(preferences);
    }

    private void DeleteWorkspace()
    {
        if (workspaceList.SelectedIndex < 0 || workspaceList.SelectedIndex >= workspaces.Count) return;
        SpriteWorkspaceProfile workspace = workspaces[workspaceList.SelectedIndex];
        if (workspace.Id == "default") return;
        workspaceStore.Delete(workspace.Id); RefreshWorkspaces();
    }

    private async Task CaptureRecoveryAsync(string reason)
    {
        try
        {
            await recoveryService.CaptureAsync(session.Package, session.PackagePath, session.ExportPath, reason, preferences.RecoverySnapshotsPerDocument);
            RefreshRecoveries();
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Recovery capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshRecoveries()
    {
        recoveries = recoveryService.ListSnapshots();
        recoveryList.BeginUpdate(); try
        {
            recoveryList.Items.Clear();
            foreach (SpriteRecoverySnapshot recovery in recoveries)
            {
                ListViewItem item = new(recovery.CapturedAt.LocalDateTime.ToString("g")) { Tag = recovery };
                item.SubItems.Add(recovery.DocumentName); item.SubItems.Add(recovery.Reason); item.SubItems.Add(FormatBytes(recovery.PackageBytes)); item.SubItems.Add(recovery.OriginalPackagePath ?? string.Empty);
                recoveryList.Items.Add(item);
            }
        }
        finally { recoveryList.EndUpdate(); }
    }

    private async Task RestoreSelectedRecoveryAsync()
    {
        if (recoveryList.SelectedItems.Count == 0 || recoveryList.SelectedItems[0].Tag is not SpriteRecoverySnapshot recovery) return;
        if (session.IsDirty && MessageBox.Show(this, "Restoring will replace the current in-memory document. Continue?", "Restore recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            SpritePackageData package = await recoveryService.LoadAsync(recovery);
            session.ReplacePackage(package, recovery.OriginalPackagePath, recovery.RuntimeExportPath, dirty: true);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Recovery restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeleteSelectedRecovery()
    {
        if (recoveryList.SelectedItems.Count == 0 || recoveryList.SelectedItems[0].Tag is not SpriteRecoverySnapshot recovery) return;
        recoveryService.Delete(recovery); RefreshRecoveries();
    }

    private void LoadShortcutRows()
    {
        shortcutGrid.Rows.Clear();
        foreach (SpriteShortcutBinding shortcut in preferences.Shortcuts.OrderBy(item => item.Command))
            shortcutGrid.Rows.Add(shortcut.Command, shortcut.Gesture, shortcut.Scope, shortcut.Enabled);
    }

    private void SaveShortcuts()
    {
        List<SpriteShortcutBinding> shortcuts = [];
        foreach (DataGridViewRow row in shortcutGrid.Rows)
        {
            shortcuts.Add(new SpriteShortcutBinding
            {
                Command = Convert.ToString(row.Cells[0].Value) ?? string.Empty,
                Gesture = Convert.ToString(row.Cells[1].Value) ?? string.Empty,
                Scope = Convert.ToString(row.Cells[2].Value) ?? "global",
                Enabled = Convert.ToBoolean(row.Cells[3].Value ?? true)
            });
        }
        IReadOnlyList<string> issues = SpriteShortcutRegistry.Validate(shortcuts);
        if (issues.Count > 0) { MessageBox.Show(this, string.Join(Environment.NewLine, issues), "Shortcut conflicts", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        preferences.Shortcuts = shortcuts; preferencesStore.Save(preferences); applyPreferences(preferences); RefreshDiagnostics();
    }

    private void Jobs_Changed(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke((Action)RefreshJobs); } catch (InvalidOperationException) { }
    }

    private void RefreshJobs()
    {
        jobList.BeginUpdate(); try
        {
            jobList.Items.Clear();
            foreach (SpriteBackgroundJobInfo job in jobs.Jobs)
            {
                ListViewItem item = new(job.Name) { Tag = job.Id };
                item.SubItems.Add(job.Status.ToString()); item.SubItems.Add(job.ProgressPercent + "%"); item.SubItems.Add(job.Message); item.SubItems.Add(job.Attempt.ToString());
                jobList.Items.Add(item);
            }
        }
        finally { jobList.EndUpdate(); }
    }

    private void CancelSelectedJob()
    {
        if (jobList.SelectedItems.Count == 0 || jobList.SelectedItems[0].Tag is not Guid id) return;
        jobs.Cancel(id);
    }

    private void RefreshPerformance()
    {
        performanceList.BeginUpdate(); try
        {
            performanceList.Items.Clear();
            foreach (SpritePerformanceSummary summary in performance.Summaries)
            {
                ListViewItem item = new(summary.Operation); item.SubItems.Add(summary.Samples.ToString()); item.SubItems.Add(summary.AverageMilliseconds.ToString("0.00")); item.SubItems.Add(summary.MaximumMilliseconds.ToString("0.00")); item.SubItems.Add(FormatBytes(summary.MaximumManagedBytes)); performanceList.Items.Add(item);
            }
        }
        finally { performanceList.EndUpdate(); }
    }

    private void RefreshDiagnostics()
    {
        SpriteLargeDocumentAssessment large = SpriteLargeDocumentPolicy.Assess(session.Package, preferences.LargeDocumentPixelThreshold);
        List<string> lines =
        [
            "Playroma Sprite Studio diagnostics",
            $"Document: {session.Document.Name} ({session.Document.Canvas.Width}×{session.Document.Canvas.Height}, {session.Document.Frames.Count} frames, {session.Document.Layers.Count} layers)",
            $"Estimated document memory: {FormatBytes(large.EstimatedBytes)}",
            $"Undo history: {session.History.UndoCount} undo / {session.History.RedoCount} redo, {FormatBytes(session.History.MemoryBytes)} of {FormatBytes(session.History.MemoryLimitBytes)}",
            $"Recovery snapshots: {recoveries.Count(item => item.DocumentId == session.Document.Id)}",
            $"Background jobs: {jobs.Jobs.Count}",
            $"Large-document policy: {(large.IsLarge ? "ACTIVE" : "normal")}",
            string.Empty
        ];
        lines.AddRange(large.Recommendations.Select(item => "Recommendation: " + item));
        foreach (SpriteAccessibilityIssue issue in SpriteAccessibilityAuditor.Audit(preferences)) lines.Add($"{issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
        diagnosticsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static string MakeId(string name) => new(name.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024L * 1024L ? $"{bytes / 1024d / 1024d / 1024d:0.00} GiB" : bytes >= 1024L * 1024L ? $"{bytes / 1024d / 1024d:0.0} MiB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KiB" : bytes + " B";
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill };
    private static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, Dock = DockStyle.Fill };
    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum) => Math.Min(maximum, Math.Max(minimum, value));
    private static TableLayoutPanel PropertyTable() => new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8), GrowStyle = TableLayoutPanelGrowStyle.AddRows, ColumnStyles = { new ColumnStyle(SizeType.Percent, 45), new ColumnStyle(SizeType.Percent, 55) } };
    private static Control WrapScrollable(Control content) { Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true }; panel.Controls.Add(content); return panel; }
    private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 4) }, 0, row); table.Controls.Add(control, 1, row); row++; }
    private static void AddFullRow(TableLayoutPanel table, ref int row, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); row++; }
}

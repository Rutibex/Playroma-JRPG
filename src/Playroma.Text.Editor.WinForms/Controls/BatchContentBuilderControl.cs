using System.ComponentModel;
using Playroma.Text.Core.Batch;
using Playroma.Text.RpgMakerMV;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class BatchContentBuilderControl : UserControl
{
    private readonly TextStudioSession _studio;
    private readonly BatchContentBuilderSession _batch;
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox _recipe = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox _field = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 999, Value = 20, Width = 65 };
    private readonly TextBox _filter = new() { PlaceholderText = "Filter rows…", Width = 180 };
    private readonly ToolStripStatusLabel _status = new("Ready");
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        MultiSelect = true,
        RowHeadersVisible = false,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
    };
    private readonly Button _generate = new() { Text = "Generate Batch", AutoSize = true };
    private readonly Button _regenRows = new() { Text = "Regenerate Rows", AutoSize = true };
    private readonly Button _regenCells = new() { Text = "Regenerate Cells", AutoSize = true };
    private readonly Button _lockRows = new() { Text = "Lock Rows", AutoSize = true };
    private readonly Button _lockCells = new() { Text = "Lock Cells", AutoSize = true };
    private readonly Button _validate = new() { Text = "Validate", AutoSize = true };
    private readonly Button _import = new() { Text = "Import Batch…", AutoSize = true };
    private readonly Button _exportCsv = new() { Text = "Export CSV", AutoSize = true };
    private readonly Button _importCsv = new() { Text = "Import CSV", AutoSize = true };
    private bool _refreshing;

    public BatchContentBuilderControl(TextStudioSession studio)
    {
        _studio = studio ?? throw new ArgumentNullException(nameof(studio));
        _batch = new BatchContentBuilderSession(studio, studio.Options.BatchHost);
        Dock = DockStyle.Fill;
        BuildUi();
        PopulateKinds();
        PopulateModes();
        PopulateFields();
        PopulateRecipes();
        WireEvents();
        UpdateEnabledState();
    }

    public event EventHandler<TextStudioStatusChangedEventArgs>? StatusChanged;

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        toolbar.Controls.AddRange([
            new Label { Text = "Database", AutoSize = true, Margin = new Padding(0, 8, 2, 0) }, _kind,
            new Label { Text = "Mode", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, _mode,
            new Label { Text = "Field", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, _field,
            new Label { Text = "Recipe", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, _recipe,
            new Label { Text = "Rows", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, _count,
            _generate, _regenRows, _regenCells, _lockRows, _lockCells, _validate, _import
        ]);
        var second = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6, 0, 6, 6) };
        second.Controls.AddRange([
            new Label { Text = "Filter", AutoSize = true, Margin = new Padding(0, 8, 2, 0) }, _filter,
            _exportCsv, _importCsv,
            new Label { Text = "Tip: select cells and use Lock Cells to freeze good results while regenerating.", AutoSize = true, Margin = new Padding(12, 8, 0, 0) }
        ]);
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        gridPanel.Controls.Add(_grid);

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        statusStrip.Items.Add(_status);
        gridPanel.Controls.Add(statusStrip);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(second, 0, 1);
        root.Controls.Add(gridPanel, 0, 2);
        Controls.Add(root);
    }

    private void PopulateKinds()
    {
        _kind.Items.Clear();
        string[] order = ["Actors", "Classes", "Skills", "Items", "Weapons", "Armors", "Enemies", "Troops", "States", "Animations", "Tilesets", "CommonEvents"];
        foreach (string item in order.Where(MvDatabaseTextTargetCatalog.SupportsKind)) _kind.Items.Add(item);
        _kind.Items.Add("Maps");
        if (_kind.Items.Count > 0) _kind.SelectedIndex = Math.Min(2, _kind.Items.Count - 1);
    }

    private void PopulateModes()
    {
        _mode.DataSource = new[]
        {
            new ModeItem(BatchContentMode.FillBlankExisting, "Fill blank records"),
            new ModeItem(BatchContentMode.UpdateExisting, "Update existing"),
            new ModeItem(BatchContentMode.AppendNew, "Append new records")
        };
        _mode.DisplayMember = nameof(ModeItem.Label);
    }

    private void PopulateRecipes()
    {
        string? selectedId = (_recipe.SelectedItem as TextStudioRecipeEntry)?.Recipe.Id;
        TextStudioRecipeEntry[] entries = _studio.Recipes.OrderBy(r => r.Category).ThenBy(r => r.DisplayName).ToArray();
        _recipe.DataSource = entries;
        _recipe.DisplayMember = nameof(TextStudioRecipeEntry.DisplayName);
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            int index = Array.FindIndex(entries, x => x.Recipe.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _recipe.SelectedIndex = index;
        }
        PickRecommendedRecipe();
    }

    private void PopulateFields()
    {
        string? previous = (_field.SelectedItem as FieldItem)?.SemanticField;
        _field.Items.Clear();
        if (_kind.SelectedItem is not string kind) return;
        foreach (MvTextFieldSpec spec in GetSpecs(kind)) _field.Items.Add(new FieldItem(spec.SemanticField, spec.DisplayName));
        if (_field.Items.Count == 0) return;
        _field.SelectedItem = _field.Items.Cast<FieldItem>().FirstOrDefault(x => x.SemanticField.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? _field.Items[0];
    }

    private void PickRecommendedRecipe()
    {
        if (_kind.SelectedItem is not string kind || _recipe.DataSource is not TextStudioRecipeEntry[] entries) return;
        string? semantic = (_field.SelectedItem as FieldItem)?.SemanticField;
        MvTextFieldSpec? spec = GetSpecs(kind).FirstOrDefault(x => x.SemanticField.Equals(semantic, StringComparison.OrdinalIgnoreCase)) ?? GetSpecs(kind).FirstOrDefault();
        string? recommended = spec?.RecommendedRecipeIds?.FirstOrDefault(id => entries.Any(r => r.Recipe.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        if (recommended is null) return;
        int index = Array.FindIndex(entries, x => x.Recipe.Id.Equals(recommended, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _recipe.SelectedIndex = index;
    }

    private void WireEvents()
    {
        _batch.RowsChanged += (_, _) => BeginInvokeIfNeeded(RefreshGrid);
        _studio.RecipesChanged += (_, _) => BeginInvokeIfNeeded(PopulateRecipes);
        _kind.SelectedIndexChanged += (_, _) => { PopulateFields(); PickRecommendedRecipe(); ClearGrid(); };
        _field.SelectedIndexChanged += (_, _) => PickRecommendedRecipe();
        _mode.SelectedIndexChanged += (_, _) => ClearGrid();
        _filter.TextChanged += (_, _) => ApplyFilter();
        _generate.Click += async (_, _) => await GenerateAsync();
        _regenRows.Click += async (_, _) => await RegenerateRowsAsync();
        _regenCells.Click += async (_, _) => await RegenerateCellsAsync();
        _lockRows.Click += (_, _) => ToggleSelectedRows();
        _lockCells.Click += (_, _) => ToggleSelectedCells();
        _validate.Click += (_, _) => ShowValidation();
        _import.Click += async (_, _) => await ImportBatchAsync();
        _exportCsv.Click += (_, _) => ExportCsv();
        _importCsv.Click += (_, _) => ImportCsv();
        _grid.CellValueChanged += GridCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    private async Task GenerateAsync()
    {
        if (!_batch.IsHostAvailable) { Report("Batch database integration is unavailable in this host.", true); return; }
        if (_kind.SelectedItem is not string kind || _recipe.SelectedItem is not TextStudioRecipeEntry recipe || _mode.SelectedItem is not ModeItem mode) return;
        SetBusy(true, "Generating batch…");
        try
        {
            if (kind.Equals("Maps", StringComparison.OrdinalIgnoreCase) && mode.Mode == BatchContentMode.AppendNew) throw new InvalidOperationException("Batch Content Builder can fill/update map text, but map creation remains in the Map editor.");
            IReadOnlyList<MvTextFieldSpec> specs = GetSpecs(kind);
            string primary = (_field.SelectedItem as FieldItem)?.SemanticField ?? (specs.Any(f => f.SemanticField.Equals("name", StringComparison.OrdinalIgnoreCase)) ? "name" : specs.First().SemanticField);
            string adapter = kind.Equals("Maps", StringComparison.OrdinalIgnoreCase) ? "map" : "database." + kind.ToLowerInvariant();
            await _batch.PrepareAndGenerateAsync(adapter, recipe.Recipe.Id, mode.Mode, (int)_count.Value, primary, _studio.Seed).ConfigureAwait(true);
            Report($"Generated {_batch.Rows.Count:N0} {kind} row(s).", false);
        }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private async Task RegenerateRowsAsync()
    {
        Guid[] ids = SelectedRowIds();
        if (ids.Length == 0) return;
        SetBusy(true, "Regenerating rows…");
        try { await _batch.RegenerateRowsAsync(ids).ConfigureAwait(true); Report($"Regenerated {ids.Length:N0} selected row(s).", false); }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private async Task RegenerateCellsAsync()
    {
        var cells = SelectedDataCells();
        if (cells.Count == 0) return;
        SetBusy(true, "Regenerating cells…");
        try { await _batch.RegenerateCellsAsync(cells).ConfigureAwait(true); Report($"Regenerated {cells.Count:N0} selected cell(s).", false); }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private async Task ImportBatchAsync()
    {
        if (_batch.Rows.Count == 0) return;
        SetBusy(true, "Validating batch import…");
        try
        {
            TextBatchImportPlan plan = await _batch.BuildImportPlanAsync().ConfigureAwait(true);
            using var dialog = new BatchImportPreviewDialog(plan);
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            await _batch.ApplyImportAsync(plan).ConfigureAwait(true);
            Report($"Imported {plan.Rows.Count:N0} rows / {plan.Changes.Count:N0} text changes as one undoable transaction.", false);
            _batch.Reset();
        }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private void ToggleSelectedRows()
    {
        foreach (Guid id in SelectedRowIds()) _batch.ToggleRowLock(id);
    }

    private void ToggleSelectedCells()
    {
        foreach ((Guid row, string field) in SelectedDataCells()) _batch.ToggleCellLock(row, field);
    }

    private void ShowValidation()
    {
        BatchValidationSummary summary = _batch.Validate();
        string message = $"Rows: {summary.RowCount:N0}\r\nWarning rows: {summary.WarningRows:N0}\r\nError rows: {summary.ErrorRows:N0}\r\nDuplicates/collisions: {summary.DuplicateCount:N0}";
        if (summary.Messages.Count > 0) message += "\r\n\r\n" + string.Join("\r\n", summary.Messages.Take(20));
        MessageBox.Show(this, message, "Batch Validation", MessageBoxButtons.OK, summary.ErrorRows > 0 ? MessageBoxIcon.Error : summary.WarningRows > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void ExportCsv()
    {
        if (_batch.Rows.Count == 0) return;
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", FileName = "playroma-text-batch.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, _batch.ExportCsv(), new System.Text.UTF8Encoding(true));
        Report($"Exported {_batch.Rows.Count:N0} rows to CSV.", false);
    }

    private void ImportCsv()
    {
        if (_batch.Rows.Count == 0) { Report("Generate or prepare a batch before importing CSV values.", true); return; }
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _batch.ImportCsv(File.ReadAllText(dialog.FileName));
        Report("Imported CSV values into the current batch grid.", false);
    }

    private void RefreshGrid()
    {
        _refreshing = true;
        try
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "rowLock", HeaderText = "Lock", Width = 45, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "recordId", HeaderText = "ID", Width = 55, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "target", HeaderText = "Target", Width = 190, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
            string[] semantics = _batch.Rows.SelectMany(r => r.Cells.Select(c => c.SemanticField)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string semantic in semantics)
            {
                string display = _batch.Rows.SelectMany(r => r.Cells).First(c => c.SemanticField.Equals(semantic, StringComparison.OrdinalIgnoreCase)).DisplayName;
                _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "field:" + semantic, HeaderText = display, Width = display.Contains("Description", StringComparison.OrdinalIgnoreCase) || display.Contains("Note", StringComparison.OrdinalIgnoreCase) ? 280 : 180, SortMode = DataGridViewColumnSortMode.Automatic });
            }
            foreach (BatchContentRow row in _batch.Rows)
            {
                int ri = _grid.Rows.Add();
                DataGridViewRow gridRow = _grid.Rows[ri];
                gridRow.Tag = row.Id;
                gridRow.Cells["rowLock"].Value = row.IsRowLocked;
                gridRow.Cells["recordId"].Value = row.RecordId?.ToString() ?? "—";
                gridRow.Cells["target"].Value = row.TargetDisplayName;
                gridRow.Cells["status"].Value = row.Status;
                gridRow.Cells["status"].ToolTipText = string.Join("\r\n", row.EffectiveWarnings);
                foreach (BatchCellState cell in row.Cells)
                {
                    DataGridViewCell gridCell = gridRow.Cells["field:" + cell.SemanticField];
                    gridCell.Value = cell.Value;
                    gridCell.ReadOnly = cell.IsLocked || row.IsRowLocked;
                    gridCell.ToolTipText = (cell.IsLocked ? "🔒 Locked. " : string.Empty) + string.Join("\r\n", cell.EffectiveWarnings);
                }
            }
            ApplyFilter();
            _status.Text = $"{_batch.Rows.Count:N0} row(s)";
        }
        finally { _refreshing = false; UpdateEnabledState(); }
    }

    private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        DataGridViewRow row = _grid.Rows[e.RowIndex];
        if (row.Tag is not Guid id) return;
        string name = _grid.Columns[e.ColumnIndex].Name;
        if (name == "rowLock") { _batch.ToggleRowLock(id); return; }
        if (!name.StartsWith("field:", StringComparison.OrdinalIgnoreCase)) return;
        string semantic = name["field:".Length..];
        _batch.SetCellValue(id, semantic, Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? string.Empty);
    }

    private Guid[] SelectedRowIds() => _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => _grid.Rows[c.RowIndex].Tag).OfType<Guid>().Distinct().ToArray();

    private List<(Guid RowId, string SemanticField)> SelectedDataCells()
    {
        var result = new List<(Guid, string)>();
        foreach (DataGridViewCell cell in _grid.SelectedCells)
        {
            if (_grid.Rows[cell.RowIndex].Tag is not Guid id) continue;
            string name = _grid.Columns[cell.ColumnIndex].Name;
            if (name.StartsWith("field:", StringComparison.OrdinalIgnoreCase)) result.Add((id, name["field:".Length..]));
        }
        return result.Distinct().ToList();
    }

    private void ApplyFilter()
    {
        string filter = _filter.Text.Trim();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            bool visible = filter.Length == 0 || row.Cells.Cast<DataGridViewCell>().Any(c => (Convert.ToString(c.Value) ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase));
            try { row.Visible = visible; } catch (InvalidOperationException) { }
        }
    }

    private void ClearGrid()
    {
        _grid.Columns.Clear(); _grid.Rows.Clear(); _status.Text = "Ready"; UpdateEnabledState();
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _generate.Enabled = !busy && _batch.IsHostAvailable;
        _regenRows.Enabled = !busy && _batch.Rows.Count > 0;
        _regenCells.Enabled = !busy && _batch.Rows.Count > 0;
        _import.Enabled = !busy && _batch.Rows.Count > 0;
        _validate.Enabled = !busy && _batch.Rows.Count > 0;
        if (!string.IsNullOrWhiteSpace(text)) _status.Text = text;
        UseWaitCursor = busy;
    }

    private void UpdateEnabledState()
    {
        bool rows = _batch.Rows.Count > 0;
        _generate.Enabled = _batch.IsHostAvailable;
        _regenRows.Enabled = rows; _regenCells.Enabled = rows; _lockRows.Enabled = rows; _lockCells.Enabled = rows;
        _validate.Enabled = rows; _import.Enabled = rows && _batch.IsHostAvailable; _exportCsv.Enabled = rows; _importCsv.Enabled = rows;
        if (!_batch.IsHostAvailable) _status.Text = "Batch database integration is unavailable in this host.";
    }

    private static IReadOnlyList<MvTextFieldSpec> GetSpecs(string kind) =>
        kind.Equals("Maps", StringComparison.OrdinalIgnoreCase) ? MvMapTextTargets.Specs : MvDatabaseTextTargetCatalog.ForKind(kind);

    private void Report(string message, bool error)
    {
        _status.Text = message;
        StatusChanged?.Invoke(this, new TextStudioStatusChangedEventArgs(message, error));
    }

    private void BeginInvokeIfNeeded(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private sealed record FieldItem(string SemanticField, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ModeItem(BatchContentMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class BatchImportPreviewDialog : Form
{
    public BatchImportPreviewDialog(TextBatchImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Text = "Batch Import Preview";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(850, 600);
        Size = new Size(1100, 720);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var changesPage = new TabPage("Changes");
        var issuesPage = new TabPage("Validation");
        var changes = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        changes.Columns.Add("row", "Row"); changes.Columns.Add("file", "File"); changes.Columns.Add("record", "Record"); changes.Columns.Add("field", "Field"); changes.Columns.Add("old", "Current"); changes.Columns.Add("new", "Generated");
        foreach (TextBatchImportRowPlan row in plan.Rows)
            foreach (TextImportChange change in row.Plan.Changes)
                changes.Rows.Add(row.RowLabel, change.DataFile, change.RecordId, change.DisplayName ?? change.SemanticField, change.OldValue, change.NewValue);
        var issues = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = plan.Issues.Count == 0 ? "No validation issues." : string.Join(Environment.NewLine, plan.Issues.Select(i => $"{(i.IsError ? "ERROR" : "WARN")} {i.Code}: {i.Message}")) };
        changesPage.Controls.Add(changes); issuesPage.Controls.Add(issues); tabs.TabPages.AddRange([changesPage, issuesPage]);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(6) };
        var apply = new Button { Text = $"Import {plan.Rows.Count:N0} Rows", DialogResult = DialogResult.OK, AutoSize = true, Enabled = plan.CanApply };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var summary = new Label { Text = $"{plan.Creations.Count:N0} new record(s), {plan.Changes.Count:N0} field change(s), {plan.Issues.Count(i => i.IsError):N0} error(s)", AutoSize = true, Margin = new Padding(12, 9, 12, 0) };
        bottom.Controls.Add(apply); bottom.Controls.Add(cancel); bottom.Controls.Add(summary);
        Controls.Add(tabs); Controls.Add(bottom); AcceptButton = apply; CancelButton = cancel;
    }
}

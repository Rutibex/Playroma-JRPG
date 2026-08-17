using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.Text.Core.Generation;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

/// <summary>Field-by-field preview for generated database content before it becomes a transaction.</summary>
internal sealed class TextStudioImportPreviewDialog : Form
{
    private readonly TextStudioDatabaseIntegrationService _service;
    private readonly TextRecordTargetDescriptor _target;
    private readonly GeneratedTextBundle _bundle;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false };
    private readonly TextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly CheckBox _create = new() { Text = "Create as a new database record", AutoSize = true };
    private readonly NumericUpDown _newId = new() { Minimum = 1, Maximum = 999999, Width = 100 };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 7, 8, 0) };
    private readonly Button _apply = new() { Text = "Import", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public TextStudioImportPreviewDialog(TextStudioDatabaseIntegrationService service, TextRecordTargetDescriptor target, GeneratedTextBundle bundle)
    {
        _service = service; _target = target; _bundle = bundle;
        Text = "Import Generated Text — " + target.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = true; ShowInTaskbar = false;
        Size = new Size(980, 690); MinimumSize = new Size(760, 520);
        BuildGrid();
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        split.Panel1.Controls.Add(_grid); split.Panel2.Controls.Add(_preview);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 5, 8, 4), WrapContents = false };
        top.Controls.Add(_create); top.Controls.Add(new Label { Text = "New ID", AutoSize = true, Margin = new Padding(12, 7, 3, 0) }); top.Controls.Add(_newId); top.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        buttons.Controls.Add(_apply); buttons.Controls.Add(_cancel);
        Controls.Add(split); Controls.Add(top); Controls.Add(buttons);
        AcceptButton = _apply; CancelButton = _cancel;
        _create.Enabled = target.CanCreateRecord;
        _create.Checked = false;
        _newId.Enabled = false;
        _newId.Value = target.SuggestedCreateRecordId ?? 1;
        _create.CheckedChanged += async (_, _) => { _newId.Enabled = _create.Checked; await RefreshPreviewAsync().ConfigureAwait(true); };
        _newId.ValueChanged += async (_, _) => await RefreshPreviewAsync().ConfigureAwait(true);
        _grid.CellValueChanged += async (_, _) => await RefreshPreviewAsync().ConfigureAwait(true);
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _apply.Click += async (_, _) => await AcceptAsync().ConfigureAwait(true);
        Shown += async (_, _) => await RefreshPreviewAsync().ConfigureAwait(true);
    }

    public TextImportPlan? Plan { get; private set; }

    private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Import", Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Database field", ReadOnly = true, Width = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Generated", HeaderText = "Generated value", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var modes = new DataGridViewComboBoxColumn { Name = "Mode", HeaderText = "Mode", Width = 125, DataSource = new[] { TextImportMode.Replace, TextImportMode.FillIfBlank, TextImportMode.Append, TextImportMode.Prepend } };
        _grid.Columns.Add(modes);
        foreach (TextTargetDescriptor field in _target.Fields)
        {
            string generated = TextImportPlanner.ResolveGeneratedText(_bundle, null, field, _target.SelectedField);
            bool explicitGenerated = _bundle.Fields.Any(x => x.Name.Equals(field.SemanticField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.SemanticRole, field.SemanticField, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(field.FieldKey) && x.Name.Equals(field.FieldKey, StringComparison.OrdinalIgnoreCase)));
            bool include = field.SemanticField.Equals(_target.SelectedSemanticField, StringComparison.OrdinalIgnoreCase) || explicitGenerated;
            int row = _grid.Rows.Add(include, field.DisplayName, generated, TextImportMode.Replace);
            _grid.Rows[row].Tag = field;
        }
    }

    private IReadOnlyList<TextImportFieldSelection> CaptureSelections()
    {
        var list = new List<TextImportFieldSelection>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not TextTargetDescriptor field) continue;
            bool include = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
            TextImportMode mode = row.Cells["Mode"].Value is TextImportMode parsed ? parsed : TextImportMode.Replace;
            list.Add(new TextImportFieldSelection(field.SemanticField, include, mode));
        }
        return list;
    }

    private async Task RefreshPreviewAsync()
    {
        try
        {
            Plan = await _service.BuildPlanAsync(_target, _bundle, CaptureSelections(), _create.Checked, _create.Checked ? decimal.ToInt32(_newId.Value) : null).ConfigureAwait(true);
            _apply.Enabled = Plan.CanApply;
            var writer = new System.Text.StringBuilder();
            writer.AppendLine(Plan.Description);
            writer.AppendLine(new string('=', Math.Min(80, Plan.Description.Length)));
            if (Plan.Creation is { } create) writer.AppendLine($"CREATE {create.DisplayName} in {create.DataFile}").AppendLine();
            foreach (TextImportChange change in Plan.Changes)
            {
                writer.AppendLine($"{change.DisplayName ?? change.SemanticField}   {change.DataFile} [{change.RecordId}] {change.JsonPath}");
                writer.AppendLine("- " + PreviewValue(change.OldValue));
                writer.AppendLine("+ " + PreviewValue(change.NewValue));
                writer.AppendLine();
            }
            if (Plan.Issues.Count > 0)
            {
                writer.AppendLine("VALIDATION"); writer.AppendLine("----------");
                foreach (TextImportValidationIssue issue in Plan.Issues)
                    writer.AppendLine((issue.IsError ? "ERROR " : "WARN  ") + issue.Code + ": " + issue.Message);
            }
            _preview.Text = writer.ToString();
            int errors = Plan.Issues.Count(x => x.IsError);
            int warnings = Plan.Issues.Count - errors;
            _status.Text = errors > 0 ? $"{errors} blocking issue(s)" : warnings > 0 ? $"{warnings} warning(s)" : $"{Plan.Changes.Count} field change(s)";
        }
        catch (Exception exception)
        {
            Plan = null; _apply.Enabled = false; _preview.Text = exception.ToString(); _status.Text = "Preview failed";
        }
    }

    private async Task AcceptAsync()
    {
        await RefreshPreviewAsync().ConfigureAwait(true);
        if (Plan?.CanApply != true) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string PreviewValue(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length > 500 ? text[..497] + "…" : text;
    }
}

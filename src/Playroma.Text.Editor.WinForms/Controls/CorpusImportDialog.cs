using Playroma.Text.Authoring.Corpora;

namespace Playroma.Text.Editor.WinForms.Controls;

internal sealed class CorpusImportDialog : Form
{
    private readonly TextBox _author = new() { Dock = DockStyle.Fill };
    private readonly TextBox _license = new() { Dock = DockStyle.Fill, Text = "Specify license/source before publishing" };
    private readonly TextBox _source = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _csvColumn = new() { Minimum = -1, Maximum = 1024, Value = -1, Dock = DockStyle.Left, Width = 80 };
    private readonly TextBox _jsonProperty = new() { Dock = DockStyle.Fill };
    private readonly string _extension;

    public CorpusImportDialog(string path)
    {
        _extension = Path.GetExtension(path).ToLowerInvariant();
        Text = "Import Corpus — " + Path.GetFileName(path);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(560, 260);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 6 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(grid, 0, "Author / creator", _author); Add(grid, 1, "License", _license); Add(grid, 2, "Source URL / note", _source);
        Add(grid, 3, "CSV column (-1 = all)", _csvColumn); Add(grid, 4, "JSON property (blank = all)", _jsonProperty);
        var hint = new Label { Dock = DockStyle.Fill, AutoSize = true, Text = _extension switch
        {
            ".csv" => "Choose a zero-based CSV column, or -1 to collect every non-empty cell.",
            ".json" => "Enter a property name to collect only matching string values, or leave blank to collect all strings.",
            _ => "Text files are imported verbatim; you can normalize and edit them in Corpus Lab."
        } };
        grid.Controls.Add(hint, 0, 5); grid.SetColumnSpan(hint, 2);
        _csvColumn.Enabled = _extension == ".csv"; _jsonProperty.Enabled = _extension == ".json";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        var ok = new Button { Text = "Import", DialogResult = DialogResult.OK, AutoSize = true }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]); AcceptButton = ok; CancelButton = cancel;
        Controls.Add(grid); Controls.Add(buttons);
    }

    public CorpusImportOptions Options => new(
        Author: string.IsNullOrWhiteSpace(_author.Text) ? null : _author.Text.Trim(),
        License: string.IsNullOrWhiteSpace(_license.Text) ? null : _license.Text.Trim(),
        SourceUri: string.IsNullOrWhiteSpace(_source.Text) ? null : _source.Text.Trim(),
        CsvColumnIndex: _extension == ".csv" && _csvColumn.Value >= 0 ? (int)_csvColumn.Value : null,
        JsonPropertyName: _extension == ".json" && !string.IsNullOrWhiteSpace(_jsonProperty.Text) ? _jsonProperty.Text.Trim() : null);

    private static void Add(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, row);
        panel.Controls.Add(control, 1, row);
    }
}

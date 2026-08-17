using Playroma.Sound.Authoring.Diagnostics;

namespace Playroma.Sound.WinForms.Controls;

public sealed class DiagnosticActivatedEventArgs : EventArgs
{
    public DiagnosticActivatedEventArgs(AuthoringDiagnostic diagnostic) => Diagnostic = diagnostic;
    public AuthoringDiagnostic Diagnostic { get; }
}

public sealed class DiagnosticsControl : UserControl
{
    private readonly ToolStrip _tools;
    private readonly ToolStripLabel _summaryLabel;
    private readonly ToolStripComboBox _severityFilter;
    private readonly DataGridView _grid;
    private IReadOnlyList<AuthoringDiagnostic> _diagnostics = [];

    public DiagnosticsControl()
    {
        Dock = DockStyle.Fill;
        _tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _summaryLabel = new ToolStripLabel("No diagnostics");
        _severityFilter = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 120 };
        _severityFilter.Items.AddRange(["All", "Errors", "Warnings", "Information"]);
        _severityFilter.SelectedIndex = 0;
        _tools.Items.Add(_summaryLabel);
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(new ToolStripLabel("Show:"));
        _tools.Items.Add(_severityFilter);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None
        };
        _grid.Columns.Add(Column("Severity", "Severity", 82));
        _grid.Columns.Add(Column("Code", "Code", 105));
        _grid.Columns.Add(Column("DocumentId", "Document", 150));
        _grid.Columns.Add(Column("ObjectId", "Object", 130));
        _grid.Columns.Add(Column("PropertyPath", "Property", 130));
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Message", HeaderText = "Message", DataPropertyName = "Message", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
        _grid.Columns.Add(Column("SourcePath", "Source", 220));

        Controls.Add(_grid);
        Controls.Add(_tools);

        _severityFilter.SelectedIndexChanged += (_, _) => Rebind();
        _grid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || _grid.Rows[eventArgs.RowIndex].DataBoundItem is not AuthoringDiagnostic diagnostic) return;
            DiagnosticActivated?.Invoke(this, new DiagnosticActivatedEventArgs(diagnostic));
        };
    }

    public event EventHandler<DiagnosticActivatedEventArgs>? DiagnosticActivated;

    public void SetDiagnostics(IEnumerable<AuthoringDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics.ToArray();
        Rebind();
    }

    public void Clear() => SetDiagnostics([]);

    private void Rebind()
    {
        IEnumerable<AuthoringDiagnostic> filtered = _severityFilter.SelectedIndex switch
        {
            1 => _diagnostics.Where(item => item.Severity == AuthoringDiagnosticSeverity.Error),
            2 => _diagnostics.Where(item => item.Severity == AuthoringDiagnosticSeverity.Warning),
            3 => _diagnostics.Where(item => item.Severity == AuthoringDiagnosticSeverity.Information),
            _ => _diagnostics
        };
        AuthoringDiagnostic[] rows = filtered.ToArray();
        _grid.DataSource = rows;
        int errors = _diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Error);
        int warnings = _diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Warning);
        _summaryLabel.Text = $"{errors} error(s), {warnings} warning(s), {_diagnostics.Count - errors - warnings} information";

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not AuthoringDiagnostic diagnostic) continue;
            row.DefaultCellStyle.ForeColor = diagnostic.Severity switch
            {
                AuthoringDiagnosticSeverity.Error => Color.Firebrick,
                AuthoringDiagnosticSeverity.Warning => Color.DarkGoldenrod,
                _ => SystemColors.WindowText
            };
        }
    }

    private static DataGridViewTextBoxColumn Column(string property, string header, int width) => new()
    {
        Name = property,
        HeaderText = header,
        DataPropertyName = property,
        Width = width,
        SortMode = DataGridViewColumnSortMode.Automatic
    };
}

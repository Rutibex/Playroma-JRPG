using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>Named reusable reference picker used by database layouts.</summary>
public class ReferencePicker : DatabaseIdPicker
{
    public ReferencePicker(MvProjectSession session, MvDocumentKind targetKind)
        : base(session, targetKind)
    {
    }
}

public sealed class AnimationPicker : ReferencePicker
{
    public AnimationPicker(MvProjectSession session)
        : base(session, MvDocumentKind.Animations)
    {
        AllowNone = true;
    }
}

public sealed class ValidationBadge : Label
{
    public ValidationBadge()
    {
        AutoSize = true;
        Padding = new Padding(5, 2, 5, 2);
        BorderStyle = BorderStyle.FixedSingle;
        Severity = null;
    }

    public DiagnosticSeverity? Severity
    {
        get => _severity;
        set
        {
            _severity = value;
            Text = value?.ToString() ?? "OK";
            ForeColor = value switch
            {
                DiagnosticSeverity.Fatal or DiagnosticSeverity.Error => Color.DarkRed,
                DiagnosticSeverity.Warning => Color.DarkOrange,
                _ => SystemColors.ControlText
            };
        }
    }
    private DiagnosticSeverity? _severity;
}

public sealed class ReferencePanel : UserControl
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };

    public ReferencePanel()
    {
        Dock = DockStyle.Fill;
        _grid.Columns.Add("Confidence", "Confidence");
        _grid.Columns.Add("Usage", "Usage");
        _grid.Columns.Add("Target", "Target");
        _grid.Columns.Add("Location", "Location");
        Controls.Add(_grid);
    }

    public void SetReferences(IEnumerable<ProjectReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _grid.Rows.Clear();
        foreach (ProjectReference reference in references)
        {
            _grid.Rows.Add(reference.Confidence, reference.Usage, reference.Target, reference.Source.JsonPath);
        }
    }
}

public sealed class DamageEditor : UserControl
{
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly NumericUpDown _element = new() { Minimum = -1, Maximum = 9999, Width = 80 };
    private readonly NumericUpDown _variance = new() { Minimum = 0, Maximum = 100, Width = 70 };
    private readonly CheckBox _critical = new() { Text = "Critical", AutoSize = true };
    private readonly FormulaEditor _formula = new() { Height = 150, Dock = DockStyle.Fill };
    private bool _suppress;

    public DamageEditor()
    {
        Dock = DockStyle.Fill;
        foreach ((int id, string name) in Playroma.JRPG.DatabaseEditing.BuiltInDatabaseDefinitions.DamageTypeOptions)
        {
            _type.Items.Add(new DamageTypeOption(id, name));
        }
        FlowLayoutPanel header = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        header.Controls.AddRange([
            new Label { Text = "Type", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, _type,
            new Label { Text = "Element", AutoSize = true, Margin = new Padding(8, 8, 3, 0) }, _element,
            new Label { Text = "Variance", AutoSize = true, Margin = new Padding(8, 8, 3, 0) }, _variance,
            _critical]);
        Controls.Add(_formula);
        Controls.Add(header);
        _type.SelectedIndexChanged += Changed;
        _element.ValueChanged += Changed;
        _variance.ValueChanged += Changed;
        _critical.CheckedChanged += Changed;
        _formula.ContentChanged += Changed;
    }

    public event EventHandler? ValueChanged;
    public int DamageType { get => (_type.SelectedItem as DamageTypeOption)?.Id ?? 0; set => SelectType(value); }
    public int ElementId { get => decimal.ToInt32(_element.Value); set => _element.Value = Math.Clamp(value, -1, 9999); }
    public string Formula { get => _formula.Text; set => _formula.Text = value ?? string.Empty; }
    public int Variance { get => decimal.ToInt32(_variance.Value); set => _variance.Value = Math.Clamp(value, 0, 100); }
    public bool Critical { get => _critical.Checked; set => _critical.Checked = value; }

    public void SetValue(int damageType, int elementId, string formula, int variance, bool critical)
    {
        _suppress = true;
        SelectType(damageType);
        ElementId = elementId;
        Formula = formula;
        Variance = variance;
        Critical = critical;
        _suppress = false;
    }

    private void SelectType(int id)
    {
        _type.SelectedIndex = _type.Items.Cast<DamageTypeOption>().ToList().FindIndex(option => option.Id == id);
    }

    private void Changed(object? sender, EventArgs e)
    {
        if (!_suppress) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record DamageTypeOption(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}

public sealed class ArrayEditor : StructuredArrayEditor
{
}

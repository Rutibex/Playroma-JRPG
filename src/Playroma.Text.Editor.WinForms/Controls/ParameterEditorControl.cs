using System.Globalization;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class ParameterEditorControl : UserControl
{
    private readonly TableLayoutPanel _table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
    private TextStudioSession? _session;

    public ParameterEditorControl()
    {
        AutoScroll = true;
        Controls.Add(_table);
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public void Bind(TextStudioSession session)
    {
        if (_session is not null) _session.SelectionChanged -= OnSelectionChanged;
        _session = session;
        _session.SelectionChanged += OnSelectionChanged;
        Rebuild();
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => Ui(Rebuild);

    private void Rebuild()
    {
        _table.SuspendLayout();
        try
        {
            _table.Controls.Clear(); _table.RowStyles.Clear(); _table.RowCount = 0;
            var recipe = _session?.SelectedRecipe?.Recipe;
            if (recipe is null) { AddMessage("Select a recipe to edit its inputs."); return; }
            var definitions = RecipeParameterDiscovery.Discover(recipe);
            if (definitions.Count == 0) { AddMessage("This recipe has no exposed inputs. Advanced settings remain available in Recipe Designer."); return; }
            foreach (var pair in definitions)
            {
                var def = pair.Value;
                AddRow(pair.Key, CreateEditor(pair.Key, def), def.Description);
            }
        }
        finally { _table.ResumeLayout(true); }
    }

    private Control CreateEditor(string key, RecipeParameterDefinition def)
    {
        object? current = null;
        if (_session is not null && _session.Parameters.TryGetValue(key, out var value)) current = value;
        switch (def.Type)
        {
            case RecipeParameterType.Boolean:
                var check = new CheckBox { Checked = current is bool b && b, AutoSize = true };
                check.CheckedChanged += (_, _) => _session?.SetParameter(key, check.Checked);
                return check;
            case RecipeParameterType.Choice:
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                if (def.Choices is not null) combo.Items.AddRange(def.Choices.Cast<object>().ToArray());
                var desired = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
                var match = combo.Items.Cast<object>().FirstOrDefault(x => string.Equals(Convert.ToString(x), desired, StringComparison.OrdinalIgnoreCase));
                if (match is not null) combo.SelectedItem = match; else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                combo.SelectedIndexChanged += (_, _) => _session?.SetParameter(key, combo.SelectedItem?.ToString() ?? string.Empty);
                return combo;
            case RecipeParameterType.Integer:
                var integer = new NumericUpDown { Minimum = -1_000_000, Maximum = 1_000_000, Dock = DockStyle.Top, ThousandsSeparator = true };
                try { integer.Value = Convert.ToDecimal(current ?? 0, CultureInfo.InvariantCulture); } catch { }
                integer.ValueChanged += (_, _) => _session?.SetParameter(key, Decimal.ToInt32(integer.Value));
                return integer;
            case RecipeParameterType.Number:
                var number = new NumericUpDown { Minimum = -1_000_000, Maximum = 1_000_000, DecimalPlaces = 3, Increment = .1M, Dock = DockStyle.Top };
                try { number.Value = Convert.ToDecimal(current ?? 0d, CultureInfo.InvariantCulture); } catch { }
                number.ValueChanged += (_, _) => _session?.SetParameter(key, Decimal.ToDouble(number.Value));
                return number;
            default:
                var text = new TextBox { Text = current is IEnumerable<string> values && current is not string ? string.Join(", ", values) : Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty, Dock = DockStyle.Top };
                text.TextChanged += (_, _) => _session?.SetParameter(key, def.Type == RecipeParameterType.StringList ? text.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : text.Text);
                return text;
        }
    }

    private void AddRow(string name, Control editor, string? description)
    {
        var row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = name, AutoSize = true, Margin = new Padding(3, 7, 6, 8), Font = new Font(Font, FontStyle.Bold) };
        if (!string.IsNullOrWhiteSpace(description)) { label.AccessibleDescription = description; editor.AccessibleDescription = description; }
        editor.Margin = new Padding(3, 4, 3, 8);
        _table.Controls.Add(label, 0, row); _table.Controls.Add(editor, 1, row);
    }

    private void AddMessage(string text)
    {
        _table.RowCount = 1;
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Padding = new Padding(4) };
        _table.Controls.Add(label, 0, 0); _table.SetColumnSpan(label, 2);
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
}

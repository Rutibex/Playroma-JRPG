using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.WinForms.Controls;

public sealed class GraphParameterChangedEventArgs : EventArgs
{
    public GraphParameterChangedEventArgs(GraphParameterDescriptor descriptor, object? value, string mergeKey)
    {
        Descriptor = descriptor;
        Value = value;
        MergeKey = mergeKey;
    }
    public GraphParameterDescriptor Descriptor { get; }
    public object? Value { get; }
    public string MergeKey { get; }
}

public sealed class GraphMacroExposureEventArgs : EventArgs
{
    public GraphMacroExposureEventArgs(GraphParameterDescriptor descriptor) => Descriptor = descriptor;
    public GraphParameterDescriptor Descriptor { get; }
}

public sealed class DescriptorParameterEditorControl : UserControl
{
    private readonly TableLayoutPanel _layout;
    private readonly ToolTip _toolTip = new();
    private bool _refreshing;

    public DescriptorParameterEditorControl()
    {
        AutoScroll = true;
        _layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(5) };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        Controls.Add(_layout);
    }

    public event EventHandler<GraphParameterChangedEventArgs>? ParameterChanged;
    public event EventHandler<GraphMacroExposureEventArgs>? ExposeMacroRequested;

    public void SetNode(NodeDefinition? node, NodeTypeDescriptor? descriptor)
    {
        _refreshing = true;
        _layout.SuspendLayout();
        try
        {
            while (_layout.Controls.Count > 0)
            {
                Control control = _layout.Controls[0];
                _layout.Controls.RemoveAt(0);
                control.Dispose();
            }
            _layout.RowStyles.Clear();
            _layout.RowCount = 0;
            if (node is null || descriptor is null) return;
            foreach (GraphParameterDescriptor parameter in descriptor.Parameters)
            {
                GraphValue value = node.Parameters.TryGetValue(parameter.Id, out GraphValue configured) ? configured : parameter.DefaultValue;
                AddParameterRow(parameter, value);
            }
        }
        finally
        {
            _layout.ResumeLayout(true);
            _refreshing = false;
        }
    }

    private void AddParameterRow(GraphParameterDescriptor parameter, GraphValue value)
    {
        int row = _layout.RowCount++;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = string.IsNullOrWhiteSpace(parameter.Unit) ? parameter.DisplayName : $"{parameter.DisplayName} ({parameter.Unit})",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 7, 3, 0)
        };
        _toolTip.SetToolTip(label, parameter.Id);
        Control editor = CreateEditor(parameter, value);
        var host = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = parameter.MacroAddressable ? 2 : 1, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.Controls.Add(editor, 0, 0);
        if (parameter.MacroAddressable)
        {
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
            var macro = new Button { Text = "M", Width = 25, Height = 24, FlatStyle = FlatStyle.System, TabStop = false };
            _toolTip.SetToolTip(macro, "Expose this parameter as a macro");
            macro.Click += (_, _) => ExposeMacroRequested?.Invoke(this, new GraphMacroExposureEventArgs(parameter));
            host.Controls.Add(macro, 1, 0);
        }
        _layout.Controls.Add(label, 0, row);
        _layout.Controls.Add(host, 1, row);
    }

    private Control CreateEditor(GraphParameterDescriptor parameter, GraphValue value)
    {
        switch (parameter.Type)
        {
            case GraphParameterType.Boolean:
            {
                var check = new CheckBox { Checked = value.AsBoolean(parameter.DefaultValue.AsBoolean()), Dock = DockStyle.Top, AutoSize = true };
                check.CheckedChanged += (_, _) => Raise(parameter, check.Checked, $"parameter:{parameter.Id}");
                return check;
            }
            case GraphParameterType.Enumeration:
            {
                var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                combo.Items.AddRange((parameter.AllowedValues ?? []).Cast<object>().ToArray());
                string current = value.AsString(parameter.DefaultValue.AsString());
                int index = combo.FindStringExact(current);
                combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
                combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is string selected) Raise(parameter, selected, $"parameter:{parameter.Id}"); };
                return combo;
            }
            case GraphParameterType.Text:
            {
                var text = new TextBox { Text = value.AsString(parameter.DefaultValue.AsString()), Dock = DockStyle.Top };
                text.Validated += (_, _) => Raise(parameter, text.Text, "");
                return text;
            }
            case GraphParameterType.Integer:
            case GraphParameterType.Seed:
            case GraphParameterType.Number:
            default:
            {
                decimal minimum = SafeDecimal(parameter.Minimum ?? (parameter.Type == GraphParameterType.Seed ? 0 : -1_000_000), -1_000_000m);
                decimal maximum = SafeDecimal(parameter.Maximum ?? (parameter.Type == GraphParameterType.Seed ? 4_294_967_295 : 1_000_000), 1_000_000m);
                if (maximum <= minimum) maximum = minimum + 1;
                decimal current = Math.Clamp(SafeDecimal(value.AsDouble(parameter.DefaultValue.AsDouble()), 0), minimum, maximum);
                var numeric = new NumericUpDown
                {
                    Dock = DockStyle.Top,
                    Minimum = minimum,
                    Maximum = maximum,
                    Value = current,
                    DecimalPlaces = parameter.Type == GraphParameterType.Number ? 4 : 0,
                    Increment = parameter.Type == GraphParameterType.Number ? Math.Max(0.0001m, (maximum - minimum) / 1000m) : 1m,
                    ThousandsSeparator = parameter.Type is GraphParameterType.Integer or GraphParameterType.Seed
                };
                numeric.ValueChanged += (_, _) => Raise(parameter, parameter.Type == GraphParameterType.Number ? (object)(double)numeric.Value : (long)numeric.Value, $"parameter:{parameter.Id}");
                return numeric;
            }
        }
    }

    private void Raise(GraphParameterDescriptor descriptor, object? value, string mergeKey)
    {
        if (!_refreshing) ParameterChanged?.Invoke(this, new GraphParameterChangedEventArgs(descriptor, value, mergeKey));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    private static decimal SafeDecimal(double value, decimal fallback)
    {
        if (!double.IsFinite(value) || value > (double)decimal.MaxValue || value < (double)decimal.MinValue) return fallback;
        return (decimal)value;
    }
}

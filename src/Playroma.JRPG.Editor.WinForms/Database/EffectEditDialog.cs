using System.Globalization;
using System.Text.Json.Nodes;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Indexing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class EffectEditDialog : Form
{
    private readonly Phase9BProjectCatalog _catalog;
    private readonly ComboBox _code = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NamedIdPicker _dataId = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _value1 = Number();
    private readonly NumericUpDown _value2 = Number();
    private readonly Label _dataLabel = new() { Text = "Data", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _value1Label = new() { Text = "Value 1", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _value2Label = new() { Text = "Value 2", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly CodeTextEditorControl _raw = new() { Dock = DockStyle.Fill };
    private readonly Label _error = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Firebrick };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private JsonObject _object;
    private bool _loading;

    private EffectEditDialog(Phase9BProjectCatalog catalog, string? rawJson)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _object = Parse(rawJson);
        Text = rawJson is null ? "Add Effect" : "Edit Effect";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 470);
        Size = new Size(780, 560);
        foreach ((int code, string name) in EffectCodes) _code.Items.Add(new CodeItem(code, name));

        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, new Label { Text = "Effect type", AutoSize = true, Anchor = AnchorStyles.Left }, _code);
        AddRow(table, 1, _dataLabel, _dataId);
        AddRow(table, 2, _value1Label, _value1);
        AddRow(table, 3, _value2Label, _value2);
        AddRow(table, 4, new Label { Text = "Preview", AutoSize = true, Anchor = AnchorStyles.Left }, _summary);
        Panel typed = new() { Dock = DockStyle.Fill, AutoScroll = true };
        typed.Controls.Add(table);
        _tabs.TabPages.Add(new TabPage("Typed") { Controls = { typed } });
        _tabs.TabPages.Add(new TabPage("Raw JSON") { Controls = { _raw } });

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        Button okay = new() { Text = "OK", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        Button applyRaw = new() { Text = "Apply Raw to Typed", AutoSize = true };
        buttons.Controls.AddRange([okay, cancel, applyRaw]);
        Controls.Add(_tabs);
        Controls.Add(_error);
        Controls.Add(buttons);
        AcceptButton = okay;
        CancelButton = cancel;

        _code.SelectedIndexChanged += (_, _) => { if (!_loading) { ConfigureDataPicker(); UpdateSummary(); } };
        _dataId.ValueChanged += (_, _) => { if (!_loading) UpdateSummary(); };
        _value1.ValueChanged += (_, _) => { if (!_loading) UpdateSummary(); };
        _value2.ValueChanged += (_, _) => { if (!_loading) UpdateSummary(); };
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedIndex == 1) _raw.Text = BuildTypedObject().ToJsonString(); };
        applyRaw.Click += (_, _) => ApplyRaw();
        okay.Click += (_, _) => Accept();
        LoadTyped();
    }

    public string ResultJson { get; private set; } = string.Empty;

    public static string? Edit(IWin32Window? owner, Phase9BProjectCatalog catalog, string? rawJson)
    {
        using EffectEditDialog dialog = new(catalog, rawJson);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.ResultJson : null;
    }

    public static string Format(string rawJson, Phase9BProjectCatalog catalog)
    {
        JsonObject value = Parse(rawJson);
        return Format(ReadInt(value, "code", 0), ReadInt(value, "dataId", 0), ReadDouble(value, "value1"), ReadDouble(value, "value2"), catalog);
    }

    private void LoadTyped()
    {
        _loading = true;
        try
        {
            int code = ReadInt(_object, "code", 11);
            int index = _code.Items.Cast<CodeItem>().ToList().FindIndex(item => item.Code == code);
            if (index < 0)
            {
                _code.Items.Add(new CodeItem(code, $"Unknown code {code}"));
                index = _code.Items.Count - 1;
            }
            _code.SelectedIndex = index;
            ConfigureDataPicker();
            _dataId.SelectedId = ReadInt(_object, "dataId", 0);
            _value1.Value = Clamp(ReadDecimal(_object, "value1"), _value1);
            _value2.Value = Clamp(ReadDecimal(_object, "value2"), _value2);
            _raw.Text = _object.ToJsonString();
        }
        finally { _loading = false; }
        UpdateSummary();
    }

    private void ConfigureDataPicker()
    {
        int code = SelectedCode;
        _value1.Visible = _value1Label.Visible = true;
        _value2.Visible = _value2Label.Visible = true;
        _dataId.Visible = _dataLabel.Visible = true;
        _value1.DecimalPlaces = 4;
        _value2.DecimalPlaces = 4;
        _value1Label.Text = "Value 1";
        _value2Label.Text = "Value 2";
        _dataLabel.Text = "Data";

        switch (code)
        {
            case 11:
            case 12:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                _dataId.Visible = _dataLabel.Visible = false;
                _value1Label.Text = "Rate (1.0 = 100%)";
                _value2Label.Text = "Flat amount";
                break;
            case 13:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                _dataId.Visible = _dataLabel.Visible = false;
                _value1Label.Text = "TP amount";
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 21:
            case 22:
                List<DatabaseChoice> states = _catalog.States.ToList();
                if (code == 21) states.Insert(0, new DatabaseChoice(0, "Normal Attack States"));
                _dataId.SetChoices(states, "State", includeNone: code != 21);
                _dataLabel.Text = "State";
                _value1Label.Text = "Chance (1.0 = 100%)";
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 31:
            case 32:
                _dataId.SetChoices(ParameterChoices(), "Parameter", includeNone: false);
                _dataLabel.Text = "Parameter";
                _value1Label.Text = "Turns";
                _value1.DecimalPlaces = 0;
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 33:
            case 34:
                _dataId.SetChoices(ParameterChoices(), "Parameter", includeNone: false);
                _dataLabel.Text = "Parameter";
                _value1.Visible = _value1Label.Visible = false;
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 41:
                _dataId.SetChoices([new NamedDatabaseValue(0, "Escape")], "Special Effect", includeNone: false);
                _dataLabel.Text = "Special Effect";
                _value1.Visible = _value1Label.Visible = false;
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 42:
                _dataId.SetChoices(ParameterChoices(), "Parameter", includeNone: false);
                _dataLabel.Text = "Parameter";
                _value1Label.Text = "Amount";
                _value1.DecimalPlaces = 0;
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 43:
                _dataId.SetChoices(_catalog.Skills, "Skill", includeNone: false);
                _dataLabel.Text = "Skill";
                _value1.Visible = _value1Label.Visible = false;
                _value2.Visible = _value2Label.Visible = false;
                break;
            case 44:
                _dataId.SetChoices(_catalog.CommonEvents, "Common Event", includeNone: false);
                _dataLabel.Text = "Common Event";
                _value1.Visible = _value1Label.Visible = false;
                _value2.Visible = _value2Label.Visible = false;
                break;
            default:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                break;
        }
    }

    private void ApplyRaw()
    {
        try
        {
            _object = Parse(_raw.Text);
            _error.Text = string.Empty;
            LoadTyped();
            _tabs.SelectedIndex = 0;
        }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private void Accept()
    {
        try
        {
            _object = _tabs.SelectedIndex == 1 ? Parse(_raw.Text) : BuildTypedObject();
            ResultJson = _object.ToJsonString();
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private JsonObject BuildTypedObject()
    {
        JsonObject result = (JsonObject)_object.DeepClone();
        result["code"] = SelectedCode;
        result["dataId"] = _dataId.SelectedId;
        result["value1"] = decimal.ToDouble(_value1.Value);
        result["value2"] = decimal.ToDouble(_value2.Value);
        return result;
    }

    private void UpdateSummary()
    {
        try
        {
            _summary.Text = Format(SelectedCode, _dataId.SelectedId, decimal.ToDouble(_value1.Value), decimal.ToDouble(_value2.Value), _catalog);
        }
        catch (Exception exception) { _summary.Text = exception.Message; }
    }

    private int SelectedCode => (_code.SelectedItem as CodeItem)?.Code ?? 11;

    private static string Format(int code, int dataId, double value1, double value2, Phase9BProjectCatalog catalog) => code switch
    {
        11 => $"Recover HP: {Percent(value1)} + {value2:0.##}",
        12 => $"Recover MP: {Percent(value1)} + {value2:0.##}",
        13 => $"Gain TP: {value1:0.##}",
        21 => $"Add State: {(dataId == 0 ? "Normal Attack States" : catalog.Resolve(ReferenceTargetKind.State, dataId))} at {Percent(value1)}",
        22 => $"Remove State: {catalog.Resolve(ReferenceTargetKind.State, dataId)} at {Percent(value1)}",
        31 => $"Add Buff: {ParameterName(dataId)} for {value1:0} turns",
        32 => $"Add Debuff: {ParameterName(dataId)} for {value1:0} turns",
        33 => $"Remove Buff: {ParameterName(dataId)}",
        34 => $"Remove Debuff: {ParameterName(dataId)}",
        41 => dataId == 0 ? "Special Effect: Escape" : $"Special Effect #{dataId}",
        42 => $"Grow {ParameterName(dataId)} by {value1:+0;-0;0}",
        43 => $"Learn Skill: {catalog.Resolve(ReferenceTargetKind.Skill, dataId)}",
        44 => $"Common Event: {catalog.Resolve(ReferenceTargetKind.CommonEvent, dataId)}",
        _ => $"Unknown Effect {code}: data {dataId}, values {value1.ToString(CultureInfo.InvariantCulture)}, {value2.ToString(CultureInfo.InvariantCulture)}"
    };

    private static IReadOnlyList<NamedDatabaseValue> ParameterChoices() => MvParameterNames.All.Select((name, id) => new NamedDatabaseValue(id, name)).ToArray();
    private static string ParameterName(int id) => MvParameterNames.Get(id);
    private static string Percent(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static JsonObject Parse(string? rawJson) => string.IsNullOrWhiteSpace(rawJson)
        ? new JsonObject { ["code"] = 11, ["dataId"] = 0, ["value1"] = 0d, ["value2"] = 0d }
        : JsonNode.Parse(rawJson) as JsonObject ?? throw new InvalidDataException("An effect entry must be a JSON object.");
    private static int ReadInt(JsonObject value, string name, int fallback) => value[name] is JsonValue item && item.TryGetValue(out int parsed) ? parsed : fallback;
    private static double ReadDouble(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out double parsed) ? parsed : 0;
    private static decimal ReadDecimal(JsonObject value, string name) => Convert.ToDecimal(ReadDouble(value, name), CultureInfo.InvariantCulture);
    private static decimal Clamp(decimal value, NumericUpDown control) => Math.Clamp(value, control.Minimum, control.Maximum);
    private static NumericUpDown Number() => new() { Minimum = -999999999, Maximum = 999999999, DecimalPlaces = 4, Increment = 0.01m, Dock = DockStyle.Fill };
    private static void AddRow(TableLayoutPanel table, int row, Control label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        label.Margin = new Padding(3, 8, 8, 3);
        control.Margin = new Padding(3, 3, 3, 8);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }
    private sealed record CodeItem(int Code, string Name) { public override string ToString() => $"{Code} — {Name}"; }
    private static IReadOnlyDictionary<int, string> EffectCodes { get; } = new Dictionary<int, string>
    {
        [11] = "Recover HP", [12] = "Recover MP", [13] = "Gain TP", [21] = "Add State", [22] = "Remove State",
        [31] = "Add Buff", [32] = "Add Debuff", [33] = "Remove Buff", [34] = "Remove Debuff", [41] = "Special Effect",
        [42] = "Grow Parameter", [43] = "Learn Skill", [44] = "Common Event"
    };
}

using System.Globalization;
using System.Text.Json.Nodes;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Indexing;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>Typed MV trait editor that preserves every unknown property in the original object.</summary>
public sealed class TraitEditDialog : Form
{
    private readonly Phase9BProjectCatalog _catalog;
    private readonly ComboBox _code = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NamedIdPicker _dataId = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _value = new() { Minimum = -999999999, Maximum = 999999999, DecimalPlaces = 4, Increment = 0.01m, Dock = DockStyle.Fill };
    private readonly Label _dataLabel = new() { Text = "Data", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _valueLabel = new() { Text = "Value", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _summary = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly CodeTextEditorControl _raw = new() { Dock = DockStyle.Fill };
    private readonly Label _error = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Firebrick };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private JsonObject _object;
    private bool _loading;

    private TraitEditDialog(Phase9BProjectCatalog catalog, string? rawJson)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _object = Parse(rawJson);
        Text = rawJson is null ? "Add Trait" : "Edit Trait";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 470);
        Size = new Size(780, 560);
        foreach ((int code, string name) in TraitCodes) _code.Items.Add(new CodeItem(code, name));

        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, new Label { Text = "Trait type", AutoSize = true, Anchor = AnchorStyles.Left }, _code);
        AddRow(table, 1, _dataLabel, _dataId);
        AddRow(table, 2, _valueLabel, _value);
        AddRow(table, 3, new Label { Text = "Preview", AutoSize = true, Anchor = AnchorStyles.Left }, _summary);
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

        _code.SelectedIndexChanged += (_, _) => { if (!_loading) { ConfigureFields(); UpdateSummary(); } };
        _dataId.ValueChanged += (_, _) => { if (!_loading) UpdateSummary(); };
        _value.ValueChanged += (_, _) => { if (!_loading) UpdateSummary(); };
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedIndex == 1) _raw.Text = BuildTypedObject().ToJsonString(); };
        applyRaw.Click += (_, _) => ApplyRaw();
        okay.Click += (_, _) => Accept();
        LoadTyped();
    }

    public string ResultJson { get; private set; } = string.Empty;

    public static string? Edit(IWin32Window? owner, Phase9BProjectCatalog catalog, string? rawJson)
    {
        using TraitEditDialog dialog = new(catalog, rawJson);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.ResultJson : null;
    }

    public static string Format(string rawJson, Phase9BProjectCatalog catalog)
    {
        JsonObject value = Parse(rawJson);
        return Format(ReadInt(value, "code", 0), ReadInt(value, "dataId", 0), ReadDouble(value, "value", 1), catalog);
    }

    private void LoadTyped()
    {
        _loading = true;
        try
        {
            int code = ReadInt(_object, "code", 21);
            int index = _code.Items.Cast<CodeItem>().ToList().FindIndex(item => item.Code == code);
            if (index < 0)
            {
                _code.Items.Add(new CodeItem(code, $"Unknown code {code}"));
                index = _code.Items.Count - 1;
            }
            _code.SelectedIndex = index;
            ConfigureFields();
            _dataId.SelectedId = ReadInt(_object, "dataId", 0);
            _value.Value = Math.Clamp(ReadDecimal(_object, "value", 1), _value.Minimum, _value.Maximum);
            _raw.Text = _object.ToJsonString();
        }
        finally { _loading = false; }
        UpdateSummary();
    }

    private void ConfigureFields()
    {
        int code = SelectedCode;
        _dataId.Visible = _dataLabel.Visible = true;
        _value.Visible = _valueLabel.Visible = true;
        _value.DecimalPlaces = 4;
        _valueLabel.Text = "Value";
        _dataLabel.Text = "Data";
        switch (code)
        {
            case 11:
            case 31:
                _dataId.SetChoices(_catalog.SystemTypes.Elements, "Element", includeNone: false);
                _dataLabel.Text = "Element";
                _valueLabel.Text = code == 11 ? "Rate (1.0 = 100%)" : "Unused";
                _value.Visible = _valueLabel.Visible = code == 11;
                break;
            case 12:
            case 21:
            case 22:
            case 23:
                _dataId.SetChoices(ParameterChoices(), "Parameter", includeNone: false);
                _dataLabel.Text = "Parameter";
                _valueLabel.Text = code == 22 ? "Addition (0.05 = +5%)" : "Rate (1.0 = 100%)";
                break;
            case 13:
            case 14:
            case 32:
                _dataId.SetChoices(_catalog.States, "State", includeNone: false);
                _dataLabel.Text = "State";
                _valueLabel.Text = code == 32 ? "Chance (1.0 = 100%)" : "Rate (1.0 = 100%)";
                _value.Visible = _valueLabel.Visible = code != 14;
                break;
            case 33:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                _dataId.Visible = _dataLabel.Visible = false;
                _valueLabel.Text = "Speed modifier";
                _value.DecimalPlaces = 0;
                break;
            case 34:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                _dataId.Visible = _dataLabel.Visible = false;
                _valueLabel.Text = "Additional attacks";
                break;
            case 41:
            case 42:
                _dataId.SetChoices(_catalog.SystemTypes.SkillTypes, "Skill Type", includeNone: false);
                _dataLabel.Text = "Skill Type";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 43:
            case 44:
                _dataId.SetChoices(_catalog.Skills, "Skill", includeNone: false);
                _dataLabel.Text = "Skill";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 51:
                _dataId.SetChoices(_catalog.SystemTypes.WeaponTypes, "Weapon Type", includeNone: false);
                _dataLabel.Text = "Weapon Type";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 52:
                _dataId.SetChoices(_catalog.SystemTypes.ArmorTypes, "Armor Type", includeNone: false);
                _dataLabel.Text = "Armor Type";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 53:
            case 54:
                _dataId.SetChoices(_catalog.SystemTypes.EquipmentTypes, "Equipment Slot", includeNone: false);
                _dataLabel.Text = "Equipment Slot";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 55:
                _dataId.SetChoices([new NamedDatabaseValue(0, "Normal Slots"), new NamedDatabaseValue(1, "Dual Wield")], "Slot Type", includeNone: false);
                _dataLabel.Text = "Slot Type";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 61:
                _dataId.SetChoices(Array.Empty<NamedDatabaseValue>(), "Data", includeNone: true);
                _dataId.Visible = _dataLabel.Visible = false;
                _valueLabel.Text = "Chance (1.0 = 100%)";
                break;
            case 62:
                _dataId.SetChoices(SpecialFlagChoices, "Special Flag", includeNone: false);
                _dataLabel.Text = "Special Flag";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 63:
                _dataId.SetChoices(CollapseChoices, "Collapse Effect", includeNone: false);
                _dataLabel.Text = "Collapse Effect";
                _value.Visible = _valueLabel.Visible = false;
                break;
            case 64:
                _dataId.SetChoices(PartyAbilityChoices, "Party Ability", includeNone: false);
                _dataLabel.Text = "Party Ability";
                _value.Visible = _valueLabel.Visible = false;
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
        result["value"] = decimal.ToDouble(_value.Value);
        return result;
    }

    private void UpdateSummary()
    {
        try { _summary.Text = Format(SelectedCode, _dataId.SelectedId, decimal.ToDouble(_value.Value), _catalog); }
        catch (Exception exception) { _summary.Text = exception.Message; }
    }

    private int SelectedCode => (_code.SelectedItem as CodeItem)?.Code ?? 21;

    private static string Format(int code, int id, double value, Phase9BProjectCatalog catalog) => code switch
    {
        11 => $"Element Rate: {catalog.Resolve(ReferenceTargetKind.Element, id)} × {Percent(value)}",
        12 => $"Debuff Rate: {MvParameterNames.Get(id)} × {Percent(value)}",
        13 => $"State Rate: {catalog.Resolve(ReferenceTargetKind.State, id)} × {Percent(value)}",
        14 => $"State Resist: {catalog.Resolve(ReferenceTargetKind.State, id)}",
        21 => $"Parameter: {MvParameterNames.Get(id)} × {Percent(value)}",
        22 => $"Ex-Parameter #{id}: {SignedPercent(value)}",
        23 => $"Sp-Parameter #{id}: × {Percent(value)}",
        31 => $"Attack Element: {catalog.Resolve(ReferenceTargetKind.Element, id)}",
        32 => $"Attack State: {catalog.Resolve(ReferenceTargetKind.State, id)} at {Percent(value)}",
        33 => $"Attack Speed: {value:+0;-0;0}",
        34 => $"Attack Times: +{value:0.##}",
        41 => $"Add Skill Type: {catalog.Resolve(ReferenceTargetKind.SkillType, id)}",
        42 => $"Seal Skill Type: {catalog.Resolve(ReferenceTargetKind.SkillType, id)}",
        43 => $"Add Skill: {catalog.Resolve(ReferenceTargetKind.Skill, id)}",
        44 => $"Seal Skill: {catalog.Resolve(ReferenceTargetKind.Skill, id)}",
        51 => $"Equip Weapon Type: {catalog.Resolve(ReferenceTargetKind.WeaponType, id)}",
        52 => $"Equip Armor Type: {catalog.Resolve(ReferenceTargetKind.ArmorType, id)}",
        53 => $"Lock Equipment Slot: {catalog.Resolve(ReferenceTargetKind.EquipmentType, id)}",
        54 => $"Seal Equipment Slot: {catalog.Resolve(ReferenceTargetKind.EquipmentType, id)}",
        55 => id == 1 ? "Slot Type: Dual Wield" : "Slot Type: Normal",
        61 => $"Action Times +: {Percent(value)}",
        62 => $"Special Flag: {ResolveStatic(SpecialFlagChoices, id)}",
        63 => $"Collapse Effect: {ResolveStatic(CollapseChoices, id)}",
        64 => $"Party Ability: {ResolveStatic(PartyAbilityChoices, id)}",
        _ => $"Unknown Trait {code}: data {id}, value {value.ToString(CultureInfo.InvariantCulture)}"
    };

    private static string ResolveStatic(IReadOnlyList<NamedDatabaseValue> values, int id) => values.FirstOrDefault(value => value.Id == id)?.Name ?? $"#{id}";
    private static IReadOnlyList<NamedDatabaseValue> ParameterChoices() => MvParameterNames.All.Select((name, id) => new NamedDatabaseValue(id, name)).ToArray();
    private static string Percent(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string SignedPercent(double value) => (value * 100).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";
    private static JsonObject Parse(string? rawJson) => string.IsNullOrWhiteSpace(rawJson)
        ? new JsonObject { ["code"] = 21, ["dataId"] = 0, ["value"] = 1d }
        : JsonNode.Parse(rawJson) as JsonObject ?? throw new InvalidDataException("A trait entry must be a JSON object.");
    private static int ReadInt(JsonObject value, string name, int fallback) => value[name] is JsonValue item && item.TryGetValue(out int parsed) ? parsed : fallback;
    private static double ReadDouble(JsonObject value, string name, double fallback) => value[name] is JsonValue item && item.TryGetValue(out double parsed) ? parsed : fallback;
    private static decimal ReadDecimal(JsonObject value, string name, decimal fallback) => Convert.ToDecimal(ReadDouble(value, name, decimal.ToDouble(fallback)), CultureInfo.InvariantCulture);
    private static void AddRow(TableLayoutPanel table, int row, Control label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        label.Margin = new Padding(3, 8, 8, 3);
        control.Margin = new Padding(3, 3, 3, 8);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }
    private sealed record CodeItem(int Code, string Name) { public override string ToString() => $"{Code} — {Name}"; }

    private static IReadOnlyDictionary<int, string> TraitCodes { get; } = new Dictionary<int, string>
    {
        [11] = "Element Rate", [12] = "Debuff Rate", [13] = "State Rate", [14] = "State Resist",
        [21] = "Parameter", [22] = "Ex-Parameter", [23] = "Sp-Parameter", [31] = "Attack Element",
        [32] = "Attack State", [33] = "Attack Speed", [34] = "Attack Times", [41] = "Add Skill Type",
        [42] = "Seal Skill Type", [43] = "Add Skill", [44] = "Seal Skill", [51] = "Equip Weapon",
        [52] = "Equip Armor", [53] = "Lock Equipment", [54] = "Seal Equipment", [55] = "Slot Type",
        [61] = "Action Times", [62] = "Special Flag", [63] = "Collapse Effect", [64] = "Party Ability"
    };
    private static IReadOnlyList<NamedDatabaseValue> SpecialFlagChoices { get; } =
    [new(0, "Auto Battle"), new(1, "Guard"), new(2, "Substitute"), new(3, "Preserve TP")];
    private static IReadOnlyList<NamedDatabaseValue> CollapseChoices { get; } =
    [new(0, "Normal"), new(1, "Boss"), new(2, "Instant")];
    private static IReadOnlyList<NamedDatabaseValue> PartyAbilityChoices { get; } =
    [new(0, "Encounter Half"), new(1, "Encounter None"), new(2, "Cancel Surprise"), new(3, "Raise Preemptive"), new(4, "Gold Double"), new(5, "Drop Item Double")];
}

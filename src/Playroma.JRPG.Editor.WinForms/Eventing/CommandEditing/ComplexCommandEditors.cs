using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.Editor.WinForms.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

public sealed class ConditionalBranchCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _type = OptionCombo(
        (0, "Switch"), (1, "Variable"), (2, "Self Switch"), (3, "Timer"),
        (4, "Actor"), (5, "Enemy"), (6, "Character"), (7, "Gold"),
        (8, "Item"), (9, "Weapon"), (10, "Armor"), (11, "Button"), (12, "Script"));
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12) };
    private readonly JsonArray _original;

    private ComboBox? _project1;
    private ComboBox? _project2;
    private ComboBox? _option1;
    private ComboBox? _option2;
    private NumericUpDown? _number1;
    private CheckBox? _check;
    private TextBox? _text;

    public ConditionalBranchCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        _original = JsonNode.Parse(context.Node.RawCommands[0].ParametersRawJson) as JsonArray ?? [];
        JsonElement parameters = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        SelectOption(_type, JsonParameterReader.GetInt(parameters, 0));
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TableLayoutPanel typeRow = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        typeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        typeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        typeRow.Controls.Add(new Label { Text = "Condition Type:", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, 0);
        typeRow.Controls.Add(_type, 1, 0);
        _view.Controls.Add(typeRow, 0, 0);
        _view.Controls.Add(_fields, 0, 1);
        _type.SelectedIndexChanged += (_, _) => RebuildFields(loadOriginal: false);
        RebuildFields(loadOriginal: true);
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        int type = ReadOption(_type);
        JsonArray p = [type];
        switch (type)
        {
            case 0:
                p.Add(ReadProjectId(_project1));
                p.Add(ReadOption(_option1));
                break;
            case 1:
                p.Add(ReadProjectId(_project1));
                p.Add(ReadOption(_option1));
                p.Add(ReadOption(_option1) == 0 ? ReadNumber(_number1) : ReadProjectId(_project2));
                p.Add(ReadOption(_option2));
                break;
            case 2:
                p.Add((_option1?.SelectedItem as StringOption)?.Value ?? "A");
                p.Add(ReadOption(_option2));
                break;
            case 3:
                p.Add(ReadNumber(_number1));
                p.Add(ReadOption(_option1));
                break;
            case 4:
            {
                int condition = ReadOption(_option1);
                p.Add(ReadProjectId(_project1));
                p.Add(condition);
                if (condition == 1) p.Add(_text?.Text ?? string.Empty);
                else if (condition >= 2) p.Add(ReadProjectId(_project2));
                break;
            }
            case 5:
                p.Add(ReadNumber(_number1));
                p.Add(ReadOption(_option1));
                if (ReadOption(_option1) == 1) p.Add(ReadProjectId(_project1));
                break;
            case 6:
                p.Add(ReadCharacterId(_project1));
                p.Add(ReadOption(_option1));
                break;
            case 7:
                p.Add(ReadNumber(_number1));
                p.Add(ReadOption(_option1));
                break;
            case 8:
                p.Add(ReadProjectId(_project1));
                break;
            case 9:
            case 10:
                p.Add(ReadProjectId(_project1));
                p.Add(_check?.Checked == true);
                break;
            case 11:
                p.Add((_option1?.SelectedItem as StringOption)?.Value ?? "ok");
                break;
            case 12:
                p.Add(_text?.Text ?? string.Empty);
                break;
        }
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, p.ToJsonString());
    }

    public IReadOnlyList<string> Validate()
    {
        if (ReadOption(_type) == 12 && string.IsNullOrWhiteSpace(_text?.Text)) return ["The script condition is empty."];
        return [];
    }

    private void RebuildFields(bool loadOriginal)
    {
        JsonElement p = JsonParameterReader.ParseArray(_context.Node.RawCommands[0].ParametersRawJson);
        int type = ReadOption(_type);
        _fields.SuspendLayout();
        try
        {
            _fields.Controls.Clear();
            _fields.RowStyles.Clear();
            _fields.RowCount = 0;
            _fields.ColumnStyles.Clear();
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _project1 = null;
            _project2 = null;
            _option1 = null;
            _option2 = null;
            _number1 = null;
            _check = null;
            _text = null;

            switch (type)
            {
                case 0:
                    _project1 = ProjectCombo("Switch", loadOriginal ? JsonParameterReader.GetInt(p, 1, 1) : 1);
                    _option1 = OptionCombo((0, "ON"), (1, "OFF"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    AddField("Switch", _project1);
                    AddField("State", _option1);
                    break;
                case 1:
                    _project1 = ProjectCombo("Variable", loadOriginal ? JsonParameterReader.GetInt(p, 1, 1) : 1);
                    _option1 = OptionCombo((0, "Constant"), (1, "Variable"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    _number1 = Number(-999999, 999999, loadOriginal ? JsonParameterReader.GetInt(p, 3) : 0);
                    _project2 = ProjectCombo("Variable", loadOriginal ? JsonParameterReader.GetInt(p, 3, 1) : 1);
                    _option2 = OptionCombo((0, "Equal to"), (1, "Greater than or equal to"), (2, "Less than or equal to"), (3, "Greater than"), (4, "Less than"), (5, "Not equal to"));
                    SelectOption(_option2, loadOriginal ? JsonParameterReader.GetInt(p, 4) : 0);
                    AddField("Variable", _project1);
                    AddField("Compare With", _option1);
                    AddField(ReadOption(_option1) == 0 ? "Constant" : "Variable", ReadOption(_option1) == 0 ? _number1 : _project2);
                    AddField("Comparison", _option2);
                    _option1.SelectedIndexChanged += (_, _) => RebuildFields(loadOriginal: false);
                    break;
                case 2:
                    _option1 = StringCombo(("A", "A"), ("B", "B"), ("C", "C"), ("D", "D"));
                    SelectString(_option1, loadOriginal ? JsonParameterReader.GetString(p, 1) : "A");
                    _option2 = OptionCombo((0, "ON"), (1, "OFF"));
                    SelectOption(_option2, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    AddField("Self Switch", _option1);
                    AddField("State", _option2);
                    break;
                case 3:
                    _number1 = Number(0, 599999, loadOriginal ? JsonParameterReader.GetInt(p, 1) : 60);
                    _option1 = OptionCombo((0, "At least"), (1, "At most"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    AddField("Seconds", _number1);
                    AddField("Comparison", _option1);
                    break;
                case 4:
                {
                    int condition = loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0;
                    _project1 = ProjectCombo("Actor", loadOriginal ? JsonParameterReader.GetInt(p, 1, 1) : 1);
                    _option1 = OptionCombo((0, "In the Party"), (1, "Name"), (2, "Class"), (3, "Skill"), (4, "Weapon"), (5, "Armor"), (6, "State"));
                    SelectOption(_option1, condition);
                    AddField("Actor", _project1);
                    AddField("Condition", _option1);
                    if (condition == 1)
                    {
                        _text = new TextBox { Dock = DockStyle.Fill, Text = loadOriginal ? JsonParameterReader.GetString(p, 3) : string.Empty };
                        AddField("Name", _text);
                    }
                    else if (condition >= 2)
                    {
                        string target = condition switch { 2 => "Class", 3 => "Skill", 4 => "Weapon", 5 => "Armor", _ => "State" };
                        _project2 = ProjectCombo(target, loadOriginal ? JsonParameterReader.GetInt(p, 3, 1) : 1);
                        AddField(target, _project2);
                    }
                    _option1.SelectedIndexChanged += (_, _) => RebuildFields(loadOriginal: false);
                    break;
                }
                case 5:
                    _number1 = Number(0, 7, loadOriginal ? JsonParameterReader.GetInt(p, 1) : 0);
                    _option1 = OptionCombo((0, "Appeared"), (1, "Affected by State"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    AddField("Enemy Index", _number1);
                    AddField("Condition", _option1);
                    if (ReadOption(_option1) == 1)
                    {
                        _project1 = ProjectCombo("State", loadOriginal ? JsonParameterReader.GetInt(p, 3, 1) : 1);
                        AddField("State", _project1);
                    }
                    _option1.SelectedIndexChanged += (_, _) => RebuildFields(loadOriginal: false);
                    break;
                case 6:
                    _project1 = CharacterCombo(loadOriginal ? JsonParameterReader.GetInt(p, 1) : 0);
                    _option1 = OptionCombo((2, "Down"), (4, "Left"), (6, "Right"), (8, "Up"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2, 2) : 2);
                    AddField("Character", _project1);
                    AddField("Direction", _option1);
                    break;
                case 7:
                    _number1 = Number(0, 99999999, loadOriginal ? JsonParameterReader.GetInt(p, 1) : 0);
                    _option1 = OptionCombo((0, "At least"), (1, "At most"), (2, "Less than"));
                    SelectOption(_option1, loadOriginal ? JsonParameterReader.GetInt(p, 2) : 0);
                    AddField("Gold", _number1);
                    AddField("Comparison", _option1);
                    break;
                case 8:
                    _project1 = ProjectCombo("Item", loadOriginal ? JsonParameterReader.GetInt(p, 1, 1) : 1);
                    AddField("Item", _project1);
                    break;
                case 9:
                case 10:
                    _project1 = ProjectCombo(type == 9 ? "Weapon" : "Armor", loadOriginal ? JsonParameterReader.GetInt(p, 1, 1) : 1);
                    _check = new CheckBox { Text = "Include Equipped", AutoSize = true, Checked = loadOriginal && JsonParameterReader.GetBool(p, 2) };
                    AddField(type == 9 ? "Weapon" : "Armor", _project1);
                    AddField("Equipment", _check);
                    break;
                case 11:
                    _option1 = StringCombo(("ok", "OK"), ("cancel", "Cancel"), ("shift", "Shift"), ("menu", "Menu"), ("pageup", "Page Up"), ("pagedown", "Page Down"), ("up", "Up"), ("down", "Down"), ("left", "Left"), ("right", "Right"));
                    SelectString(_option1, loadOriginal ? JsonParameterReader.GetString(p, 1) : "ok");
                    AddField("Button", _option1);
                    break;
                case 12:
                    _text = new TextBox
                    {
                        Dock = DockStyle.Fill,
                        Multiline = true,
                        AcceptsTab = true,
                        ScrollBars = ScrollBars.Both,
                        Height = 180,
                        Font = new Font(FontFamily.GenericMonospace, 10),
                        Text = loadOriginal ? JsonParameterReader.GetString(p, 1) : string.Empty
                    };
                    AddField("Script", _text);
                    break;
            }
        }
        finally
        {
            _fields.ResumeLayout();
        }
    }

    private void AddField(string label, Control control)
    {
        int row = _fields.RowCount++;
        _fields.RowStyles.Add(new RowStyle(control is TextBox { Multiline: true } ? SizeType.Percent : SizeType.AutoSize, control is TextBox { Multiline: true } ? 100 : 0));
        _fields.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        control.Dock = control is NumericUpDown ? DockStyle.Left : DockStyle.Fill;
        _fields.Controls.Add(control, 1, row);
    }

    private ComboBox ProjectCombo(string target, int selectedId)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (NamedProjectValue value in _context.Catalog.GetValues(target)) combo.Items.Add(value);
        NamedProjectValue? selected = combo.Items.Cast<NamedProjectValue>().FirstOrDefault(item => item.Id == selectedId);
        if (selected is not null) combo.SelectedItem = selected;
        else combo.Text = selectedId.ToString(CultureInfo.InvariantCulture);
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        combo.AutoCompleteSource = AutoCompleteSource.ListItems;
        return combo;
    }

    private ComboBox CharacterCombo(int selectedId)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add(new CharacterOption(-1, "Player"));
        combo.Items.Add(new CharacterOption(0, "This Event"));
        foreach (NamedProjectValue value in _context.Catalog.GetMapEvents()) combo.Items.Add(new CharacterOption(value.Id, value.Name));
        int index = combo.Items.Cast<CharacterOption>().ToList().FindIndex(item => item.Id == selectedId);
        combo.SelectedIndex = index >= 0 ? index : 1;
        return combo;
    }

    private static ComboBox OptionCombo(params (int Value, string Label)[] values)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach ((int value, string label) in values) combo.Items.Add(new NumericOption(value, label));
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    private static ComboBox StringCombo(params (string Value, string Label)[] values)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach ((string value, string label) in values) combo.Items.Add(new StringOption(value, label));
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 150,
        ThousandsSeparator = true
    };

    private static void SelectOption(ComboBox? combo, int value)
    {
        if (combo is null) return;
        int index = combo.Items.Cast<NumericOption>().ToList().FindIndex(item => item.Value == value);
        combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
    }

    private static void SelectString(ComboBox combo, string value)
    {
        int index = combo.Items.Cast<StringOption>().ToList().FindIndex(item => string.Equals(item.Value, value, StringComparison.Ordinal));
        combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
    }

    private static int ReadOption(ComboBox? combo) => (combo?.SelectedItem as NumericOption)?.Value ?? 0;
    private static int ReadNumber(NumericUpDown? value) => value is null ? 0 : decimal.ToInt32(value.Value);
    private static int ReadProjectId(ComboBox? combo)
    {
        if (combo?.SelectedItem is NamedProjectValue item) return item.Id;
        string text = combo?.Text.Trim() ?? "0";
        int colon = text.IndexOf(':');
        if (colon >= 0) text = text[..colon];
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
    private static int ReadCharacterId(ComboBox? combo) => (combo?.SelectedItem as CharacterOption)?.Id ?? 0;

    private sealed record NumericOption(int Value, string Label) { public override string ToString() => Label; }
    private sealed record StringOption(string Value, string Label) { public override string ToString() => Label; }
    private sealed record CharacterOption(int Id, string Name) { public override string ToString() => Id switch { -1 => "Player", 0 => "This Event", _ => $"{Id:D3}: {Name}" }; }
}

public sealed class ControlVariablesCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _first;
    private readonly ComboBox _last;
    private readonly ComboBox _operation = OptionCombo((0, "Set"), (1, "Add"), (2, "Subtract"), (3, "Multiply"), (4, "Divide"), (5, "Modulo"));
    private readonly ComboBox _operandType = OptionCombo((0, "Constant"), (1, "Variable"), (2, "Random"), (3, "Game Data"), (4, "Script"));
    private readonly Panel _operandHost = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
    private NumericUpDown? _number1;
    private NumericUpDown? _number2;
    private ComboBox? _variable;
    private ComboBox? _gameDataType;
    private ComboBox? _gameDataRecord;
    private TextBox? _script;

    public ControlVariablesCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        _first = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 0, 1));
        _last = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 1, 1));
        SelectOption(_operation, JsonParameterReader.GetInt(p, 2));
        SelectOption(_operandType, JsonParameterReader.GetInt(p, 3));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(0, "First Variable", _first);
        AddRow(1, "Last Variable", _last);
        AddRow(2, "Operation", _operation);
        AddRow(3, "Operand Type", _operandType);
        _view.Controls.Add(new Label { Text = "Operand:", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, 4);
        _view.Controls.Add(_operandHost, 1, 4);
        _view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _operandType.SelectedIndexChanged += (_, _) => RebuildOperand(loadOriginal: false);
        RebuildOperand(loadOriginal: true);
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        int type = ReadOption(_operandType);
        JsonArray p = [ReadProjectId(_first), ReadProjectId(_last), ReadOption(_operation), type];
        switch (type)
        {
            case 0: p.Add(ReadNumber(_number1)); break;
            case 1: p.Add(ReadProjectId(_variable)); break;
            case 2:
                p.Add(ReadNumber(_number1));
                p.Add(ReadNumber(_number2));
                break;
            case 3:
                p.Add(ReadOption(_gameDataType));
                p.Add(ReadProjectId(_gameDataRecord));
                p.Add(ReadNumber(_number2));
                break;
            case 4: p.Add(_script?.Text ?? string.Empty); break;
        }
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, p.ToJsonString());
    }

    public IReadOnlyList<string> Validate()
    {
        if (ReadProjectId(_first) > ReadProjectId(_last)) return ["The first variable ID cannot be greater than the last variable ID."];
        if (ReadOption(_operandType) == 2 && ReadNumber(_number1) > ReadNumber(_number2)) return ["The random minimum cannot exceed the random maximum."];
        if (ReadOption(_operandType) == 4 && string.IsNullOrWhiteSpace(_script?.Text)) return ["The script operand is empty."];
        return [];
    }

    private void RebuildOperand(bool loadOriginal)
    {
        JsonElement p = JsonParameterReader.ParseArray(_context.Node.RawCommands[0].ParametersRawJson);
        int type = ReadOption(_operandType);
        _operandHost.Controls.Clear();
        _number1 = null;
        _number2 = null;
        _variable = null;
        _gameDataType = null;
        _gameDataRecord = null;
        _script = null;
        TableLayoutPanel panel = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
            panel.Controls.Add(control, 1, row);
        }
        switch (type)
        {
            case 0:
                _number1 = Number(-99999999, 99999999, loadOriginal ? JsonParameterReader.GetInt(p, 4) : 0);
                Row("Constant", _number1);
                break;
            case 1:
                _variable = ProjectCombo("Variable", loadOriginal ? JsonParameterReader.GetInt(p, 4, 1) : 1);
                Row("Variable", _variable);
                break;
            case 2:
                _number1 = Number(-99999999, 99999999, loadOriginal ? JsonParameterReader.GetInt(p, 4) : 0);
                _number2 = Number(-99999999, 99999999, loadOriginal ? JsonParameterReader.GetInt(p, 5) : 0);
                Row("Minimum", _number1);
                Row("Maximum", _number2);
                break;
            case 3:
                _gameDataType = OptionCombo((0, "Item Count"), (1, "Weapon Count"), (2, "Armor Count"), (3, "Actor Data"), (4, "Enemy Data"), (5, "Character Data"), (6, "Party Member ID"), (7, "Other"));
                SelectOption(_gameDataType, loadOriginal ? JsonParameterReader.GetInt(p, 4) : 0);
                _gameDataRecord = ProjectCombo(GameDataTarget(ReadOption(_gameDataType)), loadOriginal ? JsonParameterReader.GetInt(p, 5) : 1);
                _number2 = Number(0, 9999, loadOriginal ? JsonParameterReader.GetInt(p, 6) : 0);
                Row("Game Data", _gameDataType);
                Row("Record", _gameDataRecord);
                Row("Property", _number2);
                _gameDataType.SelectedIndexChanged += (_, _) => RebuildOperand(loadOriginal: false);
                break;
            case 4:
                _script = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 150, AcceptsTab = true, Font = new Font(FontFamily.GenericMonospace, 10), Text = loadOriginal ? JsonParameterReader.GetString(p, 4) : string.Empty };
                Row("Script", _script);
                break;
        }
        _operandHost.Controls.Add(panel);
    }

    private string GameDataTarget(int type) => type switch { 0 => "Item", 1 => "Weapon", 2 => "Armor", 3 => "Actor", 4 => "Enemy", _ => "Variable" };

    private ComboBox ProjectCombo(string target, int selectedId)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (NamedProjectValue value in _context.Catalog.GetValues(target)) combo.Items.Add(value);
        NamedProjectValue? selected = combo.Items.Cast<NamedProjectValue>().FirstOrDefault(item => item.Id == selectedId);
        if (selected is not null) combo.SelectedItem = selected;
        else combo.Text = selectedId.ToString(CultureInfo.InvariantCulture);
        return combo;
    }

    private void AddRow(int row, string label, Control control)
    {
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
    }

    private static ComboBox OptionCombo(params (int Value, string Label)[] values)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach ((int value, string label) in values) combo.Items.Add(new NumericOption(value, label));
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 160, ThousandsSeparator = true };
    private static void SelectOption(ComboBox combo, int value) { int index = combo.Items.Cast<NumericOption>().ToList().FindIndex(item => item.Value == value); combo.SelectedIndex = index >= 0 ? index : 0; }
    private static int ReadOption(ComboBox? combo) => (combo?.SelectedItem as NumericOption)?.Value ?? 0;
    private static int ReadNumber(NumericUpDown? value) => value is null ? 0 : decimal.ToInt32(value.Value);
    private static int ReadProjectId(ComboBox? combo)
    {
        if (combo?.SelectedItem is NamedProjectValue item) return item.Id;
        string text = combo?.Text ?? "0";
        int colon = text.IndexOf(':'); if (colon >= 0) text = text[..colon];
        return int.TryParse(text.Trim(), out int value) ? value : 0;
    }
    private sealed record NumericOption(int Value, string Label) { public override string ToString() => Label; }
}

public sealed class TransferPlayerCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _designation = OptionCombo((0, "Direct designation"), (1, "Designation with variables"));
    private readonly MapLocationPickerControl _location;
    private readonly ComboBox _mapVariable;
    private readonly ComboBox _xVariable;
    private readonly ComboBox _yVariable;
    private readonly ComboBox _direction = OptionCombo((0, "Retain"), (2, "Down"), (4, "Left"), (6, "Right"), (8, "Up"));
    private readonly ComboBox _fade = OptionCombo((0, "Black"), (1, "White"), (2, "None"));
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };

    public TransferPlayerCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        SelectOption(_designation, JsonParameterReader.GetInt(p, 0));
        _location = new MapLocationPickerControl(context.Project)
        {
            Value = new SystemPosition(
                JsonParameterReader.GetInt(p, 1, 1),
                JsonParameterReader.GetInt(p, 2),
                JsonParameterReader.GetInt(p, 3))
        };
        _mapVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 1, 1));
        _xVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 2, 1));
        _yVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 3, 1));
        SelectOption(_direction, JsonParameterReader.GetInt(p, 4));
        SelectOption(_fade, JsonParameterReader.GetInt(p, 5));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        BuildRows();
        _designation.SelectedIndexChanged += (_, _) => BuildRows();
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        int designation = ReadOption(_designation);
        SystemPosition location = _location.Value;
        JsonArray p = designation == 0
            ? [designation, location.MapId, location.X, location.Y, ReadOption(_direction), ReadOption(_fade)]
            : [designation, ReadProjectId(_mapVariable), ReadProjectId(_xVariable), ReadProjectId(_yVariable), ReadOption(_direction), ReadOption(_fade)];
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, p.ToJsonString());
    }

    public IReadOnlyList<string> Validate() => [];

    private void BuildRows()
    {
        _view.SuspendLayout();
        try
        {
            _view.Controls.Clear(); _view.RowStyles.Clear(); _view.RowCount = 0;
            AddRow("Location", _designation);
            if (ReadOption(_designation) == 0)
            {
                AddRow("Map Location", _location);
            }
            else
            {
                AddRow("Map Variable", _mapVariable); AddRow("X Variable", _xVariable); AddRow("Y Variable", _yVariable);
            }
            AddRow("Direction", _direction); AddRow("Fade", _fade);
        }
        finally { _view.ResumeLayout(); }
    }

    private void AddRow(string label, Control control)
    {
        int row = _view.RowCount++; _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
    }
    private ComboBox ProjectCombo(string target, int id) { ComboBox c = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown }; foreach (NamedProjectValue v in _context.Catalog.GetValues(target)) c.Items.Add(v); NamedProjectValue? selected = c.Items.Cast<NamedProjectValue>().FirstOrDefault(v => v.Id == id); if (selected is not null) c.SelectedItem = selected; else c.Text = id.ToString(CultureInfo.InvariantCulture); return c; }
    private static ComboBox OptionCombo(params (int Value, string Label)[] values) { ComboBox c = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }; foreach (var v in values) c.Items.Add(new NumericOption(v.Value, v.Label)); c.SelectedIndex = 0; return c; }
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Width = 150 };
    private static void SelectOption(ComboBox c, int value) { int i = c.Items.Cast<NumericOption>().ToList().FindIndex(x => x.Value == value); c.SelectedIndex = i >= 0 ? i : 0; }
    private static int ReadOption(ComboBox c) => (c.SelectedItem as NumericOption)?.Value ?? 0;
    private static int ReadProjectId(ComboBox c) { if (c.SelectedItem is NamedProjectValue v) return v.Id; string text = c.Text; int colon = text.IndexOf(':'); if (colon >= 0) text = text[..colon]; return int.TryParse(text.Trim(), out int id) ? id : 0; }
    private sealed record NumericOption(int Value, string Label) { public override string ToString() => Label; }
}

public sealed class VehicleLocationCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _vehicle = OptionCombo((0, "Boat"), (1, "Ship"), (2, "Airship"));
    private readonly ComboBox _designation = OptionCombo((0, "Direct designation"), (1, "Designation with variables"));
    private readonly MapLocationPickerControl _location;
    private readonly ComboBox _mapVariable;
    private readonly ComboBox _xVariable;
    private readonly ComboBox _yVariable;
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };

    public VehicleLocationCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        SelectOption(_vehicle, JsonParameterReader.GetInt(p, 0));
        SelectOption(_designation, JsonParameterReader.GetInt(p, 1));
        _location = new MapLocationPickerControl(context.Project)
        {
            Value = new SystemPosition(
                JsonParameterReader.GetInt(p, 2, 1),
                JsonParameterReader.GetInt(p, 3),
                JsonParameterReader.GetInt(p, 4))
        };
        _mapVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 2, 1));
        _xVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 3, 1));
        _yVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 4, 1));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        BuildRows();
        _designation.SelectedIndexChanged += (_, _) => BuildRows();
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        int designation = ReadOption(_designation);
        SystemPosition location = _location.Value;
        JsonArray p = designation == 0
            ? [ReadOption(_vehicle), designation, location.MapId, location.X, location.Y]
            : [ReadOption(_vehicle), designation, ReadProjectId(_mapVariable), ReadProjectId(_xVariable), ReadProjectId(_yVariable)];
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, p.ToJsonString());
    }

    public IReadOnlyList<string> Validate() => [];

    private void BuildRows()
    {
        _view.SuspendLayout();
        try
        {
            _view.Controls.Clear();
            _view.RowStyles.Clear();
            _view.RowCount = 0;
            AddRow("Vehicle", _vehicle);
            AddRow("Designation", _designation);
            if (ReadOption(_designation) == 0) AddRow("Map Location", _location);
            else
            {
                AddRow("Map Variable", _mapVariable);
                AddRow("X Variable", _xVariable);
                AddRow("Y Variable", _yVariable);
            }
        }
        finally { _view.ResumeLayout(); }
    }

    private void AddRow(string label, Control control)
    {
        int row = _view.RowCount++;
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
    }

    private ComboBox ProjectCombo(string target, int id)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (NamedProjectValue value in _context.Catalog.GetValues(target)) combo.Items.Add(value);
        NamedProjectValue? selected = combo.Items.Cast<NamedProjectValue>().FirstOrDefault(value => value.Id == id);
        if (selected is not null) combo.SelectedItem = selected;
        else combo.Text = id.ToString(CultureInfo.InvariantCulture);
        return combo;
    }

    private static ComboBox OptionCombo(params (int Value, string Label)[] values)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach ((int value, string label) in values) combo.Items.Add(new NumericOption(value, label));
        combo.SelectedIndex = 0;
        return combo;
    }
    private static void SelectOption(ComboBox combo, int value)
    {
        int index = combo.Items.Cast<NumericOption>().ToList().FindIndex(item => item.Value == value);
        combo.SelectedIndex = index >= 0 ? index : 0;
    }
    private static int ReadOption(ComboBox combo) => (combo.SelectedItem as NumericOption)?.Value ?? 0;
    private static int ReadProjectId(ComboBox combo)
    {
        if (combo.SelectedItem is NamedProjectValue value) return value.Id;
        string text = combo.Text;
        int colon = text.IndexOf(':');
        if (colon >= 0) text = text[..colon];
        return int.TryParse(text.Trim(), out int id) ? id : 0;
    }
    private sealed record NumericOption(int Value, string Label) { public override string ToString() => Label; }
}

public sealed class EventLocationCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _character;
    private readonly ComboBox _designation = OptionCombo((0, "Direct coordinates"), (1, "Coordinates in variables"), (2, "Exchange with another event"));
    private readonly NumericUpDown _x = Number(-9999, 9999, 0);
    private readonly NumericUpDown _y = Number(-9999, 9999, 0);
    private readonly MapLocationPickerControl? _location;
    private readonly ComboBox _xVariable;
    private readonly ComboBox _yVariable;
    private readonly ComboBox _exchange;
    private readonly ComboBox _direction = OptionCombo((0, "Retain"), (2, "Down"), (4, "Left"), (6, "Right"), (8, "Up"));
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };

    public EventLocationCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        _character = CharacterCombo(JsonParameterReader.GetInt(p, 0));
        SelectOption(_designation, JsonParameterReader.GetInt(p, 1));
        _x.Value = Math.Clamp(JsonParameterReader.GetInt(p, 2), -9999, 9999);
        _y.Value = Math.Clamp(JsonParameterReader.GetInt(p, 3), -9999, 9999);
        int currentMapId = context.Address.MapId ?? 0;
        if (currentMapId > 0)
        {
            _location = new MapLocationPickerControl(context.Project, allowMapChange: false, fixedMapId: currentMapId)
            {
                Value = new SystemPosition(currentMapId, JsonParameterReader.GetInt(p, 2), JsonParameterReader.GetInt(p, 3))
            };
        }
        _xVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 2, 1));
        _yVariable = ProjectCombo("Variable", JsonParameterReader.GetInt(p, 3, 1));
        _exchange = CharacterCombo(JsonParameterReader.GetInt(p, 2));
        SelectOption(_direction, JsonParameterReader.GetInt(p, 4));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        BuildRows(); _designation.SelectedIndexChanged += (_, _) => BuildRows();
    }

    public Control View => _view;
    public IReadOnlyList<string> BuildCommands()
    {
        int mode = ReadOption(_designation); int a; int b;
        if (mode == 0) { if (_location is not null) { SystemPosition location = _location.Value; a = location.X; b = location.Y; } else { a = decimal.ToInt32(_x.Value); b = decimal.ToInt32(_y.Value); } }
        else if (mode == 1) { a = ReadProjectId(_xVariable); b = ReadProjectId(_yVariable); }
        else { a = ReadCharacterId(_exchange); b = 0; }
        JsonArray p = [ReadCharacterId(_character), mode, a, b, ReadOption(_direction)];
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, p.ToJsonString());
    }
    public IReadOnlyList<string> Validate() => ReadOption(_designation) == 2 && ReadCharacterId(_exchange) == ReadCharacterId(_character) ? ["An event cannot exchange location with itself."] : [];
    private void BuildRows() { _view.SuspendLayout(); try { _view.Controls.Clear(); _view.RowStyles.Clear(); _view.RowCount = 0; AddRow("Character", _character); AddRow("Location", _designation); if (ReadOption(_designation)==0) { if (_location is not null) AddRow("Map Location",_location); else { AddRow("X",_x); AddRow("Y",_y); } } else if (ReadOption(_designation)==1) { AddRow("X Variable",_xVariable); AddRow("Y Variable",_yVariable); } else AddRow("Exchange With",_exchange); AddRow("Direction",_direction); } finally { _view.ResumeLayout(); } }
    private void AddRow(string label, Control c) { int row=_view.RowCount++; _view.RowStyles.Add(new RowStyle(SizeType.AutoSize)); _view.Controls.Add(new Label { Text=label+":",AutoSize=true,Margin=new Padding(3,8,10,3)},0,row); _view.Controls.Add(c,1,row); }
    private ComboBox ProjectCombo(string target,int id) { ComboBox c=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDown}; foreach(NamedProjectValue v in _context.Catalog.GetValues(target)) c.Items.Add(v); NamedProjectValue? selected=c.Items.Cast<NamedProjectValue>().FirstOrDefault(v=>v.Id==id); if(selected is not null)c.SelectedItem=selected;else c.Text=id.ToString(CultureInfo.InvariantCulture);return c; }
    private ComboBox CharacterCombo(int id) { ComboBox c=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList}; c.Items.Add(new CharacterOption(-1,"Player"));c.Items.Add(new CharacterOption(0,"This Event"));foreach(NamedProjectValue v in _context.Catalog.GetMapEvents())c.Items.Add(new CharacterOption(v.Id,v.Name));int i=c.Items.Cast<CharacterOption>().ToList().FindIndex(v=>v.Id==id);c.SelectedIndex=i>=0?i:1;return c; }
    private static ComboBox OptionCombo(params (int Value,string Label)[] values){ComboBox c=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};foreach(var v in values)c.Items.Add(new NumericOption(v.Value,v.Label));c.SelectedIndex=0;return c;}
    private static NumericUpDown Number(decimal min,decimal max,decimal value)=>new(){Minimum=min,Maximum=max,Value=value,Width=150};
    private static void SelectOption(ComboBox c,int v){int i=c.Items.Cast<NumericOption>().ToList().FindIndex(x=>x.Value==v);c.SelectedIndex=i>=0?i:0;}
    private static int ReadOption(ComboBox c)=>(c.SelectedItem as NumericOption)?.Value??0;
    private static int ReadProjectId(ComboBox c){if(c.SelectedItem is NamedProjectValue v)return v.Id;return int.TryParse(c.Text.Split(':')[0],out int id)?id:0;}
    private static int ReadCharacterId(ComboBox c)=>(c.SelectedItem as CharacterOption)?.Id??0;
    private sealed record NumericOption(int Value,string Label){public override string ToString()=>Label;}
    private sealed record CharacterOption(int Id,string Name){public override string ToString()=>Id switch{-1=>"Player",0=>"This Event",_=>$"{Id:D3}: {Name}"};}
}

public sealed class MovementRouteCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _character;
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3) };
    private readonly Panel _view = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
    private MovementRouteModel _route;

    public MovementRouteCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        _character = CharacterCombo(JsonParameterReader.GetInt(p, 0));
        _route = JsonParameterReader.TryGet(p, 1, out JsonElement route) && route.ValueKind == JsonValueKind.Object
            ? MovementRouteModel.Parse(route.GetRawText())
            : MovementRouteModel.CreateDefault();
        TableLayoutPanel layout = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Character:", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, 0);
        layout.Controls.Add(_character, 1, 0);
        layout.Controls.Add(new Label { Text = "Route:", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, 1);
        layout.Controls.Add(_summary, 1, 1);
        Button edit = new() { Text = "Open Movement Route Studio…", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        edit.Click += (_, _) => EditRoute();
        layout.Controls.Add(edit, 1, 2);
        Button yep = new() { Text = "Add YEP Route Script…", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        yep.Click += async (_, _) => await AddYanflyScriptAsync().ConfigureAwait(true);
        layout.Controls.Add(yep, 1, 3);
        layout.Controls.Add(_status, 1, 4);
        _view.Controls.Add(layout);
        UpdateSummary();
    }

    public Control View => _view;
    public IReadOnlyList<string> BuildCommands() => MovementRouteSynchronizer.CreateEventCommandRows(ReadCharacterId(_character), _route, _context.Node.RawCommands[0].Indent);
    public IReadOnlyList<string> Validate() => [];

    private void EditRoute()
    {
        using MovementRouteEditorDialog dialog = new(_context.Registry, _route, _context.Project, _context.Catalog);
        if (dialog.ShowDialog(_view.FindForm()) == DialogResult.OK)
        {
            _route = dialog.Result;
            UpdateSummary();
        }
    }

    private async Task AddYanflyScriptAsync()
    {
        try
        {
            PluginTagProjectIntelligence intelligence = await YanflyPluginTagProjectCache
                .GetAsync(_context.Project.Workspace.Snapshot.Identity.ProjectRoot, PluginTagCatalogHost.Catalog)
                .ConfigureAwait(true);
            using YanflyCommandPickerDialog dialog = new(_context.Project, PluginTagCatalogHost.Catalog,
                PluginAuthoringSurface.MoveRouteScript, intelligence.Availability);
            if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.ResultText)) return;
            List<MovementRouteCommandModel> commands = _route.Commands.Where(command => command.Code != 0).ToList();
            commands.Add(MovementRouteCommandModel.Create(45, JsonSerializer.Serialize(new object?[] { dialog.ResultText })));
            commands.Add(MovementRouteCommandModel.Create(0));
            _route = _route with { Commands = commands };
            _status.Text = "Inserted YEP helper as movement-route Script command 45.";
            UpdateSummary();
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void UpdateSummary() => _summary.Text = $"{_route.Commands.Count(command => command.Code != 0)} command(s); repeat {(_route.Repeat ? "yes" : "no")}; skip {(_route.Skippable ? "yes" : "no")}; wait {(_route.Wait ? "yes" : "no")}";

    private ComboBox CharacterCombo(int id)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add(new CharacterOption(-1, "Player"));
        combo.Items.Add(new CharacterOption(0, "This Event"));
        foreach (NamedProjectValue value in _context.Catalog.GetMapEvents()) combo.Items.Add(new CharacterOption(value.Id, value.Name));
        int index = combo.Items.Cast<CharacterOption>().ToList().FindIndex(value => value.Id == id);
        combo.SelectedIndex = index >= 0 ? index : 1;
        return combo;
    }

    private static int ReadCharacterId(ComboBox combo) => (combo.SelectedItem as CharacterOption)?.Id ?? 0;
    private sealed record CharacterOption(int Id, string Name)
    {
        public override string ToString() => Id switch { -1 => "Player", 0 => "This Event", _ => $"{Id:D3}: {Name}" };
    }
}

public sealed class BattleProcessingCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly ComboBox _designation=OptionCombo((0,"Direct designation"),(1,"Designation with a variable"),(2,"Same as random encounter"));
    private readonly ComboBox _troop;
    private readonly ComboBox _variable;
    private readonly CheckBox _escape=new(){Text="Can Escape",AutoSize=true};
    private readonly CheckBox _lose=new(){Text="Can Lose",AutoSize=true};
    private readonly TableLayoutPanel _view=new(){Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(12)};
    public BattleProcessingCommandEditorPanel(EventCommandEditContext context){_context=context;JsonElement p=JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);SelectOption(_designation,JsonParameterReader.GetInt(p,0));_troop=ProjectCombo("Troop",JsonParameterReader.GetInt(p,1,1));_variable=ProjectCombo("Variable",JsonParameterReader.GetInt(p,1,1));_escape.Checked=JsonParameterReader.GetBool(p,2);_lose.Checked=JsonParameterReader.GetBool(p,3);_view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));_view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));BuildRows();_designation.SelectedIndexChanged+=(_,_)=>BuildRows();}
    public Control View=>_view;
    public IReadOnlyList<string> BuildCommands(){int d=ReadOption(_designation);JsonArray p=[d,d==0?ReadProjectId(_troop):d==1?ReadProjectId(_variable):0,_escape.Checked,_lose.Checked];return EventStructuralCommandSynchronizer.Rewrite(_context.Node,p.ToJsonString());}
    public IReadOnlyList<string> Validate()=>[];
    private void BuildRows(){_view.SuspendLayout();try{_view.Controls.Clear();_view.RowStyles.Clear();_view.RowCount=0;AddRow("Troop",_designation);if(ReadOption(_designation)==0)AddRow("Troop",_troop);else if(ReadOption(_designation)==1)AddRow("Variable",_variable);AddRow("Result Branches",_escape);AddRow("",_lose);Label help=new(){Text="Win, Escape, and Lose branch bodies are preserved while their corresponding options remain enabled.",AutoSize=true,ForeColor=SystemColors.GrayText,Padding=new Padding(3,8,3,3)};_view.Controls.Add(help,0,_view.RowCount);_view.SetColumnSpan(help,2);}finally{_view.ResumeLayout();}}
    private void AddRow(string label,Control c){int r=_view.RowCount++;_view.RowStyles.Add(new RowStyle(SizeType.AutoSize));_view.Controls.Add(new Label{Text=string.IsNullOrEmpty(label)?string.Empty:label+":",AutoSize=true,Margin=new Padding(3,8,10,3)},0,r);_view.Controls.Add(c,1,r);}
    private ComboBox ProjectCombo(string target,int id){ComboBox c=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDown};foreach(NamedProjectValue v in _context.Catalog.GetValues(target))c.Items.Add(v);NamedProjectValue? s=c.Items.Cast<NamedProjectValue>().FirstOrDefault(v=>v.Id==id);if(s is not null)c.SelectedItem=s;else c.Text=id.ToString(CultureInfo.InvariantCulture);return c;}
    private static ComboBox OptionCombo(params (int Value,string Label)[] values){ComboBox c=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};foreach(var v in values)c.Items.Add(new NumericOption(v.Value,v.Label));c.SelectedIndex=0;return c;}
    private static void SelectOption(ComboBox c,int v){int i=c.Items.Cast<NumericOption>().ToList().FindIndex(x=>x.Value==v);c.SelectedIndex=i>=0?i:0;}
    private static int ReadOption(ComboBox c)=>(c.SelectedItem as NumericOption)?.Value??0;
    private static int ReadProjectId(ComboBox c){if(c.SelectedItem is NamedProjectValue v)return v.Id;return int.TryParse(c.Text.Split(':')[0],out int id)?id:0;}
    private sealed record NumericOption(int Value,string Label){public override string ToString()=>Label;}
}

public sealed class ShopProcessingCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly List<ShopGood> _goods=[];
    private readonly DataGridView _grid=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    private readonly CheckBox _purchaseOnly=new(){Text="Purchase Only",AutoSize=true};
    private readonly Panel _view=new(){Dock=DockStyle.Fill,Padding=new Padding(12)};
    public ShopProcessingCommandEditorPanel(EventCommandEditContext context){_context=context;ParseGoods();_grid.Columns.Add("Type","Type");_grid.Columns.Add("Item","Goods");_grid.Columns.Add("Price","Price");FlowLayoutPanel tools=new(){Dock=DockStyle.Top,Height=40,WrapContents=false};AddButton(tools,"Add",AddGood);AddButton(tools,"Edit",EditGood);AddButton(tools,"Delete",DeleteGood);AddButton(tools,"Up",()=>Move(-1));AddButton(tools,"Down",()=>Move(1));Panel top=new(){Dock=DockStyle.Top,Height=34};top.Controls.Add(_purchaseOnly);_view.Controls.Add(_grid);_view.Controls.Add(tools);_view.Controls.Add(top);RefreshGrid();}
    public Control View=>_view;
    public IReadOnlyList<string> BuildCommands(){if(_goods.Count==0)return[];RawEventCommand opening=_context.Node.RawCommands[0];List<string> rows=[];for(int i=0;i<_goods.Count;i++){ShopGood g=_goods[i];JsonArray p=[g.Type,g.Id,g.PriceType,g.Price];if(i==0)p.Add(_purchaseOnly.Checked);rows.Add(i==0?EventCommandFactory.Rewrite(opening,parametersRawJson:p.ToJsonString()):RawEventCommand.CreateJson(605,opening.Indent,p.ToJsonString()));}return rows;}
    public IReadOnlyList<string> Validate()=>_goods.Count==0?["At least one shop good is required."]:[];
    private void ParseGoods(){foreach(RawEventCommand command in _context.Node.RawCommands.Where(c=>c.Code is 302 or 605)){JsonElement p=JsonParameterReader.ParseArray(command.ParametersRawJson);_goods.Add(new ShopGood(JsonParameterReader.GetInt(p,0),JsonParameterReader.GetInt(p,1),JsonParameterReader.GetInt(p,2),JsonParameterReader.GetInt(p,3)));if(command.Code==302)_purchaseOnly.Checked=JsonParameterReader.GetBool(p,4);}}
    private void AddGood(){using ShopGoodDialog dialog=new(_context.Catalog,null);if(dialog.ShowDialog(_view.FindForm())!=DialogResult.OK||dialog.Result is null)return;_goods.Add(dialog.Result);RefreshGrid(_goods.Count-1);}
    private void EditGood(){int i=_grid.CurrentRow?.Index??-1;if(i<0)return;using ShopGoodDialog dialog=new(_context.Catalog,_goods[i]);if(dialog.ShowDialog(_view.FindForm())!=DialogResult.OK||dialog.Result is null)return;_goods[i]=dialog.Result;RefreshGrid(i);}
    private void DeleteGood(){int i=_grid.CurrentRow?.Index??-1;if(i<0)return;_goods.RemoveAt(i);RefreshGrid(Math.Min(i,_goods.Count-1));}
    private void Move(int d){int i=_grid.CurrentRow?.Index??-1,t=i+d;if(i<0||t<0||t>=_goods.Count)return;(_goods[i],_goods[t])=(_goods[t],_goods[i]);RefreshGrid(t);}
    private void RefreshGrid(int selected=0){_grid.Rows.Clear();foreach(ShopGood g in _goods){string target=g.Type switch{0=>"Item",1=>"Weapon",2=>"Armor",_=>"Goods"};string name=_context.Catalog.Resolve(target,g.Id);_grid.Rows.Add(target,string.IsNullOrWhiteSpace(name)?$"#{g.Id}":$"#{g.Id}: {name}",g.PriceType==0?"Standard":g.Price.ToString("N0",CultureInfo.InvariantCulture));}if(_grid.Rows.Count>0)_grid.CurrentCell=_grid.Rows[Math.Clamp(selected,0,_grid.Rows.Count-1)].Cells[1];}
    private static void AddButton(Control p,string text,Action action){Button b=new(){Text=text,AutoSize=true};b.Click+=(_,_)=>action();p.Controls.Add(b);}
    internal sealed record ShopGood(int Type,int Id,int PriceType,int Price);
}

internal sealed class ShopGoodDialog : Form
{
    private readonly IEventCommandProjectCatalog _catalog;
    private readonly ComboBox _type=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox _item=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDown};
    private readonly ComboBox _priceType=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown _price=new(){Minimum=0,Maximum=99999999,ThousandsSeparator=true,Dock=DockStyle.Left,Width=160};
    public ShopGoodDialog(IEventCommandProjectCatalog catalog,ShopProcessingCommandEditorPanel.ShopGood? current){_catalog=catalog;Text=current is null?"Add Shop Good":"Edit Shop Good";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(520,240);Padding=new Padding(12);_type.Items.AddRange([new Option(0,"Item"),new Option(1,"Weapon"),new Option(2,"Armor")]);_priceType.Items.AddRange([new Option(0,"Standard Price"),new Option(1,"Specify Price")]);_type.SelectedIndex=current?.Type??0;_priceType.SelectedIndex=current?.PriceType??0;_price.Value=current?.Price??0;TableLayoutPanel layout=new(){Dock=DockStyle.Fill,ColumnCount=2};layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));AddRow(layout,0,"Type",_type);AddRow(layout,1,"Goods",_item);AddRow(layout,2,"Price",_priceType);AddRow(layout,3,"Amount",_price);FlowLayoutPanel buttons=new(){Dock=DockStyle.Bottom,Height=44,FlowDirection=FlowDirection.RightToLeft};Button ok=new(){Text="OK",AutoSize=true};Button cancel=new(){Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.AddRange([cancel,ok]);Controls.Add(layout);Controls.Add(buttons);AcceptButton=ok;CancelButton=cancel;_type.SelectedIndexChanged+=(_,_)=>LoadItems(current?.Id??1);_priceType.SelectedIndexChanged+=(_,_)=>_price.Enabled=(_priceType.SelectedItem as Option)?.Value==1;ok.Click+=(_,_)=>Confirm();LoadItems(current?.Id??1);}
    public ShopProcessingCommandEditorPanel.ShopGood? Result{get;private set;}
    private void LoadItems(int selectedId){string target=(_type.SelectedItem as Option)?.Value switch{1=>"Weapon",2=>"Armor",_=>"Item"};_item.Items.Clear();foreach(NamedProjectValue v in _catalog.GetValues(target))_item.Items.Add(v);NamedProjectValue? selected=_item.Items.Cast<NamedProjectValue>().FirstOrDefault(v=>v.Id==selectedId);if(selected is not null)_item.SelectedItem=selected;else _item.Text=selectedId.ToString(CultureInfo.InvariantCulture);}
    private void Confirm(){int id=_item.SelectedItem is NamedProjectValue v?v.Id:int.TryParse(_item.Text.Split(':')[0],out int parsed)?parsed:0;if(id<=0){MessageBox.Show(this,"Choose a shop good.",Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}Result=new ShopProcessingCommandEditorPanel.ShopGood((_type.SelectedItem as Option)?.Value??0,id,(_priceType.SelectedItem as Option)?.Value??0,decimal.ToInt32(_price.Value));DialogResult=DialogResult.OK;Close();}
    private static void AddRow(TableLayoutPanel t,int row,string label,Control c){t.RowStyles.Add(new RowStyle(SizeType.AutoSize));t.Controls.Add(new Label{Text=label+":",AutoSize=true,Margin=new Padding(3,8,10,3)},0,row);t.Controls.Add(c,1,row);}
    private sealed record Option(int Value,string Label){public override string ToString()=>Label;}
}

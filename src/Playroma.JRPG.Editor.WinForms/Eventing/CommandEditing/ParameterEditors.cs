using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

internal interface IParameterValueEditor
{
    Control View { get; }
    JsonNode? BuildValue();
    IReadOnlyList<string> Validate();
}

internal static class ParameterValueEditorFactory
{
    public static IParameterValueEditor Create(
        EventCommandEditContext context,
        EventCommandParameterDescriptor descriptor,
        JsonElement current)
    {
        return descriptor.Kind switch
        {
            EventParameterKind.Integer => new IntegerParameterEditor(descriptor, current),
            EventParameterKind.Boolean => new BooleanParameterEditor(descriptor, current),
            EventParameterKind.String => new StringParameterEditor(descriptor, current),
            EventParameterKind.StringArray => new StringArrayParameterEditor(descriptor, current),
            EventParameterKind.Enum => new EnumParameterEditor(descriptor, current),
            EventParameterKind.DatabaseId => new DatabaseIdParameterEditor(context.Catalog, descriptor, current),
            EventParameterKind.Asset => new AssetParameterEditor(context, descriptor, current),
            EventParameterKind.Audio => new AudioParameterEditor(context, descriptor, current),
            EventParameterKind.Tone => new VectorParameterEditor(descriptor, current, tone: true),
            EventParameterKind.Color => new VectorParameterEditor(descriptor, current, tone: false),
            _ => new JsonParameterEditor(descriptor, current)
        };
    }
}

internal sealed class IntegerParameterEditor : IParameterValueEditor
{
    private readonly NumericUpDown _value;
    private readonly EventCommandParameterDescriptor _descriptor;

    public IntegerParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _descriptor = descriptor;
        decimal minimum = descriptor.Minimum ?? -1_000_000m;
        decimal maximum = descriptor.Maximum ?? 1_000_000m;
        if (maximum < minimum) (minimum, maximum) = (maximum, minimum);
        _value = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            ThousandsSeparator = true
        };
        decimal parsed = current.ValueKind == JsonValueKind.Number && current.TryGetDecimal(out decimal number) ? number : 0m;
        _value.Value = Math.Clamp(parsed, minimum, maximum);
    }

    public Control View => _value;
    public JsonNode BuildValue() => JsonValue.Create(decimal.ToInt32(_value.Value))!;
    public IReadOnlyList<string> Validate() => [];
}

internal sealed class BooleanParameterEditor : IParameterValueEditor
{
    private readonly CheckBox _value = new() { Text = "Enabled", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    public BooleanParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _value.Text = descriptor.DisplayName;
        _value.Checked = current.ValueKind == JsonValueKind.True;
    }

    public Control View => _value;
    public JsonNode BuildValue() => JsonValue.Create(_value.Checked)!;
    public IReadOnlyList<string> Validate() => [];
}

internal sealed class StringParameterEditor : IParameterValueEditor
{
    private readonly TextBox _value = new() { Dock = DockStyle.Fill };

    public StringParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _value.Text = current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    public Control View => _value;
    public JsonNode BuildValue() => JsonValue.Create(_value.Text)!;
    public IReadOnlyList<string> Validate() => [];
}

internal sealed class EnumParameterEditor : IParameterValueEditor
{
    private readonly ComboBox _value = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<EnumOptionItem> _items = [];

    public EnumParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        string currentKey = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            _ => string.Empty
        };
        foreach ((string key, string label) in descriptor.Options ?? new Dictionary<string, string>())
        {
            _items.Add(new EnumOptionItem(key, label));
        }
        if (_items.Count == 0)
        {
            for (int index = 0; index <= 10; index++) _items.Add(new EnumOptionItem(index.ToString(CultureInfo.InvariantCulture), $"Value {index}"));
        }
        if (!_items.Any(item => string.Equals(item.Key, currentKey, StringComparison.Ordinal)))
        {
            _items.Add(new EnumOptionItem(currentKey, string.IsNullOrWhiteSpace(currentKey) ? "Current value" : $"Current value ({currentKey})"));
        }
        foreach (EnumOptionItem item in _items) _value.Items.Add(item);
        int selected = _items.FindIndex(item => string.Equals(item.Key, currentKey, StringComparison.Ordinal));
        _value.SelectedIndex = selected >= 0 ? selected : 0;
    }

    public Control View => _value;

    public JsonNode BuildValue()
    {
        string key = (_value.SelectedItem as EnumOptionItem)?.Key ?? "0";
        return int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? JsonValue.Create(number)!
            : JsonValue.Create(key)!;
    }

    public IReadOnlyList<string> Validate() => _value.SelectedIndex < 0 ? ["Select a value."] : [];

    private sealed record EnumOptionItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class DatabaseIdParameterEditor : IParameterValueEditor
{
    private readonly ComboBox _value = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly string _target;

    public DatabaseIdParameterEditor(
        IEventCommandProjectCatalog catalog,
        EventCommandParameterDescriptor descriptor,
        JsonElement current)
    {
        _target = descriptor.Target ?? "Record";
        int id = current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out int parsed) ? parsed : 0;
        _value.Items.Add(new NamedProjectValue(0, "None"));
        foreach (NamedProjectValue item in catalog.GetValues(_target))
        {
            if (item.Id != 0) _value.Items.Add(item);
        }
        NamedProjectValue? selected = _value.Items.Cast<NamedProjectValue>().FirstOrDefault(item => item.Id == id);
        if (selected is not null) _value.SelectedItem = selected;
        else _value.Text = id.ToString(CultureInfo.InvariantCulture);
        _value.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _value.AutoCompleteSource = AutoCompleteSource.ListItems;
    }

    public Control View => _value;

    public JsonNode BuildValue() => JsonValue.Create(ParseId())!;

    public IReadOnlyList<string> Validate() => ParseId() < 0 ? [$"{_target} ID cannot be negative."] : [];

    private int ParseId()
    {
        if (_value.SelectedItem is NamedProjectValue item) return item.Id;
        string text = _value.Text.Trim();
        int separator = text.IndexOf(':');
        if (separator >= 0) text = text[..separator].Trim();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0;
    }
}

internal sealed class AssetParameterEditor : IParameterValueEditor
{
    private readonly EventCommandEditContext _context;
    private readonly EventCommandParameterDescriptor _descriptor;
    private readonly TextBox _value = new() { Dock = DockStyle.Fill };
    private readonly Panel _view = new() { Dock = DockStyle.Fill, Height = 30 };

    public AssetParameterEditor(EventCommandEditContext context, EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _context = context;
        _descriptor = descriptor;
        _value.Text = current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
        Button browse = new() { Text = "Browse…", Dock = DockStyle.Right, Width = 86 };
        browse.Click += (_, _) => Browse();
        _view.Controls.Add(_value);
        _view.Controls.Add(browse);
    }

    public Control View => _view;
    public JsonNode BuildValue() => JsonValue.Create(_value.Text.Trim())!;
    public IReadOnlyList<string> Validate() => [];

    private void Browse()
    {
        AssetCategory category = _descriptor.AssetCategory ?? AssetCategory.Unknown;
        HashSet<AssetCategory> allowed = category == AssetCategory.Unknown ? [] : [category];
        AssetKey? current = string.IsNullOrWhiteSpace(_value.Text) || category == AssetCategory.Unknown
            ? null
            : new AssetKey(category, _value.Text);
        using AssetPickerDialog dialog = new(
            _context.Project,
            new AssetPickerRequest(allowed, current, AllowNone: true, Title: $"Choose {_descriptor.DisplayName}"));
        if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK) return;
        _value.Text = dialog.SelectedAsset?.LogicalName ?? string.Empty;
    }
}

internal sealed class AudioParameterEditor : IParameterValueEditor
{
    private readonly EventCommandEditContext _context;
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _volume = Number(0, 100, 90);
    private readonly NumericUpDown _pitch = Number(50, 150, 100);
    private readonly NumericUpDown _pan = Number(-100, 100, 0);
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
    private readonly JsonObject _original;

    public AudioParameterEditor(EventCommandEditContext context, EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _context = context;
        _original = current.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(current.GetRawText()) as JsonObject ?? []
            : [];
        _name.Text = ReadString(current, "name");
        _volume.Value = Math.Clamp(ReadInt(current, "volume", 90), 0, 100);
        _pitch.Value = Math.Clamp(ReadInt(current, "pitch", 100), 50, 150);
        _pan.Value = Math.Clamp(ReadInt(current, "pan", 0), -100, 100);

        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = "Audio:", AutoSize = true, Margin = new Padding(3, 7, 6, 3) }, 0, 0);
        _view.Controls.Add(_name, 1, 0);
        Button browse = new() { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        _view.Controls.Add(browse, 2, 0);
        _view.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 3, 0);
        AddRow(1, "Volume", _volume, "%");
        AddRow(2, "Pitch", _pitch, "%");
        AddRow(3, "Pan", _pan, string.Empty);
    }

    public Control View => _view;

    public JsonNode BuildValue()
    {
        JsonObject value = JsonNode.Parse(_original.ToJsonString()) as JsonObject ?? [];
        value["name"] = _name.Text.Trim();
        value["volume"] = decimal.ToInt32(_volume.Value);
        value["pitch"] = decimal.ToInt32(_pitch.Value);
        value["pan"] = decimal.ToInt32(_pan.Value);
        return value;
    }

    public IReadOnlyList<string> Validate() => [];

    private void AddRow(int row, string label, Control control, string unit)
    {
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 7, 6, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
        _view.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 7, 3, 3) }, 2, row);
    }

    private void Browse()
    {
        AssetCategory category = ResolveAudioCategory(_context.Definition.Code);
        AssetKey? current = string.IsNullOrWhiteSpace(_name.Text) ? null : new AssetKey(category, _name.Text);
        using AssetPickerDialog dialog = new(
            _context.Project,
            new AssetPickerRequest(new HashSet<AssetCategory> { category }, current, AllowNone: true, MediaKind: AssetMediaKind.Audio, Title: "Choose Audio"));
        if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK) return;
        _name.Text = dialog.SelectedAsset?.LogicalName ?? string.Empty;
    }

    private static AssetCategory ResolveAudioCategory(int commandCode) => commandCode switch
    {
        132 or 140 or 241 => AssetCategory.BackgroundMusic,
        133 or 139 or 249 => AssetCategory.MusicEffects,
        245 => AssetCategory.BackgroundSounds,
        _ => AssetCategory.SoundEffects
    };

    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new()
    {
        Dock = DockStyle.Left,
        Width = 120,
        Minimum = min,
        Maximum = max,
        Value = value
    };

    private static string ReadString(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement child) && child.ValueKind == JsonValueKind.String
            ? child.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement value, string property, int fallback) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement child) && child.TryGetInt32(out int parsed)
            ? parsed
            : fallback;
}

internal sealed class VectorParameterEditor : IParameterValueEditor
{
    private readonly NumericUpDown[] _values;
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
    private readonly bool _tone;

    public VectorParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current, bool tone)
    {
        _tone = tone;
        string[] labels = tone ? ["Red", "Green", "Blue", "Gray"] : ["Red", "Green", "Blue", "Intensity"];
        _values = new NumericUpDown[4];
        for (int index = 0; index < 4; index++)
        {
            decimal minimum = tone && index < 3 ? -255 : 0;
            NumericUpDown number = new() { Minimum = minimum, Maximum = 255, Width = 92 };
            int parsed = current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > index && current[index].TryGetInt32(out int value) ? value : 0;
            number.Value = Math.Clamp(parsed, (int)minimum, 255);
            _values[index] = number;
            _view.Controls.Add(new Label { Text = labels[index] + ":", AutoSize = true, Margin = new Padding(5, 7, 4, 3) }, index, 0);
            _view.Controls.Add(number, index, 1);
        }
    }

    public Control View => _view;

    public JsonNode BuildValue()
    {
        JsonArray array = [];
        foreach (NumericUpDown value in _values) array.Add(decimal.ToInt32(value.Value));
        return array;
    }

    public IReadOnlyList<string> Validate() => [];
}

internal sealed class StringArrayParameterEditor : IParameterValueEditor
{
    private readonly List<string> _values = [];
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, Height = 120 };
    private readonly Panel _view = new() { Dock = DockStyle.Fill, Height = 150 };

    public StringArrayParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        if (current.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in current.EnumerateArray()) _values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString());
        }
        FlowLayoutPanel tools = new() { Dock = DockStyle.Right, Width = 94, FlowDirection = FlowDirection.TopDown };
        AddButton(tools, "Add", Add);
        AddButton(tools, "Edit", Edit);
        AddButton(tools, "Delete", Delete);
        AddButton(tools, "Up", () => Move(-1));
        AddButton(tools, "Down", () => Move(1));
        _view.Controls.Add(_list);
        _view.Controls.Add(tools);
        Refresh();
    }

    public Control View => _view;

    public JsonNode BuildValue()
    {
        JsonArray array = [];
        foreach (string value in _values) array.Add(value);
        return array;
    }

    public IReadOnlyList<string> Validate() => [];

    public IReadOnlyList<string> Values => _values.ToArray();

    private void Add()
    {
        using TextPromptDialog dialog = new("Add Value", "Text:", string.Empty);
        if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK) return;
        _values.Add(dialog.Value);
        Refresh(_values.Count - 1);
    }

    private void Edit()
    {
        int index = _list.SelectedIndex;
        if (index < 0) return;
        using TextPromptDialog dialog = new("Edit Value", "Text:", _values[index]);
        if (dialog.ShowDialog(_view.FindForm()) != DialogResult.OK) return;
        _values[index] = dialog.Value;
        Refresh(index);
    }

    private void Delete()
    {
        int index = _list.SelectedIndex;
        if (index < 0) return;
        _values.RemoveAt(index);
        Refresh(Math.Min(index, _values.Count - 1));
    }

    private void Move(int delta)
    {
        int index = _list.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _values.Count) return;
        (_values[index], _values[target]) = (_values[target], _values[index]);
        Refresh(target);
    }

    private void Refresh(int selected = 0)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            for (int index = 0; index < _values.Count; index++) _list.Items.Add($"{index + 1}. {_values[index]}");
        }
        finally
        {
            _list.EndUpdate();
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        Button button = new() { Text = text, Width = 82, Height = 28 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }
}

internal sealed class JsonParameterEditor : IParameterValueEditor
{
    private readonly EventCommandParameterDescriptor _descriptor;
    private readonly TextBox _value = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Height = 86,
        Font = new Font(FontFamily.GenericMonospace, 9)
    };

    public JsonParameterEditor(EventCommandParameterDescriptor descriptor, JsonElement current)
    {
        _descriptor = descriptor;
        _value.Text = current.ValueKind == JsonValueKind.Undefined ? "null" : current.GetRawText();
    }

    public Control View => _value;

    public JsonNode? BuildValue() => JsonNode.Parse(_value.Text);

    public IReadOnlyList<string> Validate()
    {
        try
        {
            _ = JsonNode.Parse(_value.Text);
            return [];
        }
        catch (JsonException exception)
        {
            return [$"{_descriptor.DisplayName}: {exception.Message}"];
        }
    }
}

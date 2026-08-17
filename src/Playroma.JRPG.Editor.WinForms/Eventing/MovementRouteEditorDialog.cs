using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>RPG Maker-style typed movement-route editor shared by event commands and autonomous movement.</summary>
public sealed class MovementRouteEditorDialog : Form
{
    private const string ClipboardFormat = "Playroma.JRPG.MovementRouteCommands.v1";
    private readonly EventCommandRegistry _registry;
    private readonly MvProjectSession? _project;
    private readonly IEventCommandProjectCatalog? _catalog;
    private readonly CheckBox _repeat = new() { Text = "Repeat", AutoSize = true };
    private readonly CheckBox _skip = new() { Text = "Skip if unable to move", AutoSize = true };
    private readonly CheckBox _wait = new() { Text = "Wait for completion", AutoSize = true };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly List<MovementRouteCommandModel> _commands;
    private readonly string _rawObjectJson;

    public MovementRouteEditorDialog(EventCommandRegistry registry, MovementRouteModel route)
        : this(registry, route, null, null) { }

    public MovementRouteEditorDialog(
        EventCommandRegistry registry,
        MovementRouteModel route,
        MvProjectSession? project,
        IEventCommandProjectCatalog? catalog)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _project = project;
        _catalog = catalog;
        _rawObjectJson = route.RawObjectJson;
        _commands = route.Commands.Where(item => item.Code != 0).ToList();
        _repeat.Checked = route.Repeat;
        _skip.Checked = route.Skippable;
        _wait.Checked = route.Wait;

        Text = "Movement Route Studio";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 600);
        Size = new Size(1040, 740);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        options.Controls.AddRange([_repeat, _skip, _wait]);
        root.Controls.Add(options, 0, 0);
        root.SetColumnSpan(options, 2);

        GroupBox routeBox = new() { Text = "Route", Dock = DockStyle.Fill, Padding = new Padding(8) };
        TableLayoutPanel routeRoot = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        routeRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        routeRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        routeRoot.Controls.Add(_list, 0, 0);
        FlowLayoutPanel tools = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddButton(tools, "Edit…", EditCommand);
        AddButton(tools, "Cut", Cut);
        AddButton(tools, "Copy", Copy);
        AddButton(tools, "Paste", Paste);
        AddButton(tools, "Duplicate", Duplicate);
        AddButton(tools, "Delete", DeleteCommand);
        AddButton(tools, "Up", () => Move(-1));
        AddButton(tools, "Down", () => Move(1));
        routeRoot.Controls.Add(tools, 0, 1);
        routeBox.Controls.Add(routeRoot);
        root.Controls.Add(routeBox, 0, 1);

        GroupBox paletteBox = new() { Text = "Movement Commands", Dock = DockStyle.Fill, Padding = new Padding(8) };
        paletteBox.Controls.Add(BuildCommandPalette());
        root.Controls.Add(paletteBox, 1, 1);

        FlowLayoutPanel bottom = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        Button ok = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.OK };
        Button cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.Cancel };
        bottom.Controls.AddRange([cancel, ok]);
        root.Controls.Add(bottom, 0, 2);
        root.SetColumnSpan(bottom, 2);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        _list.DoubleClick += (_, _) => EditCommand();
        _list.KeyDown += OnListKeyDown;
        RefreshRows();
    }

    public MovementRouteModel Result => new(
        _repeat.Checked,
        _skip.Checked,
        _wait.Checked,
        _commands.Concat([MovementRouteCommandModel.Create(0)]).ToArray(),
        _rawObjectJson);

    private Control BuildCommandPalette()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        var groups = new[]
        {
            ("Movement", new[] { 1,2,3,4,5,6,7,8,9,10,11,12,13,14 }),
            ("Direction", new[] { 16,17,18,19,20,21,22,23,24,25,26 }),
            ("Settings", new[] { 27,28,29,30,31,32,33,34,35,36,37,38,39,40 }),
            ("Advanced", new[] { 15,41,42,43,44,45 })
        };
        foreach ((string name, int[] codes) in groups)
        {
            FlowLayoutPanel panel = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(6) };
            foreach (int code in codes)
            {
                MovementRouteCommandDefinition definition = _registry.GetMovementRouteCommand(code);
                if (definition.Code == 0) continue;
                Button button = new() { Text = definition.Name, Width = 172, Height = 32, Tag = definition, TextAlign = ContentAlignment.MiddleLeft };
                button.Click += (_, _) => AddCommand(definition);
                panel.Controls.Add(button);
            }
            tabs.TabPages.Add(new TabPage(name) { Controls = { panel } });
        }
        return tabs;
    }

    private void AddCommand(MovementRouteCommandDefinition definition)
    {
        string raw = definition.SampleParameters.ValueKind == JsonValueKind.Array ? definition.SampleParameters.GetRawText() : "[]";
        MovementRouteCommandModel command = MovementRouteCommandModel.Create(definition.Code, raw);
        if (definition.Parameters.Count > 0)
        {
            using MovementRouteCommandEditDialog dialog = new(definition, command, _project, _catalog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            command = dialog.Result;
        }
        int index = _list.SelectedIndex >= 0 ? _list.SelectedIndex + 1 : _commands.Count;
        _commands.Insert(index, command);
        RefreshRows(index);
    }

    private void EditCommand()
    {
        int index = SelectedIndex();
        if (index < 0) return;
        MovementRouteCommandDefinition definition = _registry.GetMovementRouteCommand(_commands[index].Code);
        using MovementRouteCommandEditDialog dialog = new(definition, _commands[index], _project, _catalog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _commands[index] = dialog.Result;
        RefreshRows(index);
    }

    private void Cut() { Copy(); DeleteCommand(); }
    private void Copy()
    {
        int index = SelectedIndex();
        if (index < 0) return;
        DataObject data = new();
        data.SetData(ClipboardFormat, _commands[index].RawJson);
        data.SetText(_commands[index].RawJson);
        Clipboard.SetDataObject(data, true);
    }
    private void Paste()
    {
        try
        {
            string? raw = Clipboard.GetDataObject()?.GetData(ClipboardFormat) as string;
            if (string.IsNullOrWhiteSpace(raw) && Clipboard.ContainsText()) raw = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(raw)) return;
            MovementRouteCommandModel command = MovementRouteCommandModel.Parse(raw);
            if (command.Code == 0) return;
            int index = _list.SelectedIndex >= 0 ? _list.SelectedIndex + 1 : _commands.Count;
            _commands.Insert(index, command);
            RefreshRows(index);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot Paste Movement Command", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void Duplicate()
    {
        int index = SelectedIndex(); if (index < 0) return;
        _commands.Insert(index + 1, MovementRouteCommandModel.Parse(_commands[index].RawJson));
        RefreshRows(index + 1);
    }
    private void DeleteCommand()
    {
        int index = SelectedIndex(); if (index < 0) return;
        _commands.RemoveAt(index); RefreshRows(Math.Min(index, _commands.Count - 1));
    }
    private void Move(int delta)
    {
        int index = SelectedIndex(); int target = index + delta;
        if (index < 0 || target < 0 || target >= _commands.Count) return;
        (_commands[index], _commands[target]) = (_commands[target], _commands[index]);
        RefreshRows(target);
    }
    private int SelectedIndex() => _list.SelectedIndex;
    private void RefreshRows(int selected = 0)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            for (int index = 0; index < _commands.Count; index++)
            {
                MovementRouteCommandModel command = _commands[index];
                string parameters = command.ParametersRawJson == "[]" ? string.Empty : "  " + command.ParametersRawJson;
                _list.Items.Add($"{index + 1,3}. {_registry.GetMovementRouteCommand(command.Code).Name}{parameters}");
            }
        }
        finally { _list.EndUpdate(); }
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
    }
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { EditCommand(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { DeleteCommand(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.C) { Copy(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.X) { Cut(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.V) { Paste(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.D) { Duplicate(); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Up) { Move(-1); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Down) { Move(1); e.Handled = true; }
    }
    private static void AddButton(Control parent, string text, Action action)
    {
        Button button = new() { Text = text, AutoSize = true };
        button.Click += (_, _) => action(); parent.Controls.Add(button);
    }
}

internal sealed class MovementRouteCommandEditDialog : Form
{
    private readonly MovementRouteCommandDefinition _definition;
    private readonly MovementRouteCommandModel _original;
    private readonly List<(EventCommandParameterDescriptor Descriptor, Control Control)> _controls = [];
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(12) };

    public MovementRouteCommandEditDialog(
        MovementRouteCommandDefinition definition,
        MovementRouteCommandModel command,
        MvProjectSession? project,
        IEventCommandProjectCatalog? catalog)
    {
        _definition = definition;
        _original = command;
        Result = command;
        Text = definition.Name;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 320);
        Size = new Size(680, 520);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        JsonElement values = JsonParameterReader.ParseArray(command.ParametersRawJson);
        foreach (EventCommandParameterDescriptor descriptor in definition.Parameters.OrderBy(item => item.Index))
        {
            Control control = CreateControl(descriptor, values, project, catalog);
            int row = _fields.RowCount++;
            _fields.RowStyles.Add(new RowStyle(control is TextBox { Multiline: true } ? SizeType.Percent : SizeType.AutoSize));
            Label label = new() { Text = descriptor.DisplayName + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) };
            if (!string.IsNullOrWhiteSpace(descriptor.Description)) new ToolTip().SetToolTip(label, descriptor.Description);
            _fields.Controls.Add(label, 0, row);
            _fields.Controls.Add(control, 1, row);
            _controls.Add((descriptor, control));
        }
        if (_controls.Count == 0)
        {
            _fields.Controls.Add(new Label { Text = "This movement command has no parameters.", AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 0);
            _fields.SetColumnSpan(_fields.Controls[_fields.Controls.Count - 1], 2);
        }

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button ok = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 30) };
        Button cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Confirm();
        buttons.Controls.AddRange([cancel, ok]);
        Controls.Add(_fields); Controls.Add(buttons);
        AcceptButton = ok; CancelButton = cancel;
    }

    public MovementRouteCommandModel Result { get; private set; }

    private Control CreateControl(EventCommandParameterDescriptor descriptor, JsonElement values, MvProjectSession? project, IEventCommandProjectCatalog? catalog)
    {
        JsonElement value = JsonParameterReader.TryGet(values, descriptor.Index, out JsonElement found) ? found : default;
        switch (descriptor.Kind)
        {
            case EventParameterKind.Boolean:
                return new CheckBox { AutoSize = true, Checked = value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean() };
            case EventParameterKind.Enum:
            {
                ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                int selected = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed) ? parsed : 0;
                foreach ((string key, string label) in descriptor.Options ?? new Dictionary<string, string>())
                    if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int option)) combo.Items.Add(new NumberChoice(option, label));
                int index = combo.Items.Cast<NumberChoice>().ToList().FindIndex(item => item.Value == selected);
                combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
                return combo;
            }
            case EventParameterKind.Asset:
                return new RouteAssetValueControl(project, descriptor, value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty);
            case EventParameterKind.Audio:
                return new RouteAudioValueControl(project, value);
            case EventParameterKind.Integer:
            case EventParameterKind.DatabaseId:
            {
                int selected = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed) ? parsed : 0;
                if (descriptor.Kind == EventParameterKind.DatabaseId && catalog is not null && !string.IsNullOrWhiteSpace(descriptor.Target))
                {
                    ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
                    foreach (NamedProjectValue item in catalog.GetValues(descriptor.Target)) combo.Items.Add(item);
                    NamedProjectValue? current = combo.Items.Cast<NamedProjectValue>().FirstOrDefault(item => item.Id == selected);
                    if (current is not null) combo.SelectedItem = current; else combo.Text = selected.ToString(CultureInfo.InvariantCulture);
                    return combo;
                }
                decimal min = descriptor.Minimum ?? -99999999;
                decimal max = descriptor.Maximum ?? 99999999;
                return new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(selected, min, max), Width = 180, ThousandsSeparator = true };
            }
            default:
            {
                bool multiline = descriptor.Kind is EventParameterKind.StringArray or EventParameterKind.Json or EventParameterKind.Object or EventParameterKind.Array;
                return new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = multiline,
                    Height = multiline ? 120 : 24,
                    ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
                    Font = multiline ? new Font(FontFamily.GenericMonospace, 10) : SystemFonts.MessageBoxFont,
                    Text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ValueKind == JsonValueKind.Undefined ? string.Empty : value.GetRawText()
                };
            }
        }
    }

    private void Confirm()
    {
        try
        {
            JsonArray original = JsonNode.Parse(_original.ParametersRawJson) as JsonArray ?? [];
            foreach ((EventCommandParameterDescriptor descriptor, Control control) in _controls)
            {
                while (original.Count <= descriptor.Index) original.Add(null);
                original[descriptor.Index] = ReadValue(descriptor, control);
            }
            Result = _original.WithParameters(original.ToJsonString());
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid Movement Command", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static JsonNode? ReadValue(EventCommandParameterDescriptor descriptor, Control control)
    {
        return control switch
        {
            RouteAssetValueControl asset => JsonValue.Create(asset.AssetName),
            RouteAudioValueControl audio => audio.BuildValue(),
            CheckBox check => JsonValue.Create(check.Checked),
            NumericUpDown number => JsonValue.Create(decimal.ToInt32(number.Value)),
            ComboBox combo when combo.SelectedItem is NumberChoice choice => JsonValue.Create(choice.Value),
            ComboBox combo when combo.SelectedItem is NamedProjectValue projectValue => JsonValue.Create(projectValue.Id),
            ComboBox combo when int.TryParse(combo.Text.Split(':')[0].Trim(), out int id) => JsonValue.Create(id),
            TextBox text when descriptor.Kind is EventParameterKind.String or EventParameterKind.Asset => JsonValue.Create(text.Text),
            TextBox text when string.IsNullOrWhiteSpace(text.Text) => null,
            TextBox text => JsonNode.Parse(text.Text),
            _ => null
        };
    }

    private sealed class RouteAssetValueControl : UserControl
    {
        private readonly MvProjectSession? _project;
        private readonly EventCommandParameterDescriptor _descriptor;
        private readonly TextBox _name = new() { Dock = DockStyle.Fill };

        public RouteAssetValueControl(MvProjectSession? project, EventCommandParameterDescriptor descriptor, string value)
        {
            _project = project;
            _descriptor = descriptor;
            Dock = DockStyle.Fill;
            Height = 28;
            _name.Text = value;
            Button browse = new() { Text = "Browse…", Dock = DockStyle.Right, Width = 86, Enabled = project is not null && descriptor.AssetCategory.HasValue };
            browse.Click += (_, _) => Browse();
            Controls.Add(_name);
            Controls.Add(browse);
        }

        public string AssetName => _name.Text.Trim();

        private void Browse()
        {
            if (_project is null || !_descriptor.AssetCategory.HasValue) return;
            AssetCategory category = _descriptor.AssetCategory.Value;
            AssetKey? current = string.IsNullOrWhiteSpace(_name.Text) ? null : new AssetKey(category, _name.Text);
            using AssetPickerDialog dialog = new(_project, new AssetPickerRequest(
                new HashSet<AssetCategory> { category }, current, AllowNone: true, Title: $"Choose {_descriptor.DisplayName}"));
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _name.Text = dialog.SelectedAsset?.LogicalName ?? string.Empty;
        }
    }

    private sealed class RouteAudioValueControl : UserControl
    {
        private readonly MvProjectSession? _project;
        private readonly TextBox _name = new() { Dock = DockStyle.Fill };
        private readonly NumericUpDown _volume = Number(0, 100, 90);
        private readonly NumericUpDown _pitch = Number(50, 150, 100);
        private readonly NumericUpDown _pan = Number(-100, 100, 0);
        private readonly JsonObject _original;

        public RouteAudioValueControl(MvProjectSession? project, JsonElement value)
        {
            _project = project;
            Dock = DockStyle.Fill;
            AutoSize = true;
            _original = value.ValueKind == JsonValueKind.Object ? JsonNode.Parse(value.GetRawText()) as JsonObject ?? [] : [];
            _name.Text = ReadString(value, "name");
            _volume.Value = Math.Clamp(ReadInt(value, "volume", 90), 0, 100);
            _pitch.Value = Math.Clamp(ReadInt(value, "pitch", 100), 50, 150);
            _pan.Value = Math.Clamp(ReadInt(value, "pan", 0), -100, 100);
            TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddRow(table, 0, "Sound", _name);
            Button browse = new() { Text = "Browse…", AutoSize = true, Enabled = project is not null };
            browse.Click += (_, _) => Browse();
            table.Controls.Add(browse, 2, 0);
            AddRow(table, 1, "Volume", _volume);
            AddRow(table, 2, "Pitch", _pitch);
            AddRow(table, 3, "Pan", _pan);
            Controls.Add(table);
        }

        public JsonNode BuildValue()
        {
            JsonObject value = JsonNode.Parse(_original.ToJsonString()) as JsonObject ?? [];
            value["name"] = _name.Text.Trim();
            value["volume"] = decimal.ToInt32(_volume.Value);
            value["pitch"] = decimal.ToInt32(_pitch.Value);
            value["pan"] = decimal.ToInt32(_pan.Value);
            return value;
        }

        private void Browse()
        {
            if (_project is null) return;
            AssetCategory category = AssetCategory.SoundEffects;
            AssetKey? current = string.IsNullOrWhiteSpace(_name.Text) ? null : new AssetKey(category, _name.Text);
            using AssetPickerDialog dialog = new(_project, new AssetPickerRequest(
                new HashSet<AssetCategory> { category }, current, AllowNone: true, MediaKind: AssetMediaKind.Audio, Title: "Choose Sound Effect"));
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _name.Text = dialog.SelectedAsset?.LogicalName ?? string.Empty;
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Width = 130 };
        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 8, 3) }, 0, row);
            table.Controls.Add(control, 1, row);
        }
        private static string ReadString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
        private static int ReadInt(JsonElement value, string property, int fallback) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) && item.TryGetInt32(out int parsed) ? parsed : fallback;
    }

    private sealed record NumberChoice(int Value, string Label) { public override string ToString() => Label; }
}

internal sealed class RawJsonValueDialog : Form
{
    private readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10) };
    private readonly JsonValueKind _expected;
    public RawJsonValueDialog(string title, string raw, JsonValueKind expected)
    {
        Text = title; _expected = expected; StartPosition = FormStartPosition.CenterParent; Size = new Size(680, 480);
        _text.Text = raw;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        Button ok = new() { Text = "OK", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Confirm(); buttons.Controls.AddRange([cancel, ok]);
        Controls.Add(_text); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
    }
    public string RawJson => _text.Text;
    private void Confirm()
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(_text.Text);
            if (json.RootElement.ValueKind != _expected) throw new InvalidDataException($"Expected a JSON {_expected} value.");
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Invalid JSON", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

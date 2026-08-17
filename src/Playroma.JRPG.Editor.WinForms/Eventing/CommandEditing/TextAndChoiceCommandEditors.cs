using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.Editor.WinForms.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

public sealed class ShowTextCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly AssetParameterEditor _face;
    private readonly IntegerParameterEditor _faceIndex;
    private readonly EnumParameterEditor _background;
    private readonly EnumParameterEditor _position;
    private readonly TextBox _text = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Both,
        Font = new Font(FontFamily.GenericMonospace, 10)
    };
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };

    public ShowTextCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        RawEventCommand opening = context.Node.RawCommands[0];
        JsonElement parameters = JsonParameterReader.ParseArray(opening.ParametersRawJson);
        EventCommandParameterDescriptor[] descriptors = context.Definition.Parameters.OrderBy(item => item.Index).ToArray();
        _face = new AssetParameterEditor(context, descriptors[0], Value(parameters, 0));
        _faceIndex = new IntegerParameterEditor(descriptors[1], Value(parameters, 1));
        _background = new EnumParameterEditor(descriptors[2], Value(parameters, 2));
        _position = new EnumParameterEditor(descriptors[3], Value(parameters, 3));
        _text.Text = string.Join(Environment.NewLine,
            context.Node.RawCommands.Skip(1).Where(command => command.Code == 401)
                .Select(command => JsonParameterReader.GetString(JsonParameterReader.ParseArray(command.ParametersRawJson), 0)));

        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(0, "Face", _face.View);
        AddRow(1, "Face Index", _faceIndex.View);
        AddRow(2, "Background", _background.View);
        AddRow(3, "Position", _position.View);
        GroupBox message = new() { Text = "Message", Dock = DockStyle.Fill, Padding = new Padding(8) };
        message.Controls.Add(_text);
        _view.Controls.Add(message, 0, 4);
        _view.SetColumnSpan(message, 2);
        _view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        RawEventCommand opening = _context.Node.RawCommands[0];
        JsonArray parameters = [
            _face.BuildValue(),
            _faceIndex.BuildValue(),
            _background.BuildValue(),
            _position.BuildValue()
        ];
        List<string> commands = [EventCommandFactory.Rewrite(opening, parametersRawJson: parameters.ToJsonString())];
        string[] lines = _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0) lines = [string.Empty];
        foreach (string line in lines)
        {
            commands.Add(RawEventCommand.CreateJson(401, opening.Indent, JsonSerializer.Serialize(new object?[] { line })));
        }
        return commands;
    }

    public IReadOnlyList<string> Validate() =>
        _face.Validate().Concat(_faceIndex.Validate()).Concat(_background.Validate()).Concat(_position.Validate()).ToArray();

    private void AddRow(int row, string label, Control control)
    {
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
    }

    private static JsonElement Value(JsonElement parameters, int index)
    {
        if (JsonParameterReader.TryGet(parameters, index, out JsonElement value)) return value;
        using JsonDocument document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }
}

public sealed class ScrollingTextCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly NumericUpDown _speed = new() { Minimum = 1, Maximum = 8, Value = 2, Width = 100 };
    private readonly CheckBox _noFast = new() { Text = "Disable Fast Forward", AutoSize = true };
    private readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both };
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };

    public ScrollingTextCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement parameters = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        _speed.Value = Math.Clamp(JsonParameterReader.GetInt(parameters, 0, 2), 1, 8);
        _noFast.Checked = JsonParameterReader.GetBool(parameters, 1);
        _text.Text = string.Join(Environment.NewLine,
            context.Node.RawCommands.Skip(1).Where(command => command.Code == 405)
                .Select(command => JsonParameterReader.GetString(JsonParameterReader.ParseArray(command.ParametersRawJson), 0)));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _view.Controls.Add(new Label { Text = "Speed:", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, 0);
        _view.Controls.Add(_speed, 1, 0);
        _view.Controls.Add(_noFast, 1, 1);
        GroupBox text = new() { Text = "Scrolling Text", Dock = DockStyle.Fill, Padding = new Padding(8) };
        text.Controls.Add(_text);
        _view.Controls.Add(text, 0, 2);
        _view.SetColumnSpan(text, 2);
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        RawEventCommand opening = _context.Node.RawCommands[0];
        List<string> result = [EventCommandFactory.Rewrite(opening, parametersRawJson: JsonSerializer.Serialize(new object?[] { decimal.ToInt32(_speed.Value), _noFast.Checked }))];
        foreach (string line in _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            result.Add(RawEventCommand.CreateJson(405, opening.Indent, JsonSerializer.Serialize(new object?[] { line })));
        }
        return result;
    }

    public IReadOnlyList<string> Validate() => [];
}

public sealed class ContinuationTextCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly int _openingCode;
    private readonly int _continuationCode;
    private readonly TextBox _text;
    private readonly Panel _view = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };

    public ContinuationTextCommandEditorPanel(
        EventCommandEditContext context,
        string label,
        int openingCode,
        int continuationCode,
        bool codeEditor = false)
    {
        _context = context;
        _openingCode = openingCode;
        _continuationCode = continuationCode;
        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = codeEditor,
            ScrollBars = ScrollBars.Both,
            WordWrap = !codeEditor,
            Font = codeEditor ? new Font(FontFamily.GenericMonospace, 10) : SystemFonts.MessageBoxFont
        };
        List<string> lines = [];
        foreach (RawEventCommand command in context.Node.RawCommands.Where(command => command.Code is var code && (code == openingCode || code == continuationCode)))
        {
            lines.Add(JsonParameterReader.GetString(JsonParameterReader.ParseArray(command.ParametersRawJson), 0));
        }
        _text.Text = string.Join(Environment.NewLine, lines);
        GroupBox box = new() { Text = label, Dock = DockStyle.Fill, Padding = new Padding(8) };
        box.Controls.Add(_text);
        _view.Controls.Add(box);
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        RawEventCommand opening = _context.Node.RawCommands[0];
        string[] lines = _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0) lines = [string.Empty];
        List<string> result = [EventCommandFactory.Rewrite(opening, code: _openingCode, parametersRawJson: JsonSerializer.Serialize(new object?[] { lines[0] }))];
        foreach (string line in lines.Skip(1))
        {
            result.Add(RawEventCommand.CreateJson(_continuationCode, opening.Indent, JsonSerializer.Serialize(new object?[] { line })));
        }
        return result;
    }

    public IReadOnlyList<string> Validate() => [];
}

public sealed class PluginCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly TextBox _command = new() { Dock = DockStyle.Top };
    private readonly Panel _view = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 34, ForeColor = SystemColors.GrayText };

    public PluginCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        _command.Text = JsonParameterReader.GetString(JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson), 0);
        GroupBox box = new() { Text = "MV Plugin Command", Dock = DockStyle.Top, Height = 90, Padding = new Padding(8) };
        box.Controls.Add(_command);
        Button builder = new() { Text = "Open YEP Command Builder…", AutoSize = true };
        builder.Click += async (_, _) => await OpenYanflyBuilderAsync().ConfigureAwait(true);
        FlowLayoutPanel tools = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(0, 6, 0, 0), WrapContents = false };
        tools.Controls.Add(builder);
        Label help = new()
        {
            Text = "Choose a typed YEP command or enter the complete MV plugin-command line. Unknown plugin syntax is preserved exactly.",
            Dock = DockStyle.Top,
            Height = 42,
            ForeColor = SystemColors.GrayText
        };
        _view.Controls.Add(_status);
        _view.Controls.Add(help);
        _view.Controls.Add(tools);
        _view.Controls.Add(box);
    }

    public Control View => _view;
    public IReadOnlyList<string> BuildCommands() => [EventCommandFactory.Rewrite(_context.Node.RawCommands[0], parametersRawJson: JsonSerializer.Serialize(new object?[] { _command.Text }))];
    public IReadOnlyList<string> Validate() => string.IsNullOrWhiteSpace(_command.Text) ? ["Plugin command text is empty."] : [];

    private async Task OpenYanflyBuilderAsync()
    {
        try
        {
            PluginTagProjectIntelligence intelligence = await YanflyPluginTagProjectCache
                .GetAsync(_context.Project.Workspace.Snapshot.Identity.ProjectRoot, PluginTagCatalogHost.Catalog)
                .ConfigureAwait(true);
            using YanflyCommandPickerDialog dialog = new(_context.Project, PluginTagCatalogHost.Catalog,
                PluginAuthoringSurface.PluginCommand, intelligence.Availability);
            if (dialog.ShowDialog(_view.FindForm()) == DialogResult.OK)
            {
                _command.Text = dialog.ResultText;
                _status.Text = "Typed YEP command selected. The exact command line remains editable.";
            }
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }
}

public sealed class ChoicesCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly StringArrayParameterEditor _choices;
    private readonly ComboBox _cancel = ChoiceOption();
    private readonly ComboBox _default = ChoiceOption();
    private readonly ComboBox _position = EnumCombo((0, "Left"), (1, "Middle"), (2, "Right"));
    private readonly ComboBox _background = EnumCombo((0, "Window"), (1, "Dim"), (2, "Transparent"));
    private readonly TableLayoutPanel _view = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
    private readonly JsonArray _original;

    public ChoicesCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        JsonElement p = JsonParameterReader.ParseArray(context.Node.RawCommands[0].ParametersRawJson);
        _original = JsonNode.Parse(context.Node.RawCommands[0].ParametersRawJson) as JsonArray ?? [];
        EventCommandParameterDescriptor descriptor = context.Definition.Parameters.First(item => item.Index == 0);
        _choices = new StringArrayParameterEditor(descriptor, Value(p, 0));
        RebuildChoiceSelectors(JsonParameterReader.GetInt(p, 1, -2), JsonParameterReader.GetInt(p, 2, 0));
        SelectValue(_position, JsonParameterReader.GetInt(p, 3, 2));
        SelectValue(_background, JsonParameterReader.GetInt(p, 4, 0));

        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        GroupBox choices = new() { Text = "Choices", Dock = DockStyle.Fill, Padding = new Padding(8) };
        choices.Controls.Add(_choices.View);
        _view.Controls.Add(choices, 0, 0);
        _view.SetColumnSpan(choices, 2);
        _view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddRow(1, "Cancel", _cancel);
        AddRow(2, "Default", _default);
        AddRow(3, "Position", _position);
        AddRow(4, "Background", _background);
        Label warning = new()
        {
            Text = "Removing a choice also removes that choice's event-command branch when this edit is accepted.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(3, 8, 3, 3)
        };
        _view.Controls.Add(warning, 0, 5);
        _view.SetColumnSpan(warning, 2);
        _cancel.DropDown += (_, _) => RebuildChoiceSelectors(ReadCombo(_cancel), ReadCombo(_default));
        _default.DropDown += (_, _) => RebuildChoiceSelectors(ReadCombo(_cancel), ReadCombo(_default));
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        JsonArray parameters = JsonNode.Parse(_original.ToJsonString()) as JsonArray ?? [];
        while (parameters.Count < 5) parameters.Add(null);
        parameters[0] = _choices.BuildValue();
        parameters[1] = ReadCombo(_cancel);
        parameters[2] = ReadCombo(_default);
        parameters[3] = ReadCombo(_position);
        parameters[4] = ReadCombo(_background);
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, parameters.ToJsonString());
    }

    public IReadOnlyList<string> Validate() => _choices.Values.Count == 0 ? ["At least one choice is required."] : [];

    private void RebuildChoiceSelectors(int cancelValue, int defaultValue)
    {
        int count = Math.Max(1, _choices.Values.Count);
        _cancel.BeginUpdate();
        _default.BeginUpdate();
        try
        {
            _cancel.Items.Clear();
            _cancel.Items.Add(new NumericOption(-2, "Disallow"));
            _cancel.Items.Add(new NumericOption(-1, "Branch"));
            for (int index = 0; index < count; index++) _cancel.Items.Add(new NumericOption(index, $"Choice {index + 1}"));
            _default.Items.Clear();
            _default.Items.Add(new NumericOption(-1, "None"));
            for (int index = 0; index < count; index++) _default.Items.Add(new NumericOption(index, $"Choice {index + 1}"));
            SelectValue(_cancel, cancelValue);
            SelectValue(_default, defaultValue);
        }
        finally
        {
            _cancel.EndUpdate();
            _default.EndUpdate();
        }
    }

    private void AddRow(int row, string label, Control control)
    {
        _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _view.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(3, 8, 10, 3) }, 0, row);
        _view.Controls.Add(control, 1, row);
    }

    private static ComboBox ChoiceOption() => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };

    private static ComboBox EnumCombo(params (int Value, string Label)[] values)
    {
        ComboBox combo = ChoiceOption();
        foreach ((int value, string label) in values) combo.Items.Add(new NumericOption(value, label));
        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        return combo;
    }

    private static void SelectValue(ComboBox combo, int value)
    {
        int index = combo.Items.Cast<NumericOption>().ToList().FindIndex(item => item.Value == value);
        combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
    }

    private static int ReadCombo(ComboBox combo) => (combo.SelectedItem as NumericOption)?.Value ?? 0;

    private static JsonElement Value(JsonElement parameters, int index)
    {
        if (JsonParameterReader.TryGet(parameters, index, out JsonElement value)) return value;
        using JsonDocument document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private sealed record NumericOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}

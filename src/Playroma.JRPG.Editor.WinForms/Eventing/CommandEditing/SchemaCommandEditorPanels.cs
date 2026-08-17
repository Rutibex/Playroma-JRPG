using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

public sealed class SchemaEventCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly EventCommandEditContext _context;
    private readonly TableLayoutPanel _view = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        ColumnCount = 2,
        Padding = new Padding(12)
    };
    private readonly List<(EventCommandParameterDescriptor Descriptor, IParameterValueEditor Editor)> _editors = [];
    private readonly JsonArray _parameters;

    public SchemaEventCommandEditorPanel(EventCommandEditContext context)
    {
        _context = context;
        RawEventCommand opening = context.Node.RawCommands[0];
        _parameters = JsonNode.Parse(opening.ParametersRawJson) as JsonArray ?? [];
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _view.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        if (context.Definition.Parameters.Count == 0)
        {
            _view.Controls.Add(new Label
            {
                Text = "This command has no parameters. Choose OK to accept it.",
                AutoSize = true,
                Padding = new Padding(8)
            }, 0, 0);
            _view.SetColumnSpan(_view.Controls[0], 2);
            return;
        }

        foreach (EventCommandParameterDescriptor descriptor in context.Definition.Parameters.OrderBy(item => item.Index))
        {
            JsonElement current = ReadCurrent(descriptor.Index);
            IParameterValueEditor editor = ParameterValueEditorFactory.Create(context, descriptor, current);
            _editors.Add((descriptor, editor));
            int row = _view.RowCount++;
            _view.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string labelText = descriptor.DisplayName + (string.IsNullOrWhiteSpace(descriptor.Unit) ? ":" : $" ({descriptor.Unit}):");
            Label label = new() { Text = labelText, AutoSize = true, Margin = new Padding(3, 8, 10, 3) };
            if (!string.IsNullOrWhiteSpace(descriptor.Description))
            {
                ToolTip tip = new();
                tip.SetToolTip(label, descriptor.Description);
            }
            _view.Controls.Add(label, 0, row);
            _view.Controls.Add(editor.View, 1, row);
        }
    }

    public Control View => _view;

    public IReadOnlyList<string> BuildCommands()
    {
        JsonArray parameters = JsonNode.Parse(_parameters.ToJsonString()) as JsonArray ?? [];
        foreach ((EventCommandParameterDescriptor descriptor, IParameterValueEditor editor) in _editors)
        {
            while (parameters.Count <= descriptor.Index) parameters.Add(null);
            parameters[descriptor.Index] = editor.BuildValue();
        }
        return EventStructuralCommandSynchronizer.Rewrite(_context.Node, parameters.ToJsonString());
    }

    public IReadOnlyList<string> Validate() => _editors.SelectMany(item => item.Editor.Validate()).ToArray();

    private JsonElement ReadCurrent(int index)
    {
        RawEventCommand opening = _context.Node.RawCommands[0];
        JsonElement array = JsonParameterReader.ParseArray(opening.ParametersRawJson);
        if (JsonParameterReader.TryGet(array, index, out JsonElement value)) return value;
        if (_context.Definition.SampleParameters.ValueKind == JsonValueKind.Array
            && _context.Definition.SampleParameters.GetArrayLength() > index)
        {
            return _context.Definition.SampleParameters[index].Clone();
        }
        using JsonDocument nullDocument = JsonDocument.Parse("null");
        return nullDocument.RootElement.Clone();
    }
}

public sealed class RawEventCommandEditorPanel : IEventCommandEditorPanel
{
    private readonly TextBox _raw = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 10)
    };

    public RawEventCommandEditorPanel(EventCommandEditContext context)
    {
        _raw.Text = string.Join(Environment.NewLine, context.Node.RawCommands.Select(command => command.RawJson));
    }

    public Control View => _raw;

    public IReadOnlyList<string> BuildCommands()
    {
        List<string> commands = [];
        foreach (string line in _raw.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Each command must be a JSON object.");
            commands.Add(document.RootElement.GetRawText());
        }
        return commands;
    }

    public IReadOnlyList<string> Validate()
    {
        try
        {
            return BuildCommands().Count == 0 ? ["At least one command row is required."] : [];
        }
        catch (Exception exception)
        {
            return [exception.Message];
        }
    }
}

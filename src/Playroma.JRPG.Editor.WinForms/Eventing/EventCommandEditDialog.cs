using System.Text.Json;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed class EventCommandEditDialog : Form
{
    private readonly EventCommandRegistry _registry;
    private readonly EventNode _node;
    private readonly TextBox _raw = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10) };
    private readonly DataGridView _parameters = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly TextBox _continuation = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private MovementRouteModel? _editedRoute;
    private bool _parametersAreArray = true;

    public EventCommandEditDialog(EventCommandRegistry registry, EventNode node)
    {
        _registry = registry; _node = node;
        RawCommands = node.RawCommands.Select(item => item.RawJson).ToArray();
        RawEventCommand opening = node.RawCommands[0];
        EventCommandDefinition definition = registry.Get(opening.Code);
        Text = $"{definition.Name} — Command {opening.Code}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 600); Size = new Size(850, 720);
        BuildTypedTab(definition, opening);
        BuildContinuationTab(opening);
        _raw.Text = string.Join(Environment.NewLine, RawCommands);
        TabPage rawPage = new("Raw commands") { Controls = { _raw } };
        _tabs.TabPages.Add(rawPage);
        if (!_parametersAreArray) _tabs.SelectedTab = rawPage;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        Button ok = new() { Text = "OK", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Confirm(); buttons.Controls.AddRange([cancel, ok]);
        Controls.Add(_tabs); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
    }

    public IReadOnlyList<string> RawCommands { get; private set; }

    private void BuildTypedTab(EventCommandDefinition definition, RawEventCommand opening)
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Index", ReadOnly = true, FillWeight = 12 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Parameter", ReadOnly = true, FillWeight = 26 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", ReadOnly = true, FillWeight = 18 });
        _parameters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "JSON value", FillWeight = 44 });
        JsonElement p = JsonParameterReader.ParseArray(opening.ParametersRawJson);
        _parametersAreArray = p.ValueKind == JsonValueKind.Array;
        int existingCount = _parametersAreArray ? p.GetArrayLength() : 0;
        int describedCount = definition.Parameters.Count == 0 ? 0 : definition.Parameters.Max(item => item.Index) + 1;
        int count = Math.Max(existingCount, describedCount);
        for (int index = 0; index < count; index++)
        {
            EventCommandParameterDescriptor? descriptor = definition.Parameters.FirstOrDefault(item => item.Index == index);
            string raw = JsonParameterReader.TryGet(p, index, out JsonElement value) ? value.GetRawText() : "null";
            _parameters.Rows.Add(index, descriptor?.DisplayName ?? descriptor?.Name ?? $"Parameter {index}", descriptor?.Kind.ToString() ?? "Raw", raw);
        }
        panel.Controls.Add(_parameters);
        if (opening.Code == 205)
        {
            Button route = new() { Text = "Open Movement Route Studio…", Dock = DockStyle.Top, Height = 38 };
            route.Click += (_, _) => EditMovementRoute(opening);
            panel.Controls.Add(route); route.BringToFront();
        }
        _tabs.TabPages.Add(new TabPage("Parameters") { Controls = { panel } });
    }

    private void BuildContinuationTab(RawEventCommand opening)
    {
        int continuationCode = opening.Code switch { 101 => 401, 105 => 405, 108 => 408, 355 => 655, _ => -1 };
        if (continuationCode < 0) return;
        _continuation.Text = string.Join(Environment.NewLine, _node.RawCommands.Skip(1).Where(item => item.Code == continuationCode)
            .Select(item => JsonParameterReader.GetString(JsonParameterReader.ParseArray(item.ParametersRawJson), 0)));
        _tabs.TabPages.Add(new TabPage(opening.Code is 108 ? "Comment" : opening.Code is 355 ? "Script" : "Text") { Controls = { _continuation } });
    }

    private void EditMovementRoute(RawEventCommand opening)
    {
        JsonElement p = JsonParameterReader.ParseArray(opening.ParametersRawJson);
        if (!JsonParameterReader.TryGet(p, 1, out JsonElement route) || route.ValueKind != JsonValueKind.Object)
        {
            MessageBox.Show(this, "The command does not contain a valid movement route object.", "Movement Route", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }
        using MovementRouteEditorDialog dialog = new(_registry, MovementRouteModel.Parse(route.GetRawText()));
        if (dialog.ShowDialog(this) == DialogResult.OK) _editedRoute = dialog.Result;
    }

    private void Confirm()
    {
        try
        {
            if (_tabs.SelectedTab?.Text == "Raw commands")
            {
                RawCommands = ParseRawLines(_raw.Text);
            }
            else
            {
                RawEventCommand opening = _node.RawCommands[0];
                JsonElement original = JsonParameterReader.ParseArray(opening.ParametersRawJson);
                List<string> values = [];
                for (int index = 0; index < _parameters.Rows.Count; index++)
                {
                    string raw = Convert.ToString(_parameters.Rows[index].Cells[3].Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                    using JsonDocument parsed = JsonDocument.Parse(raw);
                    values.Add(parsed.RootElement.GetRawText());
                }
                string parameters = "[" + string.Join(',', values) + "]";
                if (opening.Code == 205 && _editedRoute is not null)
                {
                    int characterId = JsonParameterReader.GetInt(original, 0);
                    RawCommands = MovementRouteSynchronizer.CreateEventCommandRows(characterId, _editedRoute, opening.Indent);
                }
                else
                {
                    int continuationCode = opening.Code switch { 101 => 401, 105 => 405, 108 => 408, 355 => 655, _ => -1 };
                    if (continuationCode >= 0)
                    {
                        List<string> result = [EventCommandFactory.Rewrite(opening, parametersRawJson: parameters)];
                        foreach (string line in _continuation.Lines)
                        {
                            result.Add(RawEventCommand.CreateJson(
                                continuationCode,
                                opening.Indent,
                                JsonSerializer.Serialize(new object?[] { line })));
                        }
                        RawCommands = result;
                    }
                    else
                    {
                        RawCommands = EventStructuralCommandSynchronizer.Rewrite(_node, parameters);
                    }
                }
            }
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Invalid event command", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static IReadOnlyList<string> ParseRawLines(string text)
    {
        List<string> result = [];
        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Each raw command line must be a JSON object.");
            result.Add(json.RootElement.GetRawText());
        }
        if (result.Count == 0) throw new InvalidDataException("At least one event command is required.");
        return result;
    }
}

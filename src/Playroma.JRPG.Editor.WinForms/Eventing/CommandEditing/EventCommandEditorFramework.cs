using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

public enum EventCommandInsertionMode
{
    BeforeSelected,
    AfterSelectedBlock,
    InsideSelectedBranch,
    AtEnd
}

public sealed record EventCommandInsertionTarget(
    int RawIndex,
    int Indent,
    EventCommandInsertionMode Mode);

public sealed record EventCommandEditContext(
    EventCommandDefinition Definition,
    EventNode Node,
    EventListAddress Address,
    MvProjectSession Project,
    IEventCommandProjectCatalog Catalog,
    EventCommandRegistry Registry,
    int SuggestedIndent,
    bool IsNewCommand);

public sealed record EventCommandEditResult(
    IReadOnlyList<string> RawCommands,
    int PreferredSelectionOffset = 0);

public interface IEventCommandEditorPanel
{
    Control View { get; }
    IReadOnlyList<string> BuildCommands();
    IReadOnlyList<string> Validate();
}

public interface IEventCommandEditorFactory
{
    string EditorKey { get; }
    IEventCommandEditorPanel Create(EventCommandEditContext context);
}

public sealed class DelegateEventCommandEditorFactory : IEventCommandEditorFactory
{
    private readonly Func<EventCommandEditContext, IEventCommandEditorPanel> _factory;

    public DelegateEventCommandEditorFactory(
        string editorKey,
        Func<EventCommandEditContext, IEventCommandEditorPanel> factory)
    {
        EditorKey = editorKey;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string EditorKey { get; }

    public IEventCommandEditorPanel Create(EventCommandEditContext context) => _factory(context);
}

public sealed class EventCommandEditorRegistry
{
    private readonly Dictionary<string, IEventCommandEditorFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public EventCommandEditorRegistry()
    {
        Register(new DelegateEventCommandEditorFactory("schema", context => new SchemaEventCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("showText", context => new ShowTextCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("choices", context => new ChoicesCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("scrollingText", context => new ScrollingTextCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("comment", context => new ContinuationTextCommandEditorPanel(context, "Comment", 108, 408)));
        Register(new DelegateEventCommandEditorFactory("conditional", context => new ConditionalBranchCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("variables", context => new ControlVariablesCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("transfer", context => new TransferPlayerCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("vehicleLocation", context => new VehicleLocationCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("eventLocation", context => new EventLocationCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("movementRoute", context => new MovementRouteCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("battle", context => new BattleProcessingCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("shop", context => new ShopProcessingCommandEditorPanel(context)));
        Register(new DelegateEventCommandEditorFactory("script", context => new ContinuationTextCommandEditorPanel(context, "Script", 355, 655, true)));
        Register(new DelegateEventCommandEditorFactory("pluginCommand", context => new PluginCommandEditorPanel(context)));

        // These editor keys use the schema engine, but remain separately registered so a dedicated
        // visual editor can replace them later without touching the workflow coordinator.
        foreach (string key in new[] { "tone", "flash", "picture", "pictureMove", "parallax" })
        {
            Register(new DelegateEventCommandEditorFactory(key, context => new SchemaEventCommandEditorPanel(context)));
        }
        Register(new DelegateEventCommandEditorFactory("raw", context => new RawEventCommandEditorPanel(context)));
    }

    public void Register(IEventCommandEditorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[factory.EditorKey] = factory;
    }

    public IEventCommandEditorPanel Create(EventCommandEditContext context)
    {
        string key = string.IsNullOrWhiteSpace(context.Definition.EditorKey) ? "schema" : context.Definition.EditorKey;
        if (_factories.TryGetValue(key, out IEventCommandEditorFactory? factory)) return factory.Create(context);
        return _factories["raw"].Create(context);
    }
}

public sealed record EventCommandValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class EventCommandResultValidator
{
    private readonly EventCommandRegistry _registry;
    private readonly EventStructureProjector _projector = new();
    private readonly EventStructureCompiler _compiler = new();

    public EventCommandResultValidator(EventCommandRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public EventCommandValidationResult Validate(IReadOnlyList<string> rawCommands, EventListAddress address)
    {
        List<string> errors = [];
        List<string> warnings = [];
        if (rawCommands.Count == 0)
        {
            errors.Add("The command editor returned no command rows.");
            return new EventCommandValidationResult(errors, warnings);
        }

        RawEventCommand[] parsed;
        try
        {
            parsed = rawCommands.Select((raw, index) => RawEventCommand.Parse(index, raw)).ToArray();
            foreach (RawEventCommand command in parsed)
            {
                using JsonDocument json = JsonDocument.Parse(command.RawJson);
                if (json.RootElement.ValueKind != JsonValueKind.Object) errors.Add($"Row {command.RawIndex + 1} is not a JSON object.");
                if (JsonParameterReader.ParseArray(command.ParametersRawJson).ValueKind != JsonValueKind.Array)
                    errors.Add($"Row {command.RawIndex + 1} does not have an array parameter value.");
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            return new EventCommandValidationResult(errors, warnings);
        }
        if (errors.Count > 0) return new EventCommandValidationResult(errors, warnings);

        int minimumIndent = parsed.Min(command => command.Indent);
        IReadOnlyList<string> normalized = EventCommandFactory.Reindent(parsed, -minimumIndent);
        List<RawEventCommand> validationRows = normalized
            .Select((raw, index) => RawEventCommand.Parse(index, raw))
            .ToList();
        validationRows.Add(RawEventCommand.Parse(validationRows.Count, RawEventCommand.CreateJson(0, 0, "[]")));
        string listJson = "[" + string.Join(',', validationRows.Select(command => command.RawJson)) + "]";
        EventListSnapshot snapshot = new(address, validationRows, listJson, string.Empty, DateTimeOffset.UtcNow);
        EventStructureDocument structure = _projector.Project(snapshot);
        EventListAnalyzer analyzer = new(_registry);
        foreach (EventStructureDiagnostic diagnostic in analyzer.Analyze(snapshot))
        {
            if (diagnostic.Code == "PJRPG10024")
            {
                warnings.Add(diagnostic.Message);
                continue;
            }
            if (diagnostic.Severity == EventDiagnosticSeverity.Error) errors.Add(diagnostic.Message);
            else if (diagnostic.Severity == EventDiagnosticSeverity.Warning) warnings.Add(diagnostic.Message);
        }
        EventNode[] meaningful = structure.Nodes
            .Where(node => node.RawCommands.Count == 0 || node.RawCommands[0].Code != 0)
            .ToArray();
        if (meaningful.Length != 1 || meaningful[0].OriginalSpan.Length != rawCommands.Count)
        {
            errors.Add("The editor result does not form exactly one complete structural event command.");
        }
        if (!_compiler.IsLosslessRoundTrip(structure, out string? reason))
        {
            errors.Add(reason ?? "The event command failed lossless structural round-trip validation.");
        }
        return new EventCommandValidationResult(errors.Distinct().ToArray(), warnings.Distinct().ToArray());
    }
}

public sealed class EventCommandEditorHostForm : Form
{
    private readonly EventCommandEditContext _context;
    private readonly IEventCommandEditorPanel _editor;
    private readonly EventCommandResultValidator _validator;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
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
    private readonly ListBox _messages = new() { Dock = DockStyle.Bottom, Height = 92, IntegralHeight = false };
    private readonly Button _ok = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.Cancel };

    public EventCommandEditorHostForm(
        EventCommandEditContext context,
        IEventCommandEditorPanel editor,
        EventCommandResultValidator validator)
    {
        _context = context;
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        Text = $"{context.Definition.Name} — Event Command";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(660, 520);
        Size = PreferredWindowSize(context.Definition.EditorKey);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        _raw.Text = string.Join(Environment.NewLine, context.Node.RawCommands.Select(command => command.RawJson));
        _tabs.TabPages.Add(new TabPage("Parameters") { Controls = { editor.View } });
        _tabs.TabPages.Add(new TabPage("Advanced Raw") { Controls = { _raw } });

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8)
        };
        buttons.Controls.AddRange([_cancel, _ok]);
        Controls.Add(_tabs);
        Controls.Add(_messages);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = _cancel;
        _ok.Click += (_, _) => Confirm();
    }

    public EventCommandEditResult? Result { get; private set; }

    private void Confirm()
    {
        try
        {
            IReadOnlyList<string> commands;
            List<string> panelErrors = [];
            if (_tabs.SelectedIndex == 1)
            {
                commands = ParseRawLines(_raw.Text);
            }
            else
            {
                panelErrors.AddRange(_editor.Validate());
                commands = panelErrors.Count == 0 ? _editor.BuildCommands() : [];
            }
            EventCommandValidationResult validation = commands.Count == 0
                ? new EventCommandValidationResult(panelErrors, [])
                : _validator.Validate(commands, _context.Address);
            ShowMessages(panelErrors.Concat(validation.Errors), validation.Warnings);
            if (panelErrors.Count > 0 || !validation.IsValid) return;
            Result = new EventCommandEditResult(commands);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            ShowMessages([exception.Message], []);
        }
    }

    private void ShowMessages(IEnumerable<string> errors, IEnumerable<string> warnings)
    {
        _messages.BeginUpdate();
        try
        {
            _messages.Items.Clear();
            foreach (string error in errors.Where(value => !string.IsNullOrWhiteSpace(value))) _messages.Items.Add("Error: " + error);
            foreach (string warning in warnings.Where(value => !string.IsNullOrWhiteSpace(value))) _messages.Items.Add("Warning: " + warning);
            _messages.Visible = _messages.Items.Count > 0;
        }
        finally
        {
            _messages.EndUpdate();
        }
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

    private static Size PreferredWindowSize(string editorKey) => editorKey switch
    {
        "showText" or "script" or "comment" or "scrollingText" => new Size(880, 680),
        "choices" or "conditional" or "variables" or "shop" => new Size(900, 720),
        "movementRoute" => new Size(980, 760),
        _ => new Size(760, 620)
    };
}

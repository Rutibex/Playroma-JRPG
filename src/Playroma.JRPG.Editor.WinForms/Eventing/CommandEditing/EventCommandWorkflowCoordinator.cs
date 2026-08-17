using Playroma.JRPG.Application;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

/// <summary>
/// Coordinates command selection and parameter editing without mutating an EventEditSession.
/// The caller applies the returned structural rows as one local undoable operation.
/// </summary>
public sealed class EventCommandWorkflowCoordinator
{
    private readonly EventCommandRegistry _registry;
    private readonly MvProjectSession _project;
    private readonly IEventCommandProjectCatalog _catalog;
    private readonly EventCommandUsageService _usage;
    private readonly EventCommandEditorRegistry _editors = new();
    private readonly EventCommandResultValidator _validator;
    private readonly EventStructureProjector _projector = new();

    public EventCommandWorkflowCoordinator(
        EventCommandRegistry registry,
        MvProjectSession project,
        IEventCommandProjectCatalog catalog,
        EventCommandUsageService usage)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _validator = new EventCommandResultValidator(registry);
    }

    public ValueTask<EventCommandEditResult?> CreateAsync(
        IWin32Window owner,
        EventCommandInsertionTarget target,
        EventListAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using EventCommandPaletteForm palette = new(_registry, _usage);
        if (palette.ShowDialog(owner) != DialogResult.OK || palette.SelectedDefinition is null)
            return ValueTask.FromResult<EventCommandEditResult?>(null);

        EventCommandDefinition definition = palette.SelectedDefinition;
        // New commands are projected at structural root indentation. EventEditSession.InsertCommands
        // reindents the accepted span to the explicit insertion target as one operation.
        IReadOnlyList<string> defaults = EventCommandFactory.CreateDefault(definition, 0);
        EventNode node = ProjectSingleCommand(defaults, address);

        EventCommandEditContext context = new(
            definition,
            node,
            address,
            _project,
            _catalog,
            _registry,
            target.Indent,
            IsNewCommand: true);

        EventCommandEditResult? result = EditCore(owner, context, allowImmediateParameterless: true);
        if (result is not null)
        {
            _usage.RecordUse(definition.Code);
            _usage.Save();
        }
        return ValueTask.FromResult(result);
    }

    public ValueTask<EventCommandEditResult?> EditAsync(
        IWin32Window owner,
        EventNode node,
        EventListAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        int code = node.RawCommands.Count == 0 ? -1 : node.RawCommands[0].Code;
        EventCommandDefinition definition = _registry.Get(code);
        EventCommandEditContext context = new(
            definition,
            node,
            address,
            _project,
            _catalog,
            _registry,
            node.OriginalIndent,
            IsNewCommand: false);
        EventCommandEditResult? result = EditCore(owner, context, allowImmediateParameterless: false);
        if (result is not null && definition.IsInsertable)
        {
            _usage.RecordUse(definition.Code);
            _usage.Save();
        }
        return ValueTask.FromResult(result);
    }

    private EventCommandEditResult? EditCore(
        IWin32Window owner,
        EventCommandEditContext context,
        bool allowImmediateParameterless)
    {
        bool parameterlessSchema = context.Definition.EditorKey.Equals("schema", StringComparison.OrdinalIgnoreCase)
            && context.Definition.Parameters.Count == 0;
        if (allowImmediateParameterless && parameterlessSchema)
        {
            IReadOnlyList<string> rows = context.Node.RawCommands.Select(command => command.RawJson).ToArray();
            EventCommandValidationResult validation = _validator.Validate(rows, context.Address);
            if (validation.IsValid) return new EventCommandEditResult(rows);
        }

        IEventCommandEditorPanel panel = _editors.Create(context);
        using EventCommandEditorHostForm dialog = new(context, panel, _validator);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    private EventNode ProjectSingleCommand(IReadOnlyList<string> rows, EventListAddress address)
    {
        List<RawEventCommand> parsed = rows
            .Select((raw, index) => RawEventCommand.Parse(index, raw))
            .ToList();
        int terminatorIndent = parsed.Count == 0 ? 0 : parsed[0].Indent;
        parsed.Add(RawEventCommand.Parse(parsed.Count, RawEventCommand.CreateJson(0, terminatorIndent, "[]")));
        string listJson = "[" + string.Join(',', parsed.Select(command => command.RawJson)) + "]";
        EventListSnapshot snapshot = new(
            address,
            parsed,
            listJson,
            EventStructureProjector.Fingerprint(parsed),
            DateTimeOffset.UtcNow);
        EventStructureDocument structure = _projector.Project(snapshot);
        EventNode? node = structure.Nodes.FirstOrDefault(candidate => candidate.RawCommands.Count > 0 && candidate.RawCommands[0].Code != 0);
        return node ?? throw new InvalidDataException("The default command could not be projected into one structural event node.");
    }
}

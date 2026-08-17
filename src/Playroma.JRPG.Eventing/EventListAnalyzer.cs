using System.Text.Json;

namespace Playroma.JRPG.Eventing;

public sealed class EventListAnalyzer
{
    private readonly EventCommandRegistry _registry;
    private readonly EventStructureProjector _projector = new();

    public EventListAnalyzer(EventCommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<EventStructureDiagnostic> Analyze(EventListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EventStructureDocument structure = _projector.Project(snapshot);
        List<EventStructureDiagnostic> diagnostics = [.. structure.Diagnostics];
        ValidateRows(snapshot, diagnostics);
        ValidateTerminator(snapshot, diagnostics);
        ValidateLabels(snapshot, diagnostics);
        ValidateMovementRoutes(structure, diagnostics);
        ValidateApparentLoops(structure.Nodes, diagnostics);
        return diagnostics
            .OrderBy(item => item.RawIndex ?? int.MaxValue)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private void ValidateRows(
        EventListSnapshot snapshot,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        int previousIndent = 0;
        for (int index = 0; index < snapshot.Commands.Count; index++)
        {
            RawEventCommand command = snapshot.Commands[index];
            EventCommandDefinition definition = _registry.Get(command.Code);
            if (definition.Category == "Unknown")
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10010",
                    EventDiagnosticSeverity.Information,
                    $"Unknown command code {command.Code} is preserved in raw form.",
                    command.RawIndex));
            }
            if (command.Indent < 0 || command.Indent > 100 || (index == 0 && command.Indent != 0) || (index > 0 && command.Indent > previousIndent + 1))
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10011",
                    EventDiagnosticSeverity.Error,
                    $"Invalid indentation {command.Indent}; previous indentation was {previousIndent}.",
                    command.RawIndex));
            }
            previousIndent = Math.Max(0, command.Indent);

            JsonElement p = JsonParameterReader.ParseArray(command.ParametersRawJson);
            if (p.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10012",
                    EventDiagnosticSeverity.Error,
                    "Command parameters are not an array.",
                    command.RawIndex));
                continue;
            }
            int required = definition.Parameters
                .Where(item => !item.Optional)
                .Select(item => item.Index + 1)
                .DefaultIfEmpty(0)
                .Max();
            if (p.GetArrayLength() < required)
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10013",
                    EventDiagnosticSeverity.Warning,
                    $"{definition.Name} has {p.GetArrayLength()} parameter(s); the registered schema expects at least {required}.",
                    command.RawIndex));
            }
            foreach (EventCommandParameterDescriptor descriptor in definition.Parameters)
            {
                if (!JsonParameterReader.TryGet(p, descriptor.Index, out JsonElement value)) continue;
                if (!Matches(descriptor.Kind, value))
                {
                    diagnostics.Add(new EventStructureDiagnostic(
                        "PJRPG10014",
                        EventDiagnosticSeverity.Warning,
                        $"Parameter '{descriptor.DisplayName}' has JSON type {value.ValueKind}; expected {descriptor.Kind}.",
                        command.RawIndex));
                }
            }
            ValidateCommandSpecificShape(command, p, diagnostics);
        }
    }

    private static void ValidateCommandSpecificShape(
        RawEventCommand command,
        JsonElement parameters,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        if (command.Code != 111 || parameters.ValueKind != JsonValueKind.Array || parameters.GetArrayLength() == 0) return;
        int type = JsonParameterReader.GetInt(parameters, 0, -1);
        int requiredLength = type switch
        {
            0 or 2 or 3 or 6 or 7 => 3,
            1 => 5,
            4 or 5 => 4,
            8 or 11 or 12 => 2,
            9 or 10 => 3,
            _ => 1
        };
        if (parameters.GetArrayLength() < requiredLength)
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10013",
                EventDiagnosticSeverity.Warning,
                $"Conditional Branch type {type} has {parameters.GetArrayLength()} parameter(s); expected at least {requiredLength}.",
                command.RawIndex));
        }
    }

    private static void ValidateTerminator(
        EventListSnapshot snapshot,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        if (snapshot.Commands.Count == 0)
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10015",
                EventDiagnosticSeverity.Error,
                "Event command list is empty and has no End of List command."));
            return;
        }
        for (int index = 0; index < snapshot.Commands.Count - 1; index++)
        {
            if (snapshot.Commands[index].Code == 0)
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10016",
                    EventDiagnosticSeverity.Warning,
                    "Commands appear after an End of List command.",
                    snapshot.Commands[index].RawIndex));
            }
        }
        RawEventCommand last = snapshot.Commands[^1];
        if (last.Code != 0 || last.Indent != 0)
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10017",
                EventDiagnosticSeverity.Error,
                "Event command list does not end with a root-level End of List command.",
                last.RawIndex));
        }
    }

    private static void ValidateLabels(
        EventListSnapshot snapshot,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        Dictionary<string, List<int>> labels = new(StringComparer.Ordinal);
        List<(string Label, int Index)> jumps = [];
        foreach (RawEventCommand command in snapshot.Commands)
        {
            if (command.Code is not 118 and not 119) continue;
            JsonElement p = JsonParameterReader.ParseArray(command.ParametersRawJson);
            string label = JsonParameterReader.GetString(p, 0);
            if (command.Code == 118)
            {
                if (!labels.TryGetValue(label, out List<int>? entries))
                {
                    entries = [];
                    labels[label] = entries;
                }
                entries.Add(command.RawIndex);
            }
            else
            {
                jumps.Add((label, command.RawIndex));
            }
        }
        foreach ((string label, List<int> indexes) in labels)
        {
            if (indexes.Count <= 1) continue;
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10018",
                EventDiagnosticSeverity.Warning,
                $"Label '{label}' is declared {indexes.Count} times.",
                indexes[1]));
        }
        foreach ((string label, int index) in jumps.Where(item => !labels.ContainsKey(item.Label)))
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10019",
                EventDiagnosticSeverity.Warning,
                $"Jump to Label references missing label '{label}'.",
                index));
        }
    }

    private static void ValidateMovementRoutes(
        EventStructureDocument structure,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        foreach (EventContinuationGroupNode group in EnumerateNodes(structure.Nodes).OfType<EventContinuationGroupNode>().Where(item => item.Opening.Code == 205))
        {
            MovementRouteSynchronizationResult result = MovementRouteSynchronizer.Analyze(group.Opening, group.Continuations);
            foreach (EventStructureDiagnostic diagnostic in result.Diagnostics) diagnostics.Add(diagnostic);
        }
    }

    private static void ValidateApparentLoops(
        IReadOnlyList<EventNode> nodes,
        ICollection<EventStructureDiagnostic> diagnostics)
    {
        foreach (EventBlockNode loop in EnumerateNodes(nodes).OfType<EventBlockNode>().Where(item => item.Kind == EventBlockKind.Loop))
        {
            bool hasBreak = loop.RawCommands.Any(command => command.Code == 113);
            if (!hasBreak)
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10024",
                    EventDiagnosticSeverity.Warning,
                    "Loop has no apparent Break Loop command.",
                    loop.Opening.RawIndex,
                    loop.OriginalSpan));
            }
        }
    }

    private static IEnumerable<EventNode> EnumerateNodes(IEnumerable<EventNode> nodes)
    {
        foreach (EventNode node in nodes)
        {
            yield return node;
            if (node is not EventBlockNode block) continue;
            foreach (EventBranchNode branch in block.Branches)
            {
                foreach (EventNode child in EnumerateNodes(branch.Children)) yield return child;
            }
        }
    }

    private static bool Matches(EventParameterKind kind, JsonElement value) => kind switch
    {
        EventParameterKind.Integer or EventParameterKind.DatabaseId => value.ValueKind == JsonValueKind.Number,
        EventParameterKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        EventParameterKind.String or EventParameterKind.Asset => value.ValueKind == JsonValueKind.String,
        EventParameterKind.StringArray or EventParameterKind.Array or EventParameterKind.Color or EventParameterKind.Tone => value.ValueKind == JsonValueKind.Array,
        EventParameterKind.Object or EventParameterKind.Audio or EventParameterKind.MovementRoute => value.ValueKind == JsonValueKind.Object,
        EventParameterKind.Enum => value.ValueKind is JsonValueKind.Number or JsonValueKind.String,
        EventParameterKind.Json => true,
        _ => true
    };
}

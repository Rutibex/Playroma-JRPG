namespace Playroma.JRPG.Eventing;

public static class EventEditingPlanner
{
    public static ReplaceEventCommandSpanCommand Insert(
        EventListAddress address,
        int rawIndex,
        int targetIndent,
        IReadOnlyList<string> commands,
        string description = "Insert event commands")
    {
        RawEventCommand[] parsed = commands.Select((raw, index) => RawEventCommand.Parse(index, raw)).ToArray();
        int sourceIndent = parsed.Length == 0 ? targetIndent : parsed.Min(row => row.Indent);
        IReadOnlyList<string> reindented = EventCommandFactory.Reindent(parsed, targetIndent - sourceIndent);
        return new ReplaceEventCommandSpanCommand(address, new EventCommandSpan(rawIndex, 0), reindented, description);
    }

    public static ReplaceEventCommandSpanCommand Delete(
        EventListAddress address,
        EventCommandSpan span,
        string description = "Delete event commands") =>
        new(address, span, [], description);

    public static ReplaceEventCommandSpanCommand Replace(
        EventListAddress address,
        EventCommandSpan span,
        IReadOnlyList<string> commands,
        string description = "Edit event commands") =>
        new(address, span, commands, description);

    public static ReplaceEventCommandSpanCommand Duplicate(
        EventListAddress address,
        EventNode node,
        int insertIndex) =>
        Insert(address, insertIndex, node.OriginalIndent, node.RawCommands.Select(row => row.RawJson).ToArray(), "Duplicate event command block");

    public static IReadOnlyList<string> WrapInConditional(
        IReadOnlyList<RawEventCommand> commands,
        int indent,
        string conditionalParameters = "[0,1,0]")
    {
        List<string> result = [RawEventCommand.CreateJson(111, indent, conditionalParameters)];
        result.AddRange(EventCommandFactory.Reindent(commands, indent + 1 - (commands.Count == 0 ? indent : commands.Min(row => row.Indent))));
        result.Add(RawEventCommand.CreateJson(412, indent, "[]"));
        return result;
    }

    public static IReadOnlyList<string> EnsureTerminator(IEnumerable<string> rawCommands)
    {
        List<string> result = rawCommands.ToList();
        RawEventCommand? last = result.Count == 0 ? null : RawEventCommand.Parse(result.Count - 1, result[^1]);
        if (last?.Code != 0) result.Add(RawEventCommand.CreateJson(0, 0, "[]"));
        return result;
    }
}

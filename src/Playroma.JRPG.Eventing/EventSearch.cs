namespace Playroma.JRPG.Eventing;

public sealed record EventSearchEntry(
    EventListAddress Address,
    int RawIndex,
    int Code,
    string CommandName,
    string Summary,
    string SearchText);

public sealed class EventSearchIndexer
{
    private readonly EventCommandRegistry _registry;
    private readonly EventCommandFormatter _formatter;

    public EventSearchIndexer(EventCommandRegistry registry, EventCommandFormatter formatter)
    {
        _registry = registry;
        _formatter = formatter;
    }

    public IReadOnlyList<EventSearchEntry> Build(EventListSnapshot snapshot)
    {
        return snapshot.Commands.Select(command =>
        {
            EventCommandDefinition definition = _registry.Get(command.Code);
            EventCommandSummary summary = _formatter.Format(command);
            string search = string.Join(' ', new[]
            {
                command.Code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                definition.Name, definition.Category, summary.PrimaryText,
                summary.SecondaryText ?? string.Empty, command.ParametersRawJson
            });
            return new EventSearchEntry(snapshot.Address, command.RawIndex, command.Code, definition.Name, summary.PrimaryText, search);
        }).ToArray();
    }

    public IReadOnlyList<EventSearchEntry> Search(EventListSnapshot snapshot, string query)
    {
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return Build(snapshot);
        return Build(snapshot).Where(entry => terms.All(term => entry.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}

namespace Playroma.JRPG.Mapping;

public sealed record EventPageConditionSnapshot(
    bool Switch1Valid,
    int Switch1Id,
    bool Switch2Valid,
    int Switch2Id,
    bool VariableValid,
    int VariableId,
    int VariableValue,
    bool SelfSwitchValid,
    string SelfSwitchKey,
    bool ItemValid,
    int ItemId,
    bool ActorValid,
    int ActorId);

public sealed record EventPreviewContext(
    IReadOnlyDictionary<int, bool> Switches,
    IReadOnlyDictionary<int, int> Variables,
    IReadOnlyDictionary<(int MapId, int EventId, string Key), bool> SelfSwitches,
    IReadOnlySet<int> PartyActors,
    IReadOnlyDictionary<int, int> Inventory)
{
    public static EventPreviewContext Empty { get; } = new(
        new Dictionary<int, bool>(),
        new Dictionary<int, int>(),
        new Dictionary<(int, int, string), bool>(),
        new HashSet<int>(),
        new Dictionary<int, int>());
}

public static class EventPageConditionEvaluator
{
    public static bool IsActive(
        EventPageConditionSnapshot condition,
        EventPreviewContext context,
        int mapId,
        int eventId)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(context);
        if (condition.Switch1Valid && (!context.Switches.TryGetValue(condition.Switch1Id, out bool switch1) || !switch1)) return false;
        if (condition.Switch2Valid && (!context.Switches.TryGetValue(condition.Switch2Id, out bool switch2) || !switch2)) return false;
        if (condition.VariableValid && (!context.Variables.TryGetValue(condition.VariableId, out int variable) || variable < condition.VariableValue)) return false;
        if (condition.SelfSwitchValid
            && (!context.SelfSwitches.TryGetValue((mapId, eventId, condition.SelfSwitchKey), out bool selfSwitch) || !selfSwitch)) return false;
        if (condition.ItemValid && (!context.Inventory.TryGetValue(condition.ItemId, out int itemCount) || itemCount <= 0)) return false;
        if (condition.ActorValid && !context.PartyActors.Contains(condition.ActorId)) return false;
        return true;
    }
}

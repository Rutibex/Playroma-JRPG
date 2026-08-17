using System.Text.Json;

namespace Playroma.JRPG.Eventing;

/// <summary>
/// Rewrites the opening command of compound MV structures while keeping the branch bodies
/// that still have a corresponding branch in the edited command. This prevents the opening
/// parameter array from drifting out of sync with 402/403/404 and 601/602/603/604 rows.
/// </summary>
public static class EventStructuralCommandSynchronizer
{
    public static IReadOnlyList<string> Rewrite(EventNode node, string parametersRawJson)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersRawJson);
        using JsonDocument parsed = JsonDocument.Parse(parametersRawJson);
        if (parsed.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Event command parameters must be a JSON array.");
        }

        if (node is EventBlockNode { Kind: EventBlockKind.Choices } choices)
        {
            return RewriteChoices(choices, parsed.RootElement.GetRawText());
        }
        if (node is EventBlockNode { Kind: EventBlockKind.Battle } battle)
        {
            return RewriteBattle(battle, parsed.RootElement.GetRawText());
        }

        RawEventCommand opening = node.RawCommands[0];
        return [EventCommandFactory.Rewrite(opening, parametersRawJson: parsed.RootElement.GetRawText()), .. node.RawCommands.Skip(1).Select(item => item.RawJson)];
    }

    private static IReadOnlyList<string> RewriteChoices(EventBlockNode block, string parametersRawJson)
    {
        using JsonDocument parsed = JsonDocument.Parse(parametersRawJson);
        JsonElement parameters = parsed.RootElement;
        JsonElement choices = JsonParameterReader.TryGet(parameters, 0, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value
            : default;
        int cancelType = JsonParameterReader.GetInt(parameters, 1, -2);

        Dictionary<int, EventBranchNode> existingChoices = [];
        EventBranchNode? existingCancel = null;
        foreach (EventBranchNode branch in block.Branches)
        {
            if (branch.Marker?.Code == 402)
            {
                JsonElement markerParameters = JsonParameterReader.ParseArray(branch.Marker.ParametersRawJson);
                existingChoices[JsonParameterReader.GetInt(markerParameters, 0)] = branch;
            }
            else if (branch.Marker?.Code == 403)
            {
                existingCancel = branch;
            }
        }

        List<string> result = [EventCommandFactory.Rewrite(block.Opening, parametersRawJson: parametersRawJson)];
        int choiceCount = choices.ValueKind == JsonValueKind.Array ? choices.GetArrayLength() : 0;
        for (int index = 0; index < choiceCount; index++)
        {
            string text = choices[index].ValueKind == JsonValueKind.String
                ? choices[index].GetString() ?? string.Empty
                : choices[index].ToString();
            EventBranchNode? branch = existingChoices.GetValueOrDefault(index);
            string markerParameters = JsonSerializer.Serialize(new object?[] { index, text });
            result.Add(branch?.Marker is { } marker
                ? EventCommandFactory.Rewrite(marker, parametersRawJson: markerParameters)
                : RawEventCommand.CreateJson(402, block.Opening.Indent, markerParameters));
            if (branch is not null) AddChildren(result, branch.Children);
        }

        // MV uses -1 to represent a dedicated cancel branch and -2 to disallow cancel.
        if (cancelType == -1)
        {
            result.Add(existingCancel?.Marker is { } marker
                ? marker.RawJson
                : RawEventCommand.CreateJson(403, block.Opening.Indent, "[]"));
            if (existingCancel is not null) AddChildren(result, existingCancel.Children);
        }

        result.Add(block.Closing?.RawJson ?? RawEventCommand.CreateJson(404, block.Opening.Indent, "[]"));
        return result;
    }

    private static IReadOnlyList<string> RewriteBattle(EventBlockNode block, string parametersRawJson)
    {
        using JsonDocument parsed = JsonDocument.Parse(parametersRawJson);
        JsonElement parameters = parsed.RootElement;
        bool canEscape = JsonParameterReader.GetBool(parameters, 2);
        bool canLose = JsonParameterReader.GetBool(parameters, 3);
        List<string> result = [EventCommandFactory.Rewrite(block.Opening, parametersRawJson: parametersRawJson)];
        if (!canEscape && !canLose) return result;

        Dictionary<int, EventBranchNode> branches = [];
        foreach (EventBranchNode branch in block.Branches)
        {
            if (branch.Marker is not null && !branches.ContainsKey(branch.Marker.Code))
            {
                branches[branch.Marker.Code] = branch;
            }
        }
        AddBattleBranch(result, block, branches.GetValueOrDefault(601), 601);
        if (canEscape) AddBattleBranch(result, block, branches.GetValueOrDefault(602), 602);
        if (canLose) AddBattleBranch(result, block, branches.GetValueOrDefault(603), 603);
        result.Add(block.Closing?.RawJson ?? RawEventCommand.CreateJson(604, block.Opening.Indent, "[]"));
        return result;
    }

    private static void AddBattleBranch(
        ICollection<string> result,
        EventBlockNode block,
        EventBranchNode? branch,
        int code)
    {
        result.Add(branch?.Marker?.RawJson ?? RawEventCommand.CreateJson(code, block.Opening.Indent, "[]"));
        if (branch is not null) AddChildren(result, branch.Children);
    }

    private static void AddChildren(ICollection<string> result, IEnumerable<EventNode> children)
    {
        foreach (EventNode child in children)
        {
            foreach (RawEventCommand command in child.RawCommands) result.Add(command.RawJson);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playroma.JRPG.Eventing;

public static class EventCommandFactory
{
    public static IReadOnlyList<string> CreateDefault(
        EventCommandDefinition definition,
        int indent)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (indent < 0) throw new ArgumentOutOfRangeException(nameof(indent));
        string parameters = definition.SampleParameters.ValueKind == JsonValueKind.Array
            ? definition.SampleParameters.GetRawText()
            : "[]";
        string opening = RawEventCommand.CreateJson(definition.Code, indent, parameters);
        return definition.Code switch
        {
            101 => [opening, RawEventCommand.CreateJson(401, indent, "[\"\"]")],
            102 => CreateDefaultChoices(opening, definition.SampleParameters, indent),
            105 => [opening, RawEventCommand.CreateJson(405, indent, "[\"\"]")],
            108 => [opening],
            111 => [opening, RawEventCommand.CreateJson(412, indent, "[]")],
            112 => [opening, RawEventCommand.CreateJson(413, indent, "[]")],
            205 => CreateDefaultMovementRoute(indent),
            301 => CreateDefaultBattle(opening, definition.SampleParameters, indent),
            302 => [opening],
            355 => [opening],
            _ => [opening]
        };
    }

    public static string Rewrite(
        RawEventCommand original,
        int? code = null,
        int? indent = null,
        string? parametersRawJson = null)
    {
        JsonObject root = JsonNode.Parse(original.RawJson) as JsonObject ?? [];
        root["code"] = code ?? original.Code;
        root["indent"] = indent ?? original.Indent;
        root["parameters"] = JsonNode.Parse(parametersRawJson ?? original.ParametersRawJson);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public static IReadOnlyList<string> Reindent(
        IEnumerable<RawEventCommand> commands,
        int delta)
    {
        ArgumentNullException.ThrowIfNull(commands);
        return commands.Select(command => Rewrite(command, indent: Math.Max(0, command.Indent + delta))).ToArray();
    }

    private static IReadOnlyList<string> CreateDefaultChoices(
        string opening,
        JsonElement parameters,
        int indent)
    {
        List<string> result = [opening];
        if (parameters.ValueKind == JsonValueKind.Array
            && parameters.GetArrayLength() > 0
            && parameters[0].ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement choice in parameters[0].EnumerateArray())
            {
                string text = choice.ValueKind == JsonValueKind.String ? choice.GetString() ?? string.Empty : choice.GetRawText();
                result.Add(RawEventCommand.CreateJson(402, indent, JsonSerializer.Serialize(new object?[] { index, text })));
                index++;
            }
        }
        int cancelType = JsonParameterReader.GetInt(parameters, 1, -2);
        if (cancelType == -1) result.Add(RawEventCommand.CreateJson(403, indent, "[]"));
        result.Add(RawEventCommand.CreateJson(404, indent, "[]"));
        return result;
    }

    private static IReadOnlyList<string> CreateDefaultMovementRoute(int indent)
    {
        MovementRouteModel route = MovementRouteModel.CreateDefault();
        return MovementRouteSynchronizer.CreateEventCommandRows(0, route, indent);
    }

    private static IReadOnlyList<string> CreateDefaultBattle(
        string opening,
        JsonElement parameters,
        int indent)
    {
        bool canEscape = JsonParameterReader.GetBool(parameters, 2);
        bool canLose = JsonParameterReader.GetBool(parameters, 3);
        if (!canEscape && !canLose) return [opening];
        List<string> result = [opening, RawEventCommand.CreateJson(601, indent, "[]")];
        if (canEscape) result.Add(RawEventCommand.CreateJson(602, indent, "[]"));
        if (canLose) result.Add(RawEventCommand.CreateJson(603, indent, "[]"));
        result.Add(RawEventCommand.CreateJson(604, indent, "[]"));
        return result;
    }
}

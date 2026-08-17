using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playroma.JRPG.Eventing;

public sealed record MovementRouteCommandModel(
    int Code,
    string ParametersRawJson,
    string RawJson)
{
    public static MovementRouteCommandModel Parse(string rawJson)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement root = document.RootElement;
        int code = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("code", out JsonElement codeElement)
            && codeElement.TryGetInt32(out int parsed)
            ? parsed
            : 0;
        string parameters = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("parameters", out JsonElement parameterElement)
            ? parameterElement.GetRawText()
            : "[]";
        return new MovementRouteCommandModel(code, parameters, rawJson);
    }

    public static MovementRouteCommandModel Create(int code, string parametersRawJson = "[]")
    {
        using JsonDocument parameters = JsonDocument.Parse(parametersRawJson);
        JsonObject root = new() { ["code"] = code };
        if (parameters.RootElement.ValueKind == JsonValueKind.Array && parameters.RootElement.GetArrayLength() > 0)
        {
            root["parameters"] = JsonNode.Parse(parametersRawJson);
        }
        string raw = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return new MovementRouteCommandModel(code, parametersRawJson, raw);
    }

    public MovementRouteCommandModel WithParameters(string parametersRawJson)
    {
        JsonObject root = JsonNode.Parse(RawJson) as JsonObject ?? [];
        root["code"] = Code;
        using JsonDocument parameters = JsonDocument.Parse(parametersRawJson);
        if (parameters.RootElement.ValueKind == JsonValueKind.Array && parameters.RootElement.GetArrayLength() == 0)
        {
            root.Remove("parameters");
        }
        else
        {
            root["parameters"] = JsonNode.Parse(parametersRawJson);
        }
        string raw = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return this with { ParametersRawJson = parametersRawJson, RawJson = raw };
    }
}

public sealed record MovementRouteModel(
    bool Repeat,
    bool Skippable,
    bool Wait,
    IReadOnlyList<MovementRouteCommandModel> Commands,
    string RawObjectJson)
{
    public static MovementRouteModel CreateDefault() => new(
        false,
        true,
        true,
        [MovementRouteCommandModel.Create(0)],
        "{\"repeat\":false,\"skippable\":true,\"wait\":true,\"list\":[{\"code\":0}]}"
    );

    public static MovementRouteModel Parse(string rawObjectJson)
    {
        JsonObject root = JsonNode.Parse(rawObjectJson) as JsonObject
            ?? throw new InvalidDataException("Movement route is not a JSON object.");
        bool repeat = root["repeat"]?.GetValue<bool>() ?? false;
        bool skippable = root["skippable"]?.GetValue<bool>() ?? false;
        bool wait = root["wait"]?.GetValue<bool>() ?? false;
        List<MovementRouteCommandModel> commands = [];
        if (root["list"] is JsonArray list)
        {
            foreach (JsonNode? node in list)
            {
                if (node is null) continue;
                commands.Add(MovementRouteCommandModel.Parse(node.ToJsonString()));
            }
        }
        EnsureTerminator(commands);
        return new MovementRouteModel(repeat, skippable, wait, commands, rawObjectJson);
    }

    public string Compile()
    {
        JsonObject root = JsonNode.Parse(RawObjectJson) as JsonObject ?? [];
        root["repeat"] = Repeat;
        root["skippable"] = Skippable;
        root["wait"] = Wait;
        List<MovementRouteCommandModel> commands = Commands.ToList();
        EnsureTerminator(commands);
        JsonArray list = [];
        foreach (MovementRouteCommandModel command in commands)
        {
            list.Add(JsonNode.Parse(command.RawJson));
        }
        root["list"] = list;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void EnsureTerminator(List<MovementRouteCommandModel> commands)
    {
        if (commands.Count == 0 || commands[^1].Code != 0)
        {
            commands.Add(MovementRouteCommandModel.Create(0));
        }
        for (int index = commands.Count - 2; index >= 0; index--)
        {
            if (commands[index].Code == 0) commands.RemoveAt(index);
        }
    }
}

public sealed record MovementRouteSynchronizationResult(
    MovementRouteModel Route,
    IReadOnlyList<MovementRouteCommandModel> MirroredCommands,
    IReadOnlyList<EventStructureDiagnostic> Diagnostics)
{
    public bool IsSynchronized => Diagnostics.All(item => item.Severity != EventDiagnosticSeverity.Error);
}

public static class MovementRouteSynchronizer
{
    public static MovementRouteSynchronizationResult Analyze(
        RawEventCommand opening,
        IReadOnlyList<RawEventCommand> continuationRows)
    {
        List<EventStructureDiagnostic> diagnostics = [];
        JsonElement parameters = JsonParameterReader.ParseArray(opening.ParametersRawJson);
        if (!JsonParameterReader.TryGet(parameters, 1, out JsonElement routeElement) || routeElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10020",
                EventDiagnosticSeverity.Error,
                "Set Movement Route does not contain a route object.",
                opening.RawIndex));
            return new MovementRouteSynchronizationResult(MovementRouteModel.CreateDefault(), [], diagnostics);
        }
        MovementRouteModel route = MovementRouteModel.Parse(routeElement.GetRawText());
        List<MovementRouteCommandModel> mirrored = [];
        foreach (RawEventCommand row in continuationRows)
        {
            JsonElement p = JsonParameterReader.ParseArray(row.ParametersRawJson);
            if (!JsonParameterReader.TryGet(p, 0, out JsonElement command) || command.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10021",
                    EventDiagnosticSeverity.Error,
                    $"Movement continuation row {row.RawIndex} has no route-command object.",
                    row.RawIndex));
                continue;
            }
            mirrored.Add(MovementRouteCommandModel.Parse(command.GetRawText()));
        }
        MovementRouteCommandModel[] expected = route.Commands.Where(command => command.Code != 0).ToArray();
        if (expected.Length != mirrored.Count)
        {
            diagnostics.Add(new EventStructureDiagnostic(
                "PJRPG10022",
                EventDiagnosticSeverity.Error,
                $"Movement route contains {expected.Length} visible command(s), but {mirrored.Count} continuation row(s) are present.",
                opening.RawIndex,
                new EventCommandSpan(opening.RawIndex, 1 + continuationRows.Count)));
        }
        int count = Math.Min(expected.Length, mirrored.Count);
        for (int index = 0; index < count; index++)
        {
            if (!JsonEquivalent(expected[index].RawJson, mirrored[index].RawJson))
            {
                diagnostics.Add(new EventStructureDiagnostic(
                    "PJRPG10023",
                    EventDiagnosticSeverity.Error,
                    $"Movement route command {index + 1} disagrees with continuation row {continuationRows[index].RawIndex}.",
                    continuationRows[index].RawIndex));
            }
        }
        return new MovementRouteSynchronizationResult(route, mirrored, diagnostics);
    }

    public static IReadOnlyList<string> CreateEventCommandRows(
        int characterId,
        MovementRouteModel route,
        int indent)
    {
        ArgumentNullException.ThrowIfNull(route);
        string routeJson = route.Compile();
        string openingParameters = $"[{characterId.ToString(System.Globalization.CultureInfo.InvariantCulture)},{routeJson}]";
        List<string> rows = [RawEventCommand.CreateJson(205, indent, openingParameters)];
        foreach (MovementRouteCommandModel command in route.Commands.Where(command => command.Code != 0))
        {
            rows.Add(RawEventCommand.CreateJson(505, indent, $"[{command.RawJson}]"));
        }
        return rows;
    }

    private static bool JsonEquivalent(string first, string second)
    {
        using JsonDocument left = JsonDocument.Parse(first);
        using JsonDocument right = JsonDocument.Parse(second);
        return JsonElement.DeepEquals(left.RootElement, right.RootElement);
    }
}

using System.Text.Json;

namespace Playroma.JRPG.Eventing;

public sealed class EventCommandFormatter
{
    private readonly EventCommandRegistry _registry;
    private readonly Func<string, int, string>? _databaseNameResolver;

    public EventCommandFormatter(
        EventCommandRegistry registry,
        Func<string, int, string>? databaseNameResolver = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _databaseNameResolver = databaseNameResolver;
    }

    public EventCommandSummary Format(RawEventCommand command)
    {
        EventCommandDefinition definition = _registry.Get(command.Code);
        JsonElement p = JsonParameterReader.ParseArray(command.ParametersRawJson);
        string? details = command.Code switch
        {
            0 => null,
            101 => FormatShowText(p),
            102 => FormatChoices(p),
            103 => $"{Resolve("Variable", JsonParameterReader.GetInt(p, 0))}, {JsonParameterReader.GetInt(p, 1)} digit(s)",
            104 => $"{Resolve("Variable", JsonParameterReader.GetInt(p, 0))}, type {JsonParameterReader.GetInt(p, 1)}",
            105 => $"Speed {JsonParameterReader.GetInt(p, 0)}, no fast-forward: {YesNo(JsonParameterReader.GetBool(p, 1))}",
            108 or 401 or 405 or 408 or 655 => Quote(JsonParameterReader.GetString(p, 0)),
            111 => FormatCondition(p),
            117 => Resolve("CommonEvent", JsonParameterReader.GetInt(p, 0)),
            118 or 119 => JsonParameterReader.GetString(p, 0),
            121 => $"{Range("Switch", JsonParameterReader.GetInt(p, 0), JsonParameterReader.GetInt(p, 1))} = {(JsonParameterReader.GetInt(p, 2) == 0 ? "ON" : "OFF")}",
            122 => FormatVariables(p),
            123 => $"{JsonParameterReader.GetString(p, 0, "A")} = {(JsonParameterReader.GetInt(p, 1) == 0 ? "ON" : "OFF")}",
            124 => $"{(JsonParameterReader.GetInt(p, 0) == 0 ? "Start" : "Stop")} {JsonParameterReader.GetInt(p, 1)} second(s)",
            125 => FormatAmount("Gold", p, 0),
            126 => FormatAmount(Resolve("Item", JsonParameterReader.GetInt(p, 0)), p, 1),
            127 => FormatAmount(Resolve("Weapon", JsonParameterReader.GetInt(p, 0)), p, 1),
            128 => FormatAmount(Resolve("Armor", JsonParameterReader.GetInt(p, 0)), p, 1),
            129 => $"{Resolve("Actor", JsonParameterReader.GetInt(p, 0))}: {(JsonParameterReader.GetInt(p, 1) == 0 ? "Add" : "Remove")}",
            132 or 133 or 139 or 241 or 245 or 249 or 250 => FormatAudio(p, 0),
            134 => Toggle("Save", p),
            135 => Toggle("Menu", p),
            136 => Toggle("Encounters", p),
            137 => Toggle("Formation", p),
            138 => FormatArray(p, 0),
            140 => $"Vehicle {JsonParameterReader.GetInt(p, 0)}: {FormatAudio(p, 1)}",
            201 => FormatTransfer(p),
            202 => FormatVehicleLocation(p),
            203 => FormatEventLocation(p),
            204 => $"Direction {JsonParameterReader.GetInt(p, 0)}, {JsonParameterReader.GetInt(p, 1)} tile(s), speed {JsonParameterReader.GetInt(p, 2)}",
            205 => $"Character {JsonParameterReader.GetInt(p, 0)}, {CountRouteCommands(p)} route command(s)",
            211 => JsonParameterReader.GetInt(p, 0) == 0 ? "Transparent ON" : "Transparent OFF",
            212 => $"Character {JsonParameterReader.GetInt(p, 0)}, {Resolve("Animation", JsonParameterReader.GetInt(p, 1))}",
            213 => $"Character {JsonParameterReader.GetInt(p, 0)}, balloon {JsonParameterReader.GetInt(p, 1)}",
            216 => JsonParameterReader.GetInt(p, 0) == 0 ? "Followers visible" : "Followers hidden",
            223 or 224 or 234 => $"{FormatArray(p, command.Code == 234 ? 1 : 0)}, {JsonParameterReader.GetInt(p, command.Code == 234 ? 2 : 1)} frame(s)",
            225 => $"Power {JsonParameterReader.GetInt(p, 0)}, speed {JsonParameterReader.GetInt(p, 1)}, {JsonParameterReader.GetInt(p, 2)} frame(s)",
            230 => $"{JsonParameterReader.GetInt(p, 0)} frame(s)",
            231 => $"#{JsonParameterReader.GetInt(p, 0)} {JsonParameterReader.GetString(p, 1)} at ({JsonParameterReader.GetInt(p, 4)}, {JsonParameterReader.GetInt(p, 5)})",
            232 => $"#{JsonParameterReader.GetInt(p, 0)} to ({JsonParameterReader.GetInt(p, 3)}, {JsonParameterReader.GetInt(p, 4)}) in {JsonParameterReader.GetInt(p, 9)} frame(s)",
            233 => $"#{JsonParameterReader.GetInt(p, 0)}, speed {JsonParameterReader.GetInt(p, 1)}",
            235 => $"#{JsonParameterReader.GetInt(p, 0)}",
            236 => $"{JsonParameterReader.GetString(p, 0)}, power {JsonParameterReader.GetInt(p, 1)}, {JsonParameterReader.GetInt(p, 2)} frame(s)",
            242 or 246 => $"{JsonParameterReader.GetInt(p, 0)} second(s)",
            261 => JsonParameterReader.GetString(p, 0),
            281 => JsonParameterReader.GetInt(p, 0) == 0 ? "Enable" : "Disable",
            282 => Resolve("Tileset", JsonParameterReader.GetInt(p, 0)),
            283 => $"{JsonParameterReader.GetString(p, 0)} / {JsonParameterReader.GetString(p, 1)}",
            284 => JsonParameterReader.GetString(p, 0),
            285 => $"Result {Resolve("Variable", JsonParameterReader.GetInt(p, 0))}, type {JsonParameterReader.GetInt(p, 1)}",
            301 => FormatBattle(p),
            302 or 605 => FormatGoods(p),
            303 => $"{Resolve("Actor", JsonParameterReader.GetInt(p, 0))}, max {JsonParameterReader.GetInt(p, 1)} characters",
            >= 311 and <= 326 => FormatActorCommand(command.Code, p),
            >= 331 and <= 342 => FormatEnemyCommand(command.Code, p),
            355 or 356 => Quote(JsonParameterReader.GetString(p, 0)),
            402 => $"[{JsonParameterReader.GetInt(p, 0)}] {JsonParameterReader.GetString(p, 1)}",
            505 => FormatMoveCommand(p),
            _ => FormatGeneric(p)
        };
        return new EventCommandSummary(
            definition.Name,
            details,
            definition.Category,
            definition.Category == "Unknown" || command.ExtraPropertyCount > 0);
    }

    private string Resolve(string target, int id)
    {
        if (_databaseNameResolver is null) return $"{target} #{id}";
        string name = _databaseNameResolver(target, id);
        return string.IsNullOrWhiteSpace(name) ? $"{target} #{id}" : $"{target} #{id}: {name}";
    }

    private string Range(string target, int first, int last) => first == last
        ? Resolve(target, first)
        : $"{target}s #{first}–#{last}";

    private static string FormatShowText(JsonElement p)
    {
        string face = JsonParameterReader.GetString(p, 0);
        int index = JsonParameterReader.GetInt(p, 1);
        return string.IsNullOrWhiteSpace(face) ? "No face" : $"{face} [{index}]";
    }

    private static string FormatChoices(JsonElement p)
    {
        if (!JsonParameterReader.TryGet(p, 0, out JsonElement choices) || choices.ValueKind != JsonValueKind.Array) return "No choices";
        return string.Join(" / ", choices.EnumerateArray().Select(item => JsonParameterReader.Compact(item, 32)));
    }

    private string FormatCondition(JsonElement p)
    {
        int type = JsonParameterReader.GetInt(p, 0, -1);
        return type switch
        {
            0 => $"{Resolve("Switch", JsonParameterReader.GetInt(p, 1))} is {(JsonParameterReader.GetInt(p, 2) == 0 ? "ON" : "OFF")}",
            1 => $"{Resolve("Variable", JsonParameterReader.GetInt(p, 1))} comparison",
            2 => $"Self Switch {JsonParameterReader.GetString(p, 1)} is {(JsonParameterReader.GetInt(p, 2) == 0 ? "ON" : "OFF")}",
            3 => "Timer comparison",
            4 => $"Actor condition: {Resolve("Actor", JsonParameterReader.GetInt(p, 1))}",
            5 => $"Enemy #{JsonParameterReader.GetInt(p, 1) + 1} condition",
            6 => $"Character {JsonParameterReader.GetInt(p, 1)} facing {JsonParameterReader.GetInt(p, 2)}",
            7 => "Gold comparison",
            8 => $"Party has {Resolve("Item", JsonParameterReader.GetInt(p, 1))}",
            9 => $"Party has {Resolve("Weapon", JsonParameterReader.GetInt(p, 1))}",
            10 => $"Party has {Resolve("Armor", JsonParameterReader.GetInt(p, 1))}",
            11 => $"Button {JsonParameterReader.GetInt(p, 1)} is pressed",
            12 => $"Script: {JsonParameterReader.GetString(p, 1)}",
            _ => $"Condition type {type}"
        };
    }

    private string FormatVariables(JsonElement p)
    {
        int first = JsonParameterReader.GetInt(p, 0);
        int last = JsonParameterReader.GetInt(p, 1);
        int operation = JsonParameterReader.GetInt(p, 2);
        string op = operation switch { 0 => "=", 1 => "+=", 2 => "-=", 3 => "*=", 4 => "/=", 5 => "%=", _ => $"op {operation}" };
        return $"{Range("Variable", first, last)} {op} {(JsonParameterReader.TryGet(p, 4, out JsonElement operand) ? JsonParameterReader.Compact(operand) : "(missing operand)")}";
    }

    private static string FormatAmount(string target, JsonElement p, int operationIndex)
    {
        bool increase = JsonParameterReader.GetInt(p, operationIndex) == 0;
        int operandType = JsonParameterReader.GetInt(p, operationIndex + 1);
        int amount = JsonParameterReader.GetInt(p, operationIndex + 2);
        return $"{target}: {(increase ? "+" : "−")}{(operandType == 0 ? amount.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"Variable #{amount}")}";
    }

    private static string Toggle(string feature, JsonElement p) =>
        $"{feature} {(JsonParameterReader.GetInt(p, 0) == 0 ? "enabled" : "disabled")}";

    private static string FormatAudio(JsonElement p, int index)
    {
        string name = JsonParameterReader.GetObjectString(p, index, "name");
        if (string.IsNullOrWhiteSpace(name)) return "(None)";
        if (!JsonParameterReader.TryGet(p, index, out JsonElement audio) || audio.ValueKind != JsonValueKind.Object) return name;
        int volume = audio.TryGetProperty("volume", out JsonElement volumeElement) && volumeElement.TryGetInt32(out int parsedVolume) ? parsedVolume : 90;
        int pitch = audio.TryGetProperty("pitch", out JsonElement pitchElement) && pitchElement.TryGetInt32(out int parsedPitch) ? parsedPitch : 100;
        return $"{name} (vol {volume}, pitch {pitch})";
    }

    private string FormatTransfer(JsonElement p)
    {
        int designation = JsonParameterReader.GetInt(p, 0);
        return designation == 0
            ? $"{Resolve("Map", JsonParameterReader.GetInt(p, 1))} ({JsonParameterReader.GetInt(p, 2)}, {JsonParameterReader.GetInt(p, 3)})"
            : $"Variables {JsonParameterReader.GetInt(p, 1)}, {JsonParameterReader.GetInt(p, 2)}, {JsonParameterReader.GetInt(p, 3)}";
    }

    private string FormatVehicleLocation(JsonElement p) =>
        $"Vehicle {JsonParameterReader.GetInt(p, 0)} → {(JsonParameterReader.GetInt(p, 1) == 0 ? Resolve("Map", JsonParameterReader.GetInt(p, 2)) : $"Variable #{JsonParameterReader.GetInt(p, 2)}")} ({JsonParameterReader.GetInt(p, 3)}, {JsonParameterReader.GetInt(p, 4)})";

    private static string FormatEventLocation(JsonElement p) =>
        $"Character {JsonParameterReader.GetInt(p, 0)} → mode {JsonParameterReader.GetInt(p, 1)}, ({JsonParameterReader.GetInt(p, 2)}, {JsonParameterReader.GetInt(p, 3)})";

    private static int CountRouteCommands(JsonElement p)
    {
        if (!JsonParameterReader.TryGet(p, 1, out JsonElement route) || route.ValueKind != JsonValueKind.Object) return 0;
        if (!route.TryGetProperty("list", out JsonElement list) || list.ValueKind != JsonValueKind.Array) return 0;
        return list.GetArrayLength();
    }

    private string FormatBattle(JsonElement p)
    {
        int designation = JsonParameterReader.GetInt(p, 0);
        string troop = designation == 0 ? Resolve("Troop", JsonParameterReader.GetInt(p, 1)) : $"Variable #{JsonParameterReader.GetInt(p, 1)}";
        return $"{troop}; escape {YesNo(JsonParameterReader.GetBool(p, 2))}; lose {YesNo(JsonParameterReader.GetBool(p, 3))}";
    }

    private string FormatGoods(JsonElement p)
    {
        int type = JsonParameterReader.GetInt(p, 0);
        int id = JsonParameterReader.GetInt(p, 1);
        string target = type switch { 0 => "Item", 1 => "Weapon", 2 => "Armor", _ => "Goods" };
        return Resolve(target, id);
    }

    private string FormatActorCommand(int code, JsonElement p)
    {
        int actorId = code is 319 or 320 or 321 or 322 or 324 or 325 ? JsonParameterReader.GetInt(p, 0) : JsonParameterReader.GetInt(p, 1);
        string actor = Resolve("Actor", actorId);
        return code switch
        {
            313 => $"{actor}, {Resolve("State", JsonParameterReader.GetInt(p, 3))}",
            318 => $"{actor}, {Resolve("Skill", JsonParameterReader.GetInt(p, 3))}",
            319 => $"{actor}, slot {JsonParameterReader.GetInt(p, 1)}, item #{JsonParameterReader.GetInt(p, 2)}",
            320 => $"{actor} → {JsonParameterReader.GetString(p, 1)}",
            321 => $"{actor} → {Resolve("Class", JsonParameterReader.GetInt(p, 1))}",
            324 or 325 => $"{actor} → {JsonParameterReader.GetString(p, 1)}",
            _ => actor
        };
    }

    private string FormatEnemyCommand(int code, JsonElement p)
    {
        string enemy = $"Enemy #{JsonParameterReader.GetInt(p, 0) + 1}";
        return code switch
        {
            333 => $"{enemy}, {Resolve("State", JsonParameterReader.GetInt(p, 2))}",
            336 => $"{enemy} → {Resolve("Enemy", JsonParameterReader.GetInt(p, 1))}",
            337 => $"{enemy}, {Resolve("Animation", JsonParameterReader.GetInt(p, 1))}",
            339 => $"Skill {Resolve("Skill", JsonParameterReader.GetInt(p, 2))} → target {JsonParameterReader.GetInt(p, 3)}",
            _ => enemy
        };
    }

    private string FormatMoveCommand(JsonElement p)
    {
        if (!JsonParameterReader.TryGet(p, 0, out JsonElement move) || move.ValueKind != JsonValueKind.Object) return "Invalid route command";
        int code = move.TryGetProperty("code", out JsonElement codeElement) && codeElement.TryGetInt32(out int parsed) ? parsed : -1;
        return _registry.GetMovementRouteCommand(code).Name;
    }

    private static string FormatArray(JsonElement p, int index)
    {
        if (!JsonParameterReader.TryGet(p, index, out JsonElement value)) return "[]";
        return value.GetRawText();
    }

    private static string FormatGeneric(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() == 0) return string.Empty;
        return string.Join(", ", p.EnumerateArray().Take(4).Select(item => JsonParameterReader.Compact(item, 36)));
    }

    private static string Quote(string value) => string.IsNullOrEmpty(value) ? "(empty)" : $"\"{value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}\"";
    private static string YesNo(bool value) => value ? "yes" : "no";
}

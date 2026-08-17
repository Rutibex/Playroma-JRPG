using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Eventing;

public sealed record EventPageConditionModel(
    bool Switch1Valid,
    int Switch1Id,
    bool Switch2Valid,
    int Switch2Id,
    bool VariableValid,
    int VariableId,
    int VariableValue,
    bool SelfSwitchValid,
    string SelfSwitch,
    bool ItemValid,
    int ItemId,
    bool ActorValid,
    int ActorId);

public sealed record EventGraphicModel(
    int TileId,
    string CharacterName,
    int CharacterIndex,
    int Direction,
    int Pattern);

public sealed record EventPageSettingsSnapshot(
    EventListAddress Address,
    EventPageConditionModel Conditions,
    EventGraphicModel Graphic,
    int MoveType,
    int MoveSpeed,
    int MoveFrequency,
    MovementRouteModel MoveRoute,
    bool WalkAnimation,
    bool StepAnimation,
    bool DirectionFix,
    bool Through,
    int PriorityType,
    int Trigger,
    string RawOwnerJson)
{
    public static EventPageSettingsSnapshot Capture(MutableJsonProjectDocument document, EventListAddress address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Surface == EventSurfaceKind.CommonEvent)
        {
            throw new InvalidOperationException("Common events do not have map-page settings.");
        }

        string raw = document.GetRawJson(address.OwnerPath)
            ?? throw new InvalidDataException($"Event page '{address.OwnerPath}' does not exist.");
        return Parse(address, raw);
    }

    public static EventPageSettingsSnapshot Parse(EventListAddress address, string raw)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (address.Surface == EventSurfaceKind.CommonEvent)
        {
            throw new InvalidOperationException("Common events do not have map-page settings.");
        }

        JsonObject owner = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidDataException($"Event page '{address.OwnerPath}' is not an object.");
        JsonObject conditions = owner["conditions"] as JsonObject ?? [];
        JsonObject image = owner["image"] as JsonObject ?? [];
        string routeRaw = owner["moveRoute"]?.ToJsonString() ?? MovementRouteModel.CreateDefault().RawObjectJson;
        return new EventPageSettingsSnapshot(
            address,
            new EventPageConditionModel(
                Bool(conditions, "switch1Valid"), Int(conditions, "switch1Id", 1),
                Bool(conditions, "switch2Valid"), Int(conditions, "switch2Id", 1),
                Bool(conditions, "variableValid"), Int(conditions, "variableId", 1), Int(conditions, "variableValue", 0),
                Bool(conditions, "selfSwitchValid"), String(conditions, "selfSwitchCh", "A"),
                Bool(conditions, "itemValid"), Int(conditions, "itemId", 1),
                Bool(conditions, "actorValid"), Int(conditions, "actorId", 1)),
            new EventGraphicModel(
                Int(image, "tileId", 0), String(image, "characterName", string.Empty),
                Int(image, "characterIndex", 0), Int(image, "direction", 2), Int(image, "pattern", 1)),
            Int(owner, "moveType", 0), Int(owner, "moveSpeed", 3), Int(owner, "moveFrequency", 3),
            MovementRouteModel.Parse(routeRaw),
            Bool(owner, "walkAnime", true), Bool(owner, "stepAnime"), Bool(owner, "directionFix"), Bool(owner, "through"),
            Int(owner, "priorityType", 1), Int(owner, "trigger", 0), raw);
    }

    public string Compile()
    {
        JsonObject owner = JsonNode.Parse(RawOwnerJson) as JsonObject ?? [];
        JsonObject conditions = owner["conditions"] as JsonObject ?? [];
        conditions["switch1Valid"] = Conditions.Switch1Valid;
        conditions["switch1Id"] = Conditions.Switch1Id;
        conditions["switch2Valid"] = Conditions.Switch2Valid;
        conditions["switch2Id"] = Conditions.Switch2Id;
        conditions["variableValid"] = Conditions.VariableValid;
        conditions["variableId"] = Conditions.VariableId;
        conditions["variableValue"] = Conditions.VariableValue;
        conditions["selfSwitchValid"] = Conditions.SelfSwitchValid;
        conditions["selfSwitchCh"] = Conditions.SelfSwitch;
        conditions["itemValid"] = Conditions.ItemValid;
        conditions["itemId"] = Conditions.ItemId;
        conditions["actorValid"] = Conditions.ActorValid;
        conditions["actorId"] = Conditions.ActorId;
        owner["conditions"] = conditions;

        JsonObject image = owner["image"] as JsonObject ?? [];
        image["tileId"] = Graphic.TileId;
        image["characterName"] = Graphic.CharacterName;
        image["characterIndex"] = Graphic.CharacterIndex;
        image["direction"] = Graphic.Direction;
        image["pattern"] = Graphic.Pattern;
        owner["image"] = image;

        owner["moveType"] = MoveType;
        owner["moveSpeed"] = MoveSpeed;
        owner["moveFrequency"] = MoveFrequency;
        owner["moveRoute"] = JsonNode.Parse(MoveRoute.Compile());
        owner["walkAnime"] = WalkAnimation;
        owner["stepAnime"] = StepAnimation;
        owner["directionFix"] = DirectionFix;
        owner["through"] = Through;
        owner["priorityType"] = PriorityType;
        owner["trigger"] = Trigger;
        return owner.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static bool Bool(JsonObject value, string key, bool fallback = false) =>
        value[key] is JsonValue node && node.TryGetValue(out bool result) ? result : fallback;
    private static int Int(JsonObject value, string key, int fallback = 0) =>
        value[key] is JsonValue node && node.TryGetValue(out int result) ? result : fallback;
    private static string String(JsonObject value, string key, string fallback) =>
        value[key] is JsonValue node && node.TryGetValue(out string? result) ? result ?? fallback : fallback;
}

public sealed record CommonEventSettingsSnapshot(
    EventListAddress Address,
    string Name,
    int Trigger,
    int SwitchId,
    string RawOwnerJson)
{
    public static CommonEventSettingsSnapshot Capture(MutableJsonProjectDocument document, EventListAddress address)
    {
        if (address.Surface != EventSurfaceKind.CommonEvent) throw new InvalidOperationException("The address is not a common event.");
        string raw = document.GetRawJson(address.OwnerPath) ?? throw new InvalidDataException("The common event does not exist.");
        return Parse(address, raw);
    }

    public static CommonEventSettingsSnapshot Parse(EventListAddress address, string raw)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (address.Surface != EventSurfaceKind.CommonEvent) throw new InvalidOperationException("The address is not a common event.");
        JsonObject root = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException("The common event is not an object.");
        return new CommonEventSettingsSnapshot(
            address,
            root["name"]?.GetValue<string>() ?? string.Empty,
            root["trigger"]?.GetValue<int>() ?? 0,
            root["switchId"]?.GetValue<int>() ?? 1,
            raw);
    }

    public string Compile()
    {
        JsonObject root = JsonNode.Parse(RawOwnerJson) as JsonObject ?? [];
        root["name"] = Name;
        root["trigger"] = Trigger;
        root["switchId"] = SwitchId;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}



public sealed record MapEventHeaderSnapshot(
    EventListAddress Address,
    string Name,
    string Note,
    int X,
    int Y,
    string RawEventJson)
{
    public static MapEventHeaderSnapshot Capture(
        MutableJsonProjectDocument document,
        EventListAddress address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Surface != EventSurfaceKind.MapEvent)
        {
            throw new InvalidOperationException("The address is not a map event.");
        }

        JsonPath eventPath = JsonPath.Root.Append("events").Append(address.EventId ?? 0);
        string raw = document.GetRawJson(eventPath)
            ?? throw new InvalidDataException($"Map event '{eventPath}' does not exist.");
        return Parse(address, raw);
    }

    public static MapEventHeaderSnapshot Parse(EventListAddress address, string raw)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (address.Surface != EventSurfaceKind.MapEvent)
        {
            throw new InvalidOperationException("The address is not a map event.");
        }
        JsonPath eventPath = JsonPath.Root.Append("events").Append(address.EventId ?? 0);
        JsonObject root = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidDataException($"Map event '{eventPath}' is not an object.");
        return new MapEventHeaderSnapshot(
            address,
            root["name"]?.GetValue<string>() ?? string.Empty,
            root["note"]?.GetValue<string>() ?? string.Empty,
            root["x"]?.GetValue<int>() ?? 0,
            root["y"]?.GetValue<int>() ?? 0,
            raw);
    }

    public JsonPath EventPath => JsonPath.Root.Append("events").Append(Address.EventId ?? 0);

    public string Compile()
    {
        JsonObject root = JsonNode.Parse(RawEventJson) as JsonObject ?? [];
        root["name"] = Name;
        root["note"] = Note;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}

public sealed record TroopEventPageSettingsSnapshot(
    EventListAddress Address,
    bool TurnEnding,
    bool TurnValid,
    int TurnA,
    int TurnB,
    bool EnemyValid,
    int EnemyIndex,
    int EnemyHp,
    bool ActorValid,
    int ActorId,
    int ActorHp,
    bool SwitchValid,
    int SwitchId,
    int Span,
    string RawOwnerJson,
    string RawConditionsJson)
{
    public static TroopEventPageSettingsSnapshot Capture(
        MutableJsonProjectDocument document,
        EventListAddress address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Surface != EventSurfaceKind.TroopEvent)
        {
            throw new InvalidOperationException("The address is not a troop event page.");
        }

        string raw = document.GetRawJson(address.OwnerPath)
            ?? throw new InvalidDataException($"Troop event page '{address.OwnerPath}' does not exist.");
        return Parse(address, raw);
    }

    public static TroopEventPageSettingsSnapshot Parse(EventListAddress address, string raw)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (address.Surface != EventSurfaceKind.TroopEvent)
        {
            throw new InvalidOperationException("The address is not a troop event page.");
        }
        JsonObject owner = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidDataException($"Troop event page '{address.OwnerPath}' is not an object.");
        JsonObject conditions = owner["conditions"] as JsonObject ?? [];
        string conditionsRaw = owner["conditions"]?.ToJsonString() ?? "{}";
        return new TroopEventPageSettingsSnapshot(
            address,
            Bool(conditions, "turnEnding"),
            Bool(conditions, "turnValid"),
            Int(conditions, "turnA"),
            Int(conditions, "turnB"),
            Bool(conditions, "enemyValid"),
            Int(conditions, "enemyIndex"),
            Int(conditions, "enemyHp", 50),
            Bool(conditions, "actorValid"),
            Int(conditions, "actorId", 1),
            Int(conditions, "actorHp", 50),
            Bool(conditions, "switchValid"),
            Int(conditions, "switchId", 1),
            Int(owner, "span"),
            raw,
            conditionsRaw);
    }

    public string Compile()
    {
        JsonObject owner = JsonNode.Parse(RawOwnerJson) as JsonObject ?? [];
        JsonObject conditions = JsonNode.Parse(RawConditionsJson) as JsonObject ?? [];
        conditions["turnEnding"] = TurnEnding;
        conditions["turnValid"] = TurnValid;
        conditions["turnA"] = TurnA;
        conditions["turnB"] = TurnB;
        conditions["enemyValid"] = EnemyValid;
        conditions["enemyIndex"] = EnemyIndex;
        conditions["enemyHp"] = EnemyHp;
        conditions["actorValid"] = ActorValid;
        conditions["actorId"] = ActorId;
        conditions["actorHp"] = ActorHp;
        conditions["switchValid"] = SwitchValid;
        conditions["switchId"] = SwitchId;
        owner["conditions"] = conditions;
        owner["span"] = Span;
        return owner.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static bool Bool(JsonObject value, string key, bool fallback = false) =>
        value[key] is JsonValue node && node.TryGetValue(out bool result) ? result : fallback;

    private static int Int(JsonObject value, string key, int fallback = 0) =>
        value[key] is JsonValue node && node.TryGetValue(out int result) ? result : fallback;
}

public static class EventPageDefaults
{
    public static string CreateMapPageJson() =>
        "{\"conditions\":{\"actorId\":1,\"actorValid\":false,\"itemId\":1,\"itemValid\":false,\"selfSwitchCh\":\"A\",\"selfSwitchValid\":false,\"switch1Id\":1,\"switch1Valid\":false,\"switch2Id\":1,\"switch2Valid\":false,\"variableId\":1,\"variableValid\":false,\"variableValue\":0},\"directionFix\":false,\"image\":{\"characterIndex\":0,\"characterName\":\"\",\"direction\":2,\"pattern\":1,\"tileId\":0},\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}],\"moveFrequency\":3,\"moveRoute\":{\"list\":[{\"code\":0}],\"repeat\":true,\"skippable\":false,\"wait\":false},\"moveSpeed\":3,\"moveType\":0,\"priorityType\":1,\"stepAnime\":false,\"through\":false,\"trigger\":0,\"walkAnime\":true}";

    public static string CreateTroopPageJson() =>
        "{\"conditions\":{\"actorHp\":50,\"actorId\":1,\"actorValid\":false,\"enemyHp\":50,\"enemyIndex\":0,\"enemyValid\":false,\"switchId\":1,\"switchValid\":false,\"turnA\":0,\"turnB\":0,\"turnEnding\":false,\"turnValid\":false},\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}],\"span\":0}";
}

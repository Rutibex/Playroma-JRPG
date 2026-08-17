using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Action Test)> tests =
[
    ("Registry contains all 122 event commands", RegistryCount),
    ("Registry contains all 46 movement commands", RouteRegistryCount),
    ("Structural projection round-trips nested blocks", StructuralRoundTrip),
    ("Malformed continuations are preserved as opaque nodes", MalformedPreservation),
    ("Movement route mirrors 205 and 505 rows", MovementSynchronization),
    ("Clipboard JSON and readable text round-trip", ClipboardRoundTrip),
    ("Clipboard distinguishes direct and variable destinations", ClipboardVariableReferences),
    ("Choice editing synchronizes branch markers", ChoiceSynchronization),
    ("Battle editing synchronizes result branches", BattleSynchronization),
    ("Search indexes command summaries and raw parameters", SearchIndex),
    ("Map page settings preserve unknown fields", PageSettingsPreserveUnknown),
    ("Map event headers preserve unknown fields", MapHeaderPreserveUnknown),
    ("Troop page settings preserve unknown fields", TroopSettingsPreserveUnknown)
];
int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Eventing tests passed.");
return failures == 0 ? 0 : 1;

static EventCommandRegistry Registry() => EventCommandRegistry.CreateBuiltIn(FindRepositoryRoot());
static void RegistryCount() { Equal(122, Registry().Commands.Count); True(Registry().Commands.Any(item => item.Code == 355)); }
static void RouteRegistryCount() { Equal(46, Registry().MovementRouteCommands.Count); True(Registry().MovementRouteCommands.Any(item => item.Code == 45)); }

static void StructuralRoundTrip()
{
    string[] raw =
    [
        RawEventCommand.CreateJson(111, 0, "[0,1,0]"),
        RawEventCommand.CreateJson(101, 1, "[\"\",0,0,2]"),
        RawEventCommand.CreateJson(401, 1, "[\"Yes\"]"),
        RawEventCommand.CreateJson(411, 0, "[]"),
        RawEventCommand.CreateJson(102, 1, "[[\"A\",\"B\"],-1,0,2,0]"),
        RawEventCommand.CreateJson(402, 1, "[0,\"A\"]"),
        RawEventCommand.CreateJson(230, 2, "[10]"),
        RawEventCommand.CreateJson(402, 1, "[1,\"B\"]"),
        RawEventCommand.CreateJson(404, 1, "[]"),
        RawEventCommand.CreateJson(412, 0, "[]"),
        RawEventCommand.CreateJson(0, 0, "[]")
    ];
    EventListSnapshot snapshot = Snapshot(raw);
    EventStructureDocument structure = new EventStructureProjector().Project(snapshot);
    True(structure.Nodes[0] is EventBlockNode);
    True(new EventStructureCompiler().IsLosslessRoundTrip(structure, out string? reason), reason ?? "Round trip failed.");
}

static void MalformedPreservation()
{
    string[] raw = [RawEventCommand.CreateJson(401, 0, "[\"orphan\"]"), RawEventCommand.CreateJson(0, 0, "[]")];
    EventStructureDocument structure = new EventStructureProjector().Project(Snapshot(raw));
    True(structure.Nodes[0] is EventOpaqueNode);
    Equal(raw[0], structure.Nodes[0].RawCommands[0].RawJson);
    True(structure.Diagnostics.Count > 0);
}

static void MovementSynchronization()
{
    MovementRouteModel route = new(false, true, true,
        [MovementRouteCommandModel.Create(1), MovementRouteCommandModel.Create(14, "[2,1]"), MovementRouteCommandModel.Create(0)],
        MovementRouteModel.CreateDefault().RawObjectJson);
    IReadOnlyList<string> rows = MovementRouteSynchronizer.CreateEventCommandRows(-1, route, 2);
    RawEventCommand opening = RawEventCommand.Parse(0, rows[0]);
    RawEventCommand[] continuations = rows.Skip(1).Select((raw, index) => RawEventCommand.Parse(index + 1, raw)).ToArray();
    MovementRouteSynchronizationResult result = MovementRouteSynchronizer.Analyze(opening, continuations);
    True(result.IsSynchronized, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    Equal(2, result.MirroredCommands.Count);
}

static void ClipboardRoundTrip()
{
    EventCommandRegistry registry = Registry(); EventCommandFormatter formatter = new(registry);
    RawEventCommand[] rows = [RawEventCommand.Parse(0, RawEventCommand.CreateJson(126, 0, "[5,0,0,2]"))];
    EventClipboardPackage package = EventClipboardCodec.Create(rows, "test", registry, formatter);
    EventClipboardPackage restored = EventClipboardCodec.Deserialize(EventClipboardCodec.Serialize(package));
    Equal(rows[0].RawJson, restored.Commands[0]);
    Equal(rows[0].RawJson, EventClipboardCodec.ParseReadableText(restored.ReadableText)[0]);
    True(restored.References.Any(item => item.Kind == "item" && item.Id == 5));
}

static void ClipboardVariableReferences()
{
    EventCommandRegistry registry = Registry(); EventCommandFormatter formatter = new(registry);
    RawEventCommand[] rows =
    [
        RawEventCommand.Parse(0, RawEventCommand.CreateJson(201, 0, "[1,11,12,13,2,0]")),
        RawEventCommand.Parse(1, RawEventCommand.CreateJson(301, 0, "[1,22,false,false]"))
    ];
    EventClipboardPackage package = EventClipboardCodec.Create(rows, "test", registry, formatter);
    True(package.References.Any(item => item.Kind == "variable" && item.Id == 11));
    True(package.References.Any(item => item.Kind == "variable" && item.Id == 12));
    True(package.References.Any(item => item.Kind == "variable" && item.Id == 13));
    True(package.References.Any(item => item.Kind == "variable" && item.Id == 22));
    True(!package.References.Any(item => item.Kind == "map" && item.Id == 11));
    True(!package.References.Any(item => item.Kind == "troop" && item.Id == 22));
}

static void ChoiceSynchronization()
{
    string[] raw =
    [
        RawEventCommand.CreateJson(102, 0, "[[\"A\",\"B\"],-1,0,2,0]"),
        RawEventCommand.CreateJson(402, 0, "[0,\"A\"]"),
        RawEventCommand.CreateJson(230, 1, "[10]"),
        RawEventCommand.CreateJson(402, 0, "[1,\"B\"]"),
        RawEventCommand.CreateJson(230, 1, "[20]"),
        RawEventCommand.CreateJson(403, 0, "[]"),
        RawEventCommand.CreateJson(230, 1, "[30]"),
        RawEventCommand.CreateJson(404, 0, "[]")
    ];
    EventBlockNode block = (EventBlockNode)new EventStructureProjector().Project(Snapshot(raw)).Nodes[0];
    IReadOnlyList<string> rewritten = EventStructuralCommandSynchronizer.Rewrite(block, "[[\"Alpha\",\"Beta\",\"Gamma\"],-2,0,2,0]");
    RawEventCommand[] commands = rewritten.Select((item, index) => RawEventCommand.Parse(index, item)).ToArray();
    Equal(3, commands.Count(item => item.Code == 402));
    Equal(0, commands.Count(item => item.Code == 403));
    True(commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[10]"));
    True(commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[20]"));
    True(!commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[30]"));
    JsonElement third = JsonParameterReader.ParseArray(commands.Last(item => item.Code == 402).ParametersRawJson);
    Equal("Gamma", JsonParameterReader.GetString(third, 1));
}

static void BattleSynchronization()
{
    string[] raw =
    [
        RawEventCommand.CreateJson(301, 0, "[0,5,true,true]"),
        RawEventCommand.CreateJson(601, 0, "[]"),
        RawEventCommand.CreateJson(230, 1, "[1]"),
        RawEventCommand.CreateJson(602, 0, "[]"),
        RawEventCommand.CreateJson(230, 1, "[2]"),
        RawEventCommand.CreateJson(603, 0, "[]"),
        RawEventCommand.CreateJson(230, 1, "[3]"),
        RawEventCommand.CreateJson(604, 0, "[]")
    ];
    EventBlockNode block = (EventBlockNode)new EventStructureProjector().Project(Snapshot(raw)).Nodes[0];
    IReadOnlyList<string> rewritten = EventStructuralCommandSynchronizer.Rewrite(block, "[0,5,true,false]");
    RawEventCommand[] commands = rewritten.Select((item, index) => RawEventCommand.Parse(index, item)).ToArray();
    True(commands.Any(item => item.Code == 601));
    True(commands.Any(item => item.Code == 602));
    True(!commands.Any(item => item.Code == 603));
    True(commands.Any(item => item.Code == 604));
    True(commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[1]"));
    True(commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[2]"));
    True(!commands.Any(item => item.Code == 230 && item.ParametersRawJson == "[3]"));
}

static void SearchIndex()
{
    EventCommandRegistry registry = Registry(); EventCommandFormatter formatter = new(registry);
    EventListSnapshot snapshot = Snapshot([RawEventCommand.CreateJson(101, 0, "[\"Actor1\",0,0,2]"), RawEventCommand.CreateJson(401, 0, "[\"Find the crystal\"]")]);
    EventSearchIndexer index = new(registry, formatter);
    True(index.Search(snapshot, "Show Text").Count == 1);
    True(index.Search(snapshot, "crystal").Count == 1);
}

static void PageSettingsPreserveUnknown()
{
    string json = "{\"events\":[null,{\"id\":1,\"name\":\"E\",\"x\":0,\"y\":0,\"pages\":[{\"conditions\":{},\"image\":{},\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}],\"moveRoute\":{\"list\":[{\"code\":0}],\"repeat\":true,\"skippable\":false,\"wait\":false},\"pluginPageData\":{\"keep\":true}}]}]}";
    MutableJsonProjectDocument document = Create(json, MvDocumentKind.Map, "data/Map001.json");
    EventListAddress address = EventListAddress.ForMapEvent("data/Map001.json", 1, 1, 0);
    EventPageSettingsSnapshot snapshot = EventPageSettingsSnapshot.Capture(document, address);
    string compiled = (snapshot with { Trigger = 3 }).Compile();
    using JsonDocument parsed = JsonDocument.Parse(compiled);
    True(parsed.RootElement.GetProperty("pluginPageData").GetProperty("keep").GetBoolean());
    Equal(3, parsed.RootElement.GetProperty("trigger").GetInt32());
}

static void MapHeaderPreserveUnknown()
{
    string json = "{\"events\":[null,{\"id\":1,\"name\":\"Old\",\"note\":\"N\",\"x\":3,\"y\":4,\"pluginEventData\":{\"keep\":true},\"pages\":[]}]}";
    MutableJsonProjectDocument document = Create(json, MvDocumentKind.Map, "data/Map001.json");
    EventListAddress address = EventListAddress.ForMapEvent("data/Map001.json", 1, 1, 0);
    MapEventHeaderSnapshot snapshot = MapEventHeaderSnapshot.Capture(document, address);
    using JsonDocument parsed = JsonDocument.Parse((snapshot with { Name = "New" }).Compile());
    Equal("New", parsed.RootElement.GetProperty("name").GetString());
    True(parsed.RootElement.GetProperty("pluginEventData").GetProperty("keep").GetBoolean());
    Equal(3, parsed.RootElement.GetProperty("x").GetInt32());
}

static void TroopSettingsPreserveUnknown()
{
    string json = "[null,{\"id\":1,\"pages\":[{\"conditions\":{\"turnEnding\":false,\"turnValid\":false,\"turnA\":0,\"turnB\":0,\"enemyValid\":false,\"enemyIndex\":0,\"enemyHp\":50,\"actorValid\":false,\"actorId\":1,\"actorHp\":50,\"switchValid\":false,\"switchId\":1,\"pluginCondition\":7},\"span\":0,\"pluginPageData\":{\"keep\":true},\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}]}]}]";
    MutableJsonProjectDocument document = Create(json, MvDocumentKind.Troops, "data/Troops.json");
    EventListAddress address = EventListAddress.ForTroopEvent("data/Troops.json", 1, 0);
    TroopEventPageSettingsSnapshot snapshot = TroopEventPageSettingsSnapshot.Capture(document, address);
    using JsonDocument parsed = JsonDocument.Parse((snapshot with { Span = 2, TurnEnding = true }).Compile());
    Equal(2, parsed.RootElement.GetProperty("span").GetInt32());
    True(parsed.RootElement.GetProperty("conditions").GetProperty("turnEnding").GetBoolean());
    Equal(7, parsed.RootElement.GetProperty("conditions").GetProperty("pluginCondition").GetInt32());
    True(parsed.RootElement.GetProperty("pluginPageData").GetProperty("keep").GetBoolean());
}

static EventListSnapshot Snapshot(IReadOnlyList<string> raw)
{
    RawEventCommand[] commands = raw.Select((item, index) => RawEventCommand.Parse(index, item)).ToArray();
    string array = "[" + string.Join(',', raw) + "]";
    return new EventListSnapshot(EventListAddress.ForCommonEvent("data/CommonEvents.json", 1), commands, array, "test", DateTimeOffset.UtcNow);
}
static MutableJsonProjectDocument Create(string json, MvDocumentKind kind, string path)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    ContentFingerprint fingerprint = new(bytes.LongLength, DateTimeOffset.UnixEpoch, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    return MutableJsonProjectDocument.Create(path, kind, bytes, fingerprint);
}
static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null) { if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName; current = current.Parent; }
    throw new DirectoryNotFoundException();
}
static void True(bool value, string message = "Assertion was false.") { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }

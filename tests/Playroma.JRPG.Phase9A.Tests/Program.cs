using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Func<Task> Test)> tests =
[
    ("Actor and Class contracts are independent from visible forms", ContractsAsync),
    ("Actor and Class snapshots read standard and plugin data safely", SnapshotReadersAsync),
    ("Field validators reject values outside descriptor bounds", FieldValidationAsync),
    ("State templates contain only standard MV removal fields", StateTemplateAsync),
    ("Class learning edits preserve unknown properties", LearningPreservationAsync),
    ("Class learning serialization preserves editor order", LearningOrderAsync),
    ("MV EXP calculation is deterministic", ExperienceCurveAsync),
    ("Smooth parameter generation covers indexes zero through ninety-nine", SmoothCurveAsync),
    ("Actor profile and nickname are searchable", ExpandedSearchAsync),
    ("Incremental record updates preserve unrelated index entries", IncrementalIndexAsync),
    ("Equipment legality follows System slots and dual-wield traits", EquipmentLegalityAsync)
];

int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try
    {
        await test().ConfigureAwait(false);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + exception);
    }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 9A tests passed.");
return failures == 0 ? 0 : 1;

static Task ContractsAsync()
{
    MvRecordContract actor = MvRecordContractCatalog.Get(MvDocumentKind.Actors);
    MvRecordContract @class = MvRecordContractCatalog.Get(MvDocumentKind.Classes);
    True(actor.IsStandard("equips"));
    True(actor.IsStandard("traits"));
    True(@class.IsStandard("expParams"));
    True(@class.IsStandard("params"));
    True(@class.IsStandard("learnings"));
    IDatabaseEditorDefinition actorEditor = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.Actors);
    True(ReferenceEquals(actorEditor.RecordSchema.Contract, actor));
    return Task.CompletedTask;
}

static Task SnapshotReadersAsync()
{
    MutableJsonProjectDocument actors = Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        "[null,{\"id\":1,\"battlerName\":\"Hero\",\"characterIndex\":3,\"characterName\":\"People1\",\"classId\":2,\"equips\":[4,5,0],\"faceIndex\":6,\"faceName\":\"Faces1\",\"traits\":[],\"initialLevel\":7,\"maxLevel\":88,\"name\":\"Ada\",\"nickname\":\"Ace\",\"note\":\"<Plugin:X>\",\"profile\":\"Explorer\",\"pluginUnknown\":true}]" );
    MvActorRecordSnapshot actor = ActorClassRecordReader.ReadActor(actors, 1);
    Equal("Ada", actor.Name);
    Equal("Ace", actor.Nickname);
    Equal(3, actor.CharacterIndex);
    Equal(3, actor.EquipmentIds.Count);

    int[] curve = Enumerable.Range(0, 100).ToArray();
    string classesRaw = "[null,{\"id\":1,\"expParams\":[30,20,30,30],\"traits\":[],\"learnings\":[{\"level\":4,\"skillId\":9,\"pluginFlag\":\"keep\"}],\"name\":\"Hero\",\"note\":\"\",\"params\":[" + string.Join(',', Enumerable.Repeat(JsonSerializer.Serialize(curve), 8)) + "]}]";
    MvClassRecordSnapshot @class = ActorClassRecordReader.ReadClass(Create("data/Classes.json", MvDocumentKind.Classes, classesRaw), 1);
    Equal(8, @class.ParameterCurves.Count);
    Equal(0, @class.TraitCount);
    True(@class.Learnings.Single().RawJson.Contains("pluginFlag", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task FieldValidationAsync()
{
    IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(MvDocumentKind.Actors);
    DatabaseFieldDescriptor level = definition.RecordSchema.FindField("initialLevel");
    True(level.ValidateObject(0)?.Code == DiagnosticCodes.Phase9AInvalidFieldValue);
    True(level.ValidateObject(100)?.Code == DiagnosticCodes.Phase9AInvalidFieldValue);
    True(level.ValidateObject(42) is null);
    True(level.ValidateObject("42")?.Severity == DiagnosticSeverity.Error);
    return Task.CompletedTask;
}

static Task StateTemplateAsync()
{
    using JsonDocument state = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.States, 7));
    True(state.RootElement.TryGetProperty("removeByDamage", out _));
    False(state.RootElement.TryGetProperty("releaseByDamage", out _));
    return Task.CompletedTask;
}

static Task LearningPreservationAsync()
{
    string raw = ActorClassMutationService.BuildClassLearningsJson(
    [
        new MvClassLearningSnapshot(12, 31, "{\"level\":1,\"skillId\":2,\"pluginData\":{\"ordered\":[3,2,1]}}")
    ]);
    using JsonDocument document = JsonDocument.Parse(raw);
    JsonElement learning = document.RootElement[0];
    Equal(12, learning.GetProperty("level").GetInt32());
    Equal(31, learning.GetProperty("skillId").GetInt32());
    Equal(3, learning.GetProperty("pluginData").GetProperty("ordered")[0].GetInt32());
    return Task.CompletedTask;
}

static Task LearningOrderAsync()
{
    string raw = ActorClassMutationService.BuildClassLearningsJson(
    [
        new MvClassLearningSnapshot(40, 9, "{\"level\":40,\"skillId\":9,\"marker\":\"first\"}"),
        new MvClassLearningSnapshot(5, 2, "{\"level\":5,\"skillId\":2,\"marker\":\"second\"}")
    ]);
    using JsonDocument document = JsonDocument.Parse(raw);
    Equal("first", document.RootElement[0].GetProperty("marker").GetString()!);
    Equal(40, document.RootElement[0].GetProperty("level").GetInt32());
    Equal("second", document.RootElement[1].GetProperty("marker").GetString()!);
    Equal(5, document.RootElement[1].GetProperty("level").GetInt32());
    return Task.CompletedTask;
}

static Task ExperienceCurveAsync()
{
    IReadOnlyList<int> parameters = [30, 20, 30, 30];
    Equal(0L, ActorClassMutationService.ExperienceForLevel(1, parameters));
    True(ActorClassMutationService.ExperienceForLevel(50, parameters) > ActorClassMutationService.ExperienceForLevel(10, parameters));
    Equal(ActorClassMutationService.ExperienceForLevel(99, parameters), ActorClassMutationService.ExperienceForLevel(200, parameters));
    return Task.CompletedTask;
}

static Task SmoothCurveAsync()
{
    int[] curve = ActorClassMutationService.GenerateSmoothCurve(100, 1000, 1.5);
    Equal(100, curve.Length);
    Equal(100, curve[1]);
    Equal(1000, curve[99]);
    True(curve.Zip(curve.Skip(1)).All(pair => pair.First <= pair.Second));
    return Task.CompletedTask;
}

static Task ExpandedSearchAsync()
{
    MutableJsonProjectDocument document = Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        "[null,{\"id\":1,\"name\":\"Ada\",\"nickname\":\"Crimson Fox\",\"profile\":\"Explores the moon\",\"note\":\"\"}]" );
    DatabaseRecordIndex index = new(document);
    Equal(1, index.Query(new DatabaseRecordFilter(Text: "Crimson Fox")).Single().Id);
    Equal(1, index.Query(new DatabaseRecordFilter(Text: "moon")).Single().Id);
    return Task.CompletedTask;
}

static Task IncrementalIndexAsync()
{
    MutableJsonProjectDocument document = Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        "[null,{\"id\":1,\"name\":\"One\",\"note\":\"\"},{\"id\":2,\"name\":\"Two\",\"note\":\"\"}]" );
    DatabaseRecordIndex index = new(document);
    DatabaseRecordIndexEntry before = index.Find(2)!;
    document.SetRawJson("/1/name", Encoding.UTF8.GetBytes("\"Changed\""));
    index.UpdateRecords([1]);
    Equal("Changed", index.Find(1)!.Name);
    True(ReferenceEquals(before, index.Find(2)));
    return Task.CompletedTask;
}


static Task EquipmentLegalityAsync()
{
    MutableJsonProjectDocument actors = Create(
        "data/Actors.json",
        MvDocumentKind.Actors,
        "[null,{\"id\":1,\"battlerName\":\"\",\"characterIndex\":0,\"characterName\":\"\",\"classId\":1,\"equips\":[1,2,0,0,0],\"faceIndex\":0,\"faceName\":\"\",\"traits\":[],\"initialLevel\":1,\"maxLevel\":99,\"name\":\"Dual\",\"nickname\":\"\",\"note\":\"\",\"profile\":\"\"}]");
    MutableJsonProjectDocument classes = Create(
        "data/Classes.json",
        MvDocumentKind.Classes,
        "[null,{\"id\":1,\"expParams\":[30,20,30,30],\"traits\":[{\"code\":51,\"dataId\":1,\"value\":0},{\"code\":55,\"dataId\":1,\"value\":0}],\"learnings\":[],\"name\":\"Dual Wielder\",\"note\":\"\",\"params\":[]}]");
    MutableJsonProjectDocument system = Create(
        "data/System.json",
        MvDocumentKind.System,
        "{\"equipTypes\":[\"\",\"Weapon\",\"Shield\",\"Head\",\"Body\",\"Accessory\"]}");
    MutableJsonProjectDocument weapons = Create(
        "data/Weapons.json",
        MvDocumentKind.Weapons,
        "[null,{\"id\":1,\"name\":\"Blade A\",\"etypeId\":1,\"wtypeId\":1},{\"id\":2,\"name\":\"Blade B\",\"etypeId\":1,\"wtypeId\":1}]");
    MutableJsonProjectDocument armors = Create(
        "data/Armors.json",
        MvDocumentKind.Armors,
        "[null]");

    ActorEquipmentLegalityReport report = ActorEquipmentLegalityAnalyzer.Analyze(
        actors, classes, system, weapons, armors, 1);
    True(report.IsDualWield);
    Equal(1, report.Entries[1].ExpectedEquipTypeId);
    True(report.Entries[0].IsLegal);
    True(report.Entries[1].IsLegal);
    return Task.CompletedTask;
}

static MutableJsonProjectDocument Create(string path, MvDocumentKind kind, string json)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    return MutableJsonProjectDocument.Create(path, kind, bytes, new ContentFingerprint(
        bytes.LongLength,
        DateTimeOffset.UnixEpoch,
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
}

static void True(bool condition, string message = "Assertion was false.") { if (!condition) throw new InvalidOperationException(message); }
static void False(bool condition, string message = "Assertion was true.") => True(!condition, message);
static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

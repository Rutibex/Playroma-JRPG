using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Action Test)> tests =
[
    ("Enemy template has a safe default action", EnemyTemplate),
    ("Troop template has one valid empty page", TroopTemplate),
    ("Enemy record reader preserves unknown nested and root data", EnemyReader),
    ("Troop record reader preserves pages, members, and raw commands", TroopReader),
    ("Action ratings follow the MV cutoff and weighting rule", ActionRatings),
    ("All standard action condition types evaluate", ActionConditions),
    ("Drop expectations and seeded simulation are deterministic", Drops),
    ("Formation operations preserve member indexes", FormationIndexes),
    ("Member dependency analysis detects page and command references", MemberReferences),
    ("Difficulty estimation is explainable and deterministic", Difficulty),
    ("Multi-wave compilation emits hidden members and standard commands", MultiWave),
    ("Battle-test profiles preserve extended battler data", BattleTestProfileRoundTrip),
    ("Raw-backed serializers preserve extension properties", RawPreservation)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 9C tests passed.");
return failures == 0 ? 0 : 1;

static void EnemyTemplate()
{
    using JsonDocument json = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.Enemies, 1));
    JsonElement actions = json.RootElement.GetProperty("actions");
    Equal(1, actions.GetArrayLength());
    JsonElement action = actions[0];
    Equal(1, action.GetProperty("skillId").GetInt32());
    Equal(0, action.GetProperty("conditionType").GetInt32());
    Equal(5, action.GetProperty("rating").GetInt32());
    JsonElement parameters = json.RootElement.GetProperty("params");
    Equal(100, parameters[0].GetInt32());
    Equal(0, parameters[1].GetInt32());
}

static void TroopTemplate()
{
    using JsonDocument json = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.Troops, 1));
    JsonElement pages = json.RootElement.GetProperty("pages");
    Equal(1, pages.GetArrayLength());
    JsonElement page = pages[0];
    Equal(0, page.GetProperty("span").GetInt32());
    JsonElement commands = page.GetProperty("list");
    Equal(1, commands.GetArrayLength());
    Equal(0, commands[0].GetProperty("code").GetInt32());
}

static void EnemyReader()
{
    string json = "[null,{\"id\":1,\"name\":\"Witch\",\"battlerName\":\"Witch\",\"battlerHue\":120,\"params\":[100,20,30,40,50,60,70,80],\"exp\":99,\"gold\":42,\"dropItems\":[{\"kind\":1,\"dataId\":3,\"denominator\":20,\"pluginDrop\":true}],\"actions\":[{\"skillId\":4,\"conditionType\":2,\"conditionParam1\":0,\"conditionParam2\":0.5,\"rating\":8,\"pluginAction\":1}],\"traits\":[],\"note\":\"<Level: 5>\",\"pluginRoot\":\"keep\"}]";
    MvEnemyRecordSnapshot enemy = Phase9CRecordReader.ReadEnemy(Create("data/Enemies.json", MvDocumentKind.Enemies, json), 1);
    Equal("Witch", enemy.Name);
    Equal(120, enemy.BattlerHue);
    Equal(8, enemy.Parameters.Count);
    True(enemy.Drops[0].RawJson.Contains("pluginDrop", StringComparison.Ordinal));
    True(enemy.Actions[0].RawJson.Contains("pluginAction", StringComparison.Ordinal));
    Equal("\"keep\"", enemy.ExtensionFields["pluginRoot"]);
}

static void TroopReader()
{
    string json = "[null,{\"id\":1,\"name\":\"Witches\",\"members\":[{\"enemyId\":1,\"x\":410,\"y\":350,\"hidden\":true,\"pluginMember\":9}],\"pages\":[{\"conditions\":{\"turnEnding\":false,\"turnValid\":false,\"enemyValid\":true,\"enemyIndex\":0,\"enemyHp\":50,\"actorValid\":false,\"actorId\":1,\"actorHp\":50,\"switchValid\":false,\"switchId\":1,\"turnA\":0,\"turnB\":0},\"list\":[{\"code\":335,\"indent\":0,\"parameters\":[0]},{\"code\":0,\"indent\":0,\"parameters\":[]}],\"span\":1,\"pluginPage\":true}],\"pluginTroop\":5}]";
    MvTroopRecordSnapshot troop = Phase9CRecordReader.ReadTroop(Create("data/Troops.json", MvDocumentKind.Troops, json), 1);
    Equal(1, troop.Members.Count);
    True(troop.Members[0].Hidden);
    Equal(2, troop.Pages[0].CommandCount);
    True(troop.Pages[0].RawJson.Contains("pluginPage", StringComparison.Ordinal));
    Equal("5", troop.ExtensionFields["pluginTroop"]);
}

static void ActionRatings()
{
    MvEnemyActionSnapshot[] actions =
    [
        Action(1, 9), Action(2, 8), Action(3, 7), Action(4, 5)
    ];
    EnemyActionScenario scenario = new(1, 1, 1, new HashSet<int>(), 1, new Dictionary<int, bool>(), 999);
    IReadOnlyList<EnemyActionEvaluation> result = EnemyActionRuleEvaluator.Evaluate(actions, scenario);
    Equal(3, result[0].Weight);
    Equal(2, result[1].Weight);
    Equal(1, result[2].Weight);
    Equal(0, result[3].Weight);
    Near(0.5, result[0].Probability);
    Near(1d / 3d, result[1].Probability);
    Near(1d / 6d, result[2].Probability);
    False(result[3].IncludedByRating);
}

static void ActionConditions()
{
    EnemyActionScenario scenario = new(5, 0.4, 0.6, new HashSet<int> { 9 }, 12, new Dictionary<int, bool> { [7] = true }, 50);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 0), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 1, 3, 2), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 2, 0.2, 0.5), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 3, 0.5, 0.8), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 4, 9, 0), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 5, 10, 0), scenario).IsMet);
    True(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 6, 7, 0), scenario).IsMet);
    False(EnemyActionRuleEvaluator.CheckCondition(Action(1, 5, 99, 0, 0), scenario).IsMet);
}

static void Drops()
{
    MvEnemyRecordSnapshot enemy = new(1, "Slime", "Slime", 0, new int[8], 10, 5,
        [new MvEnemyDropSnapshot(1, 2, 20, "{\"kind\":1,\"dataId\":2,\"denominator\":20}")], [], 0, "", new Dictionary<string, string>());
    EnemyRewardSummary summary = EnemyRewardCalculator.CalculateEnemy(enemy, 100, doubleDrops: false);
    Near(5, summary.Drops[0].ExpectedPerBattle);
    IReadOnlyDictionary<(int Kind, int DataId), int> first = EnemyRewardCalculator.SimulateDrops(enemy.Drops, 1000, 123);
    IReadOnlyDictionary<(int Kind, int DataId), int> second = EnemyRewardCalculator.SimulateDrops(enemy.Drops, 1000, 123);
    Equal(first[(1, 2)], second[(1, 2)]);
}

static void FormationIndexes()
{
    MvTroopMemberSnapshot[] members =
    [
        Member(0, 1, 100, 300), Member(1, 2, 200, 300), Member(2, 3, 300, 300)
    ];
    IReadOnlyList<MvTroopMemberSnapshot> moved = TroopFormationPlanner.Move(members, new HashSet<int> { 1 }, 40, -20);
    Equal(0, moved[0].Index);
    Equal(1, moved[1].Index);
    Equal(2, moved[2].Index);
    Equal(240, moved[1].X);
    IReadOnlyList<MvTroopMemberSnapshot> planned = TroopFormationPlanner.Plan(members, TroopFormationPreset.Arc);
    Equal(new[] { 0, 1, 2 }, planned.Select(item => item.Index).ToArray());
}

static void MemberReferences()
{
    MvTroopPageConditionSnapshot condition = new(false, false, 0, 0, true, 1, 50, false, 1, 50, false, 1, "{}");
    string list = "[{\"code\":331,\"indent\":0,\"parameters\":[1,1]},{\"code\":339,\"indent\":0,\"parameters\":[0,2,1,0]},{\"code\":0,\"indent\":0,\"parameters\":[]}]";
    MvTroopRecordSnapshot troop = new(1, "Test", [Member(0,1,1,1), Member(1,1,2,2), Member(2,1,3,3)], [new MvTroopPageSnapshot(0, condition, 0, 3, list, "{}")], new Dictionary<string,string>());
    TroopMemberReferenceReport report = TroopMemberReferenceAnalyzer.Analyze(troop);
    True(report.References.Any(item => item.MemberIndex == 1 && item.CommandIndex is null));
    True(report.References.Any(item => item.MemberIndex == 1 && item.CommandIndex == 0));
    True(report.References.Any(item => item.MemberIndex == 2 && item.CommandIndex == 1));
    True(TroopMemberReferenceAnalyzer.AnalyzeRemoval(troop, 1).HasReferences);
}

static void Difficulty()
{
    MvEnemyRecordSnapshot enemy = new(1, "Boss", "Boss", 0, [1000,100,80,70,90,60,50,40], 500, 200, [], [Action(1,8)], 0, "", new Dictionary<string,string>());
    MvTroopRecordSnapshot troop = new(1, "Boss", [Member(0,1,400,350)], [], new Dictionary<string,string>());
    TroopDifficultyEstimate first = TroopDifficultyEstimator.EstimateTroop(troop, new Dictionary<int,MvEnemyRecordSnapshot>{{1,enemy}});
    TroopDifficultyEstimate second = TroopDifficultyEstimator.EstimateTroop(troop, new Dictionary<int,MvEnemyRecordSnapshot>{{1,enemy}});
    True(first.CompositeThreat > 0);
    Near(first.CompositeThreat, second.CompositeThreat);
    True(first.Assumptions.Contains("Approximation", StringComparison.Ordinal));
}

static void MultiWave()
{
    MvTroopMemberSnapshot[] members = [Member(0,1,200,350), Member(1,2,400,350), Member(2,3,600,350)];
    MultiWaveCompilationResult result = MultiWaveCompiler.Compile(members,
        [new TroopWaveDefinition("Opening", [0]), new TroopWaveDefinition("Reinforcements", [1,2])]);
    False(result.Members[0].Hidden);
    True(result.Members[1].Hidden);
    Equal(1, result.GeneratedPages.Count);
    using JsonDocument commands = JsonDocument.Parse(result.GeneratedPages[0].CommandListRawJson);
    Equal(111, commands.RootElement[0].GetProperty("code").GetInt32());
    True(commands.RootElement.EnumerateArray().Any(command => command.GetProperty("code").GetInt32() == 335));
    Equal(0, commands.RootElement[commands.RootElement.GetArrayLength()-1].GetProperty("code").GetInt32());
}

static void BattleTestProfileRoundTrip()
{
    string systemJson = "{\"testTroopId\":3,\"battleback1Name\":\"Grassland\",\"battleback2Name\":\"Forest\",\"testBattlers\":[{\"actorId\":2,\"level\":10,\"equips\":[1,2,0,0,0],\"plugin\":true}]}";
    BattleTestProfile profile = BattleTestProfileReader.Read(Create("data/System.json", MvDocumentKind.System, systemJson));
    Equal(3, profile.TroopId);
    Equal(2, profile.Battlers[0].ActorId);
    string raw = Phase9CMutationService.SerializeBattleTestBattlers(profile.Battlers);
    using JsonDocument document = JsonDocument.Parse(raw);
    True(document.RootElement[0].GetProperty("plugin").GetBoolean());
}

static void RawPreservation()
{
    string actionJson = Phase9CMutationService.SerializeActions([new MvEnemyActionSnapshot(3, 0, 0, 0, 6, "{\"skillId\":1,\"conditionType\":0,\"conditionParam1\":0,\"conditionParam2\":0,\"rating\":5,\"plugin\":true}")]);
    using JsonDocument action = JsonDocument.Parse(actionJson);
    True(action.RootElement[0].GetProperty("plugin").GetBoolean());
    string memberJson = Phase9CMutationService.SerializeMembers([new MvTroopMemberSnapshot(0, 4, 10, 20, false, "{\"enemyId\":1,\"x\":1,\"y\":2,\"hidden\":false,\"plugin\":7}")]);
    using JsonDocument member = JsonDocument.Parse(memberJson);
    Equal(7, member.RootElement[0].GetProperty("plugin").GetInt32());
}

static MvEnemyActionSnapshot Action(int skill, int rating, int condition = 0, double first = 0, double second = 0) =>
    new(skill, condition, first, second, rating, "{}");
static MvTroopMemberSnapshot Member(int index, int enemy, int x, int y) => new(index, enemy, x, y, false, "{}");

static MutableJsonProjectDocument Create(string path, MvDocumentKind kind, string json)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    return MutableJsonProjectDocument.Create(path, kind, bytes, new ContentFingerprint(bytes.LongLength, DateTimeOffset.UnixEpoch, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
}

static void True(bool value, string message = "Assertion was false.") { if (!value) throw new InvalidOperationException(message); }
static void False(bool value, string message = "Assertion was true.") => True(!value, message);
static void Near(double expected, double actual, double tolerance = 0.000001) { if (Math.Abs(expected - actual) > tolerance) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
static void Equal<T>(T expected, T actual)
{
    if (expected is Array expectedArray && actual is Array actualArray)
    {
        if (!expectedArray.Cast<object?>().SequenceEqual(actualArray.Cast<object?>())) throw new InvalidOperationException("Arrays were not equal.");
        return;
    }
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

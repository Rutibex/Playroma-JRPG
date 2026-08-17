using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.FormulaSandbox;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Func<Task> Test)> tests =
[
    ("System type catalog resolves project-defined names and unknown IDs", SystemCatalogAsync),
    ("Skill reader loads every Phase 9B field and preserves nested raw JSON", SkillReaderAsync),
    ("Item reader loads shared usable-item and item-specific fields", ItemReaderAsync),
    ("Equipment reader preserves eight parameters and project type IDs", EquipmentReaderAsync),
    ("State reader preserves nonstandard extension fields", StateExtensionsAsync),
    ("Phase 9B templates contain standard MV fields", TemplatesAsync),
    ("Formula, effects, traits, descriptions, and messages are searchable", ExpandedSearchAsync),
    ("Formula evaluator is deterministic and reports references", FormulaEvaluatorAsync),
    ("Formula evaluator fails safely on unsupported JavaScript", FormulaFailureAsync),
    ("A 1,250-record skill index remains queryable", LargeIndexAsync)
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
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 9B tests passed.");
return failures == 0 ? 0 : 1;

static Task SystemCatalogAsync()
{
    MutableJsonProjectDocument system = Create("data/System.json", MvDocumentKind.System,
        "{\"elements\":[\"\",\"Fire\",\"Ice\"],\"skillTypes\":[\"\",\"Magic\"],\"weaponTypes\":[\"\",\"Sword\"],\"armorTypes\":[\"\",\"Light\"],\"equipTypes\":[\"\",\"Weapon\",\"Relic\"]}");
    MvSystemTypeCatalog catalog = MvSystemTypeCatalog.Read(system);
    Equal("Fire", catalog.Elements[1].Name);
    Equal("Relic", catalog.EquipmentTypes[2].Name);
    True(catalog.Resolve(Playroma.JRPG.Indexing.ReferenceTargetKind.WeaponType, 99).Contains("Unknown", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task SkillReaderAsync()
{
    string json = "[null,{\"id\":1,\"animationId\":7,\"damage\":{\"critical\":true,\"elementId\":2,\"formula\":\"a.atk*4-b.def*2\",\"type\":1,\"variance\":15,\"pluginDamage\":42},\"description\":\"Flame\",\"effects\":[{\"code\":21,\"dataId\":4,\"value1\":0.5,\"value2\":0,\"pluginEffect\":true}],\"hitType\":2,\"iconIndex\":12,\"message1\":\" casts %1!\",\"message2\":\"\",\"mpCost\":9,\"name\":\"Fire\",\"note\":\"<Tag>\",\"occasion\":0,\"repeats\":2,\"requiredWtypeId1\":3,\"requiredWtypeId2\":0,\"scope\":1,\"speed\":5,\"stypeId\":2,\"successRate\":95,\"tpCost\":1,\"tpGain\":4}]";
    MvSkillRecordSnapshot skill = Phase9BRecordReader.ReadSkill(Create("data/Skills.json", MvDocumentKind.Skills, json), 1);
    Equal("Fire", skill.Common.Name);
    Equal(2, skill.SkillTypeId);
    Equal(9, skill.MpCost);
    Equal(3, skill.RequiredWeaponType1);
    Equal("a.atk*4-b.def*2", skill.Common.Damage.Formula);
    True(skill.Common.Damage.RawJson.Contains("pluginDamage", StringComparison.Ordinal));
    True(skill.Common.Effects[0].RawJson.Contains("pluginEffect", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task ItemReaderAsync()
{
    string json = "[null,{\"id\":1,\"animationId\":0,\"consumable\":false,\"damage\":{\"critical\":false,\"elementId\":0,\"formula\":\"0\",\"type\":0,\"variance\":20},\"description\":\"Key\",\"effects\":[],\"hitType\":0,\"iconIndex\":1,\"itypeId\":2,\"name\":\"Pass\",\"note\":\"\",\"occasion\":3,\"price\":50,\"repeats\":1,\"scope\":0,\"speed\":0,\"successRate\":100,\"tpGain\":0}]";
    MvItemRecordSnapshot item = Phase9BRecordReader.ReadItem(Create("data/Items.json", MvDocumentKind.Items, json), 1);
    Equal(2, item.ItemTypeId);
    Equal(50, item.Price);
    False(item.Consumable);
    Equal(3, item.Common.Occasion);
    return Task.CompletedTask;
}

static Task EquipmentReaderAsync()
{
    string json = "[null,{\"id\":1,\"animationId\":4,\"description\":\"Blade\",\"etypeId\":1,\"traits\":[],\"iconIndex\":3,\"name\":\"Sword\",\"note\":\"\",\"params\":[1,2,3,4,5,6,7,8],\"price\":100,\"wtypeId\":9}]";
    MvEquipmentRecordSnapshot weapon = Phase9BRecordReader.ReadEquipment(Create("data/Weapons.json", MvDocumentKind.Weapons, json), 1);
    Equal(8, weapon.Parameters.Count);
    Equal(8, weapon.Parameters[7]);
    Equal(9, weapon.TypeId);
    Equal(4, weapon.AnimationId);
    return Task.CompletedTask;
}

static Task StateExtensionsAsync()
{
    string json = "[null,{\"id\":1,\"autoRemovalTiming\":0,\"chanceByDamage\":100,\"iconIndex\":0,\"maxTurns\":1,\"message1\":\"\",\"message2\":\"\",\"message3\":\"\",\"message4\":\"\",\"minTurns\":1,\"motion\":0,\"name\":\"State\",\"note\":\"\",\"overlay\":0,\"priority\":50,\"removeAtBattleEnd\":false,\"removeByDamage\":false,\"removeByRestriction\":false,\"removeByWalking\":false,\"restriction\":0,\"stepsToRemove\":100,\"traits\":[],\"releaseByDamage\":false,\"description\":\"preserve\"}]";
    MvStateRecordSnapshot state = Phase9BRecordReader.ReadState(Create("data/States.json", MvDocumentKind.States, json), 1);
    Equal("false", state.ExtensionFields["releaseByDamage"]);
    Equal("\"preserve\"", state.ExtensionFields["description"]);
    return Task.CompletedTask;
}

static Task TemplatesAsync()
{
    foreach (MvDocumentKind kind in new[] { MvDocumentKind.Skills, MvDocumentKind.Items, MvDocumentKind.Weapons, MvDocumentKind.Armors, MvDocumentKind.States })
    {
        using JsonDocument json = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(kind, 1));
        MvRecordContract contract = MvRecordContractCatalog.Get(kind);
        foreach (JsonProperty property in json.RootElement.EnumerateObject()) True(contract.IsStandard(property.Name), $"{kind}.{property.Name} was not standard.");
    }
    using JsonDocument state = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.States, 1));
    False(state.RootElement.TryGetProperty("releaseByDamage", out _));
    return Task.CompletedTask;
}

static Task ExpandedSearchAsync()
{
    MutableJsonProjectDocument skills = Create("data/Skills.json", MvDocumentKind.Skills,
        "[null,{\"id\":1,\"name\":\"Bolt\",\"description\":\"Storm spell\",\"message1\":\" channels thunder\",\"message2\":\"\",\"damage\":{\"formula\":\"a.mat*7\"},\"effects\":[{\"code\":21,\"dataId\":4}],\"traits\":[],\"note\":\"<Lightning>\"}]");
    DatabaseRecordIndex index = new(skills);
    Equal(1, index.Query(new DatabaseRecordFilter(Text: "a.mat*7")).Single().Id);
    Equal(1, index.Query(new DatabaseRecordFilter(Text: "channels thunder")).Single().Id);
    Equal(1, index.Query(new DatabaseRecordFilter(Text: "Lightning")).Single().Id);
    return Task.CompletedTask;
}

static Task FormulaEvaluatorAsync()
{
    FormulaSandboxRequest request = new(
        "a.atk * 4 - b.def * 2 + v[31]",
        Battler("A", atk: 50, def: 20),
        Battler("B", atk: 30, def: 40),
        new FormulaItemSnapshot(1, "Attack", 1, -1, 20, true),
        new Dictionary<int, double> { [31] = 10 },
        ApplyCritical: true,
        SampleCount: 50,
        Seed: 42);
    FormulaSandboxResult first = FormulaSandboxEvaluator.Evaluate(request);
    FormulaSandboxResult second = FormulaSandboxEvaluator.Evaluate(request);
    True(first.Success);
    Equal(130d, first.RawResult);
    Equal(first.Samples[0], second.Samples[0]);
    True(first.ReferencedProperties.Contains("a.atk"));
    True(first.ReferencedVariables.Contains(31));
    return Task.CompletedTask;
}

static Task FormulaFailureAsync()
{
    FormulaSandboxResult result = FormulaSandboxEvaluator.Evaluate(new FormulaSandboxRequest(
        "require('fs').readFileSync('x')",
        Battler("A", 10, 10),
        Battler("B", 10, 10),
        new FormulaItemSnapshot(1, "Bad", 1, 0, 0, false)));
    False(result.Success);
    True(!string.IsNullOrWhiteSpace(result.Error));
    return Task.CompletedTask;
}

static Task LargeIndexAsync()
{
    StringBuilder json = new("[null");
    for (int id = 1; id <= 1250; id++) json.Append(",{\"id\":").Append(id).Append(",\"name\":\"Skill ").Append(id).Append("\",\"description\":\"\",\"note\":\"\"}");
    json.Append(']');
    DatabaseRecordIndex index = new(Create("data/Skills.json", MvDocumentKind.Skills, json.ToString()));
    Equal(1250, index.Records.Count);
    Equal(1250, index.Query(new DatabaseRecordFilter(Text: "Skill 1250")).Single().Id);
    return Task.CompletedTask;
}

static FormulaBattlerSnapshot Battler(string name, double atk, double def) => new(name, 1, 100, 100, 20, 20, 0, atk, def, 10, 10, 10, 10);

static MutableJsonProjectDocument Create(string path, MvDocumentKind kind, string json)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    return MutableJsonProjectDocument.Create(path, kind, bytes, new ContentFingerprint(bytes.LongLength, DateTimeOffset.UnixEpoch, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
}

static void True(bool condition, string message = "Assertion was false.") { if (!condition) throw new InvalidOperationException(message); }
static void False(bool condition, string message = "Assertion was true.") => True(!condition, message);
static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

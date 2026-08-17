using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public enum EnemyActionConditionType
{
    Always = 0,
    Turn = 1,
    Hp = 2,
    Mp = 3,
    State = 4,
    PartyLevel = 5,
    Switch = 6
}

public enum EnemyDropKind
{
    None = 0,
    Item = 1,
    Weapon = 2,
    Armor = 3
}

public enum TroopPageSpan
{
    Battle = 0,
    Turn = 1,
    Moment = 2
}

public sealed record MvEnemyDropSnapshot(
    int Kind,
    int DataId,
    int Denominator,
    string RawJson)
{
    public double BaseProbability => Kind == 0 || DataId <= 0 || Denominator <= 0 ? 0d : Math.Min(1d, 1d / Denominator);
}

public sealed record MvEnemyActionSnapshot(
    int SkillId,
    int ConditionType,
    double ConditionParam1,
    double ConditionParam2,
    int Rating,
    string RawJson);

public sealed record MvEnemyRecordSnapshot(
    int Id,
    string Name,
    string BattlerName,
    int BattlerHue,
    IReadOnlyList<int> Parameters,
    int Experience,
    int Gold,
    IReadOnlyList<MvEnemyDropSnapshot> Drops,
    IReadOnlyList<MvEnemyActionSnapshot> Actions,
    int TraitCount,
    string Note,
    IReadOnlyDictionary<string, string> ExtensionFields);

public sealed record MvTroopMemberSnapshot(
    int Index,
    int EnemyId,
    int X,
    int Y,
    bool Hidden,
    string RawJson);

public sealed record MvTroopPageConditionSnapshot(
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
    string RawJson)
{
    public bool HasAnyCondition => TurnEnding || TurnValid || EnemyValid || ActorValid || SwitchValid;
}

public sealed record MvTroopPageSnapshot(
    int Index,
    MvTroopPageConditionSnapshot Conditions,
    int Span,
    int CommandCount,
    string CommandListRawJson,
    string RawJson);

public sealed record MvTroopRecordSnapshot(
    int Id,
    string Name,
    IReadOnlyList<MvTroopMemberSnapshot> Members,
    IReadOnlyList<MvTroopPageSnapshot> Pages,
    IReadOnlyDictionary<string, string> ExtensionFields);

public static class Phase9CRecordReader
{
    public static MvEnemyRecordSnapshot ReadEnemy(MutableJsonProjectDocument document, int recordId)
    {
        RequireKind(document, MvDocumentKind.Enemies);
        JsonPath root = JsonPath.Root.Append(ValidateId(recordId));
        EnsureObject(document, root, "enemy");
        return new MvEnemyRecordSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetString(root.Append("battlerName")) ?? string.Empty,
            document.GetInt32(root.Append("battlerHue")) ?? 0,
            ReadIntArray(document, root.Append("params")),
            document.GetInt32(root.Append("exp")) ?? 0,
            document.GetInt32(root.Append("gold")) ?? 0,
            ReadDrops(document, root.Append("dropItems")),
            ReadActions(document, root.Append("actions")),
            CountArray(document, root.Append("traits")),
            document.GetString(root.Append("note")) ?? string.Empty,
            ReadExtensions(document, root, MvDocumentKind.Enemies));
    }

    public static MvTroopRecordSnapshot ReadTroop(MutableJsonProjectDocument document, int recordId)
    {
        RequireKind(document, MvDocumentKind.Troops);
        JsonPath root = JsonPath.Root.Append(ValidateId(recordId));
        EnsureObject(document, root, "troop");
        return new MvTroopRecordSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            ReadMembers(document, root.Append("members")),
            ReadPages(document, root.Append("pages")),
            ReadExtensions(document, root, MvDocumentKind.Troops));
    }

    public static IReadOnlyList<MvEnemyActionSnapshot> ReadActions(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvEnemyActionSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath item = path.Append(index);
            if (document.Syntax.Resolve(item) is not JsonObjectSyntax) continue;
            result.Add(new MvEnemyActionSnapshot(
                document.GetInt32(item.Append("skillId")) ?? 1,
                document.GetInt32(item.Append("conditionType")) ?? 0,
                document.GetDouble(item.Append("conditionParam1")) ?? 0,
                document.GetDouble(item.Append("conditionParam2")) ?? 0,
                document.GetInt32(item.Append("rating")) ?? 5,
                document.GetRawJson(item) ?? "{}"));
        }
        return result;
    }

    public static IReadOnlyList<MvEnemyDropSnapshot> ReadDrops(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvEnemyDropSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath item = path.Append(index);
            if (document.Syntax.Resolve(item) is not JsonObjectSyntax) continue;
            result.Add(new MvEnemyDropSnapshot(
                document.GetInt32(item.Append("kind")) ?? 0,
                document.GetInt32(item.Append("dataId")) ?? 1,
                document.GetInt32(item.Append("denominator")) ?? 1,
                document.GetRawJson(item) ?? "{}"));
        }
        return result;
    }

    public static IReadOnlyList<MvTroopMemberSnapshot> ReadMembers(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvTroopMemberSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath item = path.Append(index);
            if (document.Syntax.Resolve(item) is not JsonObjectSyntax) continue;
            result.Add(new MvTroopMemberSnapshot(
                index,
                document.GetInt32(item.Append("enemyId")) ?? 1,
                document.GetInt32(item.Append("x")) ?? 0,
                document.GetInt32(item.Append("y")) ?? 0,
                document.GetBoolean(item.Append("hidden")) ?? false,
                document.GetRawJson(item) ?? "{}"));
        }
        return result;
    }

    public static IReadOnlyList<MvTroopPageSnapshot> ReadPages(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        List<MvTroopPageSnapshot> result = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            JsonPath page = path.Append(index);
            if (document.Syntax.Resolve(page) is not JsonObjectSyntax) continue;
            JsonPath conditions = page.Append("conditions");
            JsonPath list = page.Append("list");
            MvTroopPageConditionSnapshot condition = new(
                document.GetBoolean(conditions.Append("turnEnding")) ?? false,
                document.GetBoolean(conditions.Append("turnValid")) ?? false,
                document.GetInt32(conditions.Append("turnA")) ?? 0,
                document.GetInt32(conditions.Append("turnB")) ?? 0,
                document.GetBoolean(conditions.Append("enemyValid")) ?? false,
                document.GetInt32(conditions.Append("enemyIndex")) ?? 0,
                document.GetInt32(conditions.Append("enemyHp")) ?? 50,
                document.GetBoolean(conditions.Append("actorValid")) ?? false,
                document.GetInt32(conditions.Append("actorId")) ?? 1,
                document.GetInt32(conditions.Append("actorHp")) ?? 50,
                document.GetBoolean(conditions.Append("switchValid")) ?? false,
                document.GetInt32(conditions.Append("switchId")) ?? 1,
                document.GetRawJson(conditions) ?? "{}");
            result.Add(new MvTroopPageSnapshot(
                index,
                condition,
                document.GetInt32(page.Append("span")) ?? 0,
                CountArray(document, list),
                document.GetRawJson(list) ?? "[]",
                document.GetRawJson(page) ?? "{}"));
        }
        return result;
    }

    public static IReadOnlyList<int> ReadIntArray(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax array) return [];
        int[] result = new int[array.Elements.Count];
        for (int index = 0; index < result.Length; index++) result[index] = document.GetInt32(path.Append(index)) ?? 0;
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadExtensions(
        MutableJsonProjectDocument document,
        JsonPath path,
        MvDocumentKind kind)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        MvRecordContract contract = MvRecordContractCatalog.Get(kind);
        if (document.Syntax.Resolve(path) is not JsonObjectSyntax record) return result;
        foreach (JsonPropertySyntax property in record.Properties)
        {
            if (!contract.IsStandard(property.Name))
            {
                result[property.Name] = document.GetRawJson(path.Append(property.Name)) ?? "null";
            }
        }
        return result;
    }

    private static int CountArray(MutableJsonProjectDocument document, JsonPath path) =>
        (document.Syntax.Resolve(path) as JsonArraySyntax)?.Elements.Count ?? 0;

    private static void RequireKind(MutableJsonProjectDocument document, MvDocumentKind expected)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Kind != expected) throw new ArgumentException($"A {expected} document is required.", nameof(document));
    }

    private static void EnsureObject(MutableJsonProjectDocument document, JsonPath path, string label)
    {
        if (document.Syntax.Resolve(path) is not JsonObjectSyntax) throw new InvalidDataException($"The selected {label} record is empty or invalid.");
    }

    private static int ValidateId(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
}

public sealed record EnemyActionScenario(
    int Turn,
    double HpRatio,
    double MpRatio,
    IReadOnlySet<int> ActiveStates,
    int HighestPartyLevel,
    IReadOnlyDictionary<int, bool> Switches,
    int CurrentMp,
    IReadOnlyDictionary<int, int>? SkillMpCosts = null);

public sealed record EnemyActionEvaluation(
    int Index,
    MvEnemyActionSnapshot Action,
    bool ConditionMet,
    bool Affordable,
    bool IncludedByRating,
    int Weight,
    double Probability,
    string Explanation);

public static class EnemyActionRuleEvaluator
{
    public static IReadOnlyList<EnemyActionEvaluation> Evaluate(
        IReadOnlyList<MvEnemyActionSnapshot> actions,
        EnemyActionScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(scenario);
        List<(int Index, MvEnemyActionSnapshot Action, bool Condition, bool Affordable, string Explanation)> preliminary = [];
        for (int index = 0; index < actions.Count; index++)
        {
            MvEnemyActionSnapshot action = actions[index];
            (bool met, string explanation) = CheckCondition(action, scenario);
            int mpCost = scenario.SkillMpCosts is not null && scenario.SkillMpCosts.TryGetValue(action.SkillId, out int value) ? value : 0;
            bool affordable = mpCost <= scenario.CurrentMp;
            if (!affordable) explanation += $" Skill needs {mpCost} MP but only {scenario.CurrentMp} MP is available.";
            preliminary.Add((index, action, met, affordable, explanation));
        }

        MvEnemyActionSnapshot[] eligible = preliminary.Where(item => item.Condition && item.Affordable).Select(item => item.Action).ToArray();
        int highest = eligible.Length == 0 ? 0 : eligible.Max(item => item.Rating);
        int cutoff = highest - 3;
        int totalWeight = preliminary
            .Where(item => item.Condition && item.Affordable && item.Action.Rating > cutoff)
            .Sum(item => item.Action.Rating - cutoff);

        return preliminary.Select(item =>
        {
            bool included = item.Condition && item.Affordable && item.Action.Rating > cutoff;
            int weight = included ? item.Action.Rating - cutoff : 0;
            double probability = totalWeight <= 0 ? 0d : weight / (double)totalWeight;
            string explanation = item.Explanation;
            if (item.Condition && item.Affordable && !included) explanation += $" Excluded because its rating is more than two below the current maximum ({highest}).";
            return new EnemyActionEvaluation(item.Index, item.Action, item.Condition, item.Affordable, included, weight, probability, explanation);
        }).ToArray();
    }

    public static (bool IsMet, string Explanation) CheckCondition(
        MvEnemyActionSnapshot action,
        EnemyActionScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scenario);
        if (!Enum.IsDefined(typeof(EnemyActionConditionType), action.ConditionType))
        {
            return (false, $"Unknown condition type {action.ConditionType}.");
        }

        EnemyActionConditionType type = (EnemyActionConditionType)action.ConditionType;
        return type switch
        {
            EnemyActionConditionType.Always => (true, "Always available."),
            EnemyActionConditionType.Turn => CheckTurn(action, scenario),
            EnemyActionConditionType.Hp => CheckRange("HP", scenario.HpRatio, action.ConditionParam1, action.ConditionParam2),
            EnemyActionConditionType.Mp => CheckRange("MP", scenario.MpRatio, action.ConditionParam1, action.ConditionParam2),
            EnemyActionConditionType.State => CheckState(action, scenario),
            EnemyActionConditionType.PartyLevel => CheckPartyLevel(action, scenario),
            EnemyActionConditionType.Switch => CheckSwitch(action, scenario),
            _ => (false, $"Unknown condition type {action.ConditionType}.")
        };
    }

    private static (bool, string) CheckTurn(MvEnemyActionSnapshot action, EnemyActionScenario scenario)
    {
        int first = Math.Max(0, (int)Math.Round(action.ConditionParam1));
        int interval = Math.Max(0, (int)Math.Round(action.ConditionParam2));
        bool met = scenario.Turn == first || interval > 0 && scenario.Turn > first && (scenario.Turn - first) % interval == 0;
        return (met, interval == 0 ? $"Turn must equal {first}." : $"Turn must be {first} + {interval}×X.");
    }

    private static (bool, string) CheckRange(string name, double value, double minimum, double maximum)
    {
        bool met = value >= minimum && value <= maximum;
        return (met, $"{name} {value:P0}; required {minimum:P0}–{maximum:P0}.");
    }

    private static (bool, string) CheckState(MvEnemyActionSnapshot action, EnemyActionScenario scenario)
    {
        int stateId = (int)Math.Round(action.ConditionParam1);
        bool met = scenario.ActiveStates.Contains(stateId);
        return (met, met ? $"State #{stateId} is active." : $"State #{stateId} is not active.");
    }

    private static (bool, string) CheckPartyLevel(MvEnemyActionSnapshot action, EnemyActionScenario scenario)
    {
        int required = (int)Math.Round(action.ConditionParam1);
        bool met = scenario.HighestPartyLevel >= required;
        return (met, $"Highest party level {scenario.HighestPartyLevel}; requires at least {required}.");
    }

    private static (bool, string) CheckSwitch(MvEnemyActionSnapshot action, EnemyActionScenario scenario)
    {
        int switchId = (int)Math.Round(action.ConditionParam1);
        bool met = scenario.Switches.TryGetValue(switchId, out bool enabled) && enabled;
        return (met, met ? $"Switch #{switchId} is ON." : $"Switch #{switchId} is OFF.");
    }
}

public sealed record DropExpectation(
    MvEnemyDropSnapshot Drop,
    double Probability,
    double ExpectedPerBattle,
    double ProbabilityAtLeastOne);

public sealed record EnemyRewardSummary(
    int Experience,
    int Gold,
    IReadOnlyList<DropExpectation> Drops);

public sealed record TroopRewardSummary(
    int Experience,
    int Gold,
    IReadOnlyDictionary<(int Kind, int DataId), double> ExpectedDrops);

public static class EnemyRewardCalculator
{
    public static EnemyRewardSummary CalculateEnemy(MvEnemyRecordSnapshot enemy, int battles = 1, bool doubleDrops = false)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        battles = Math.Max(1, battles);
        double multiplier = doubleDrops ? 2d : 1d;
        DropExpectation[] drops = enemy.Drops.Where(drop => drop.Kind > 0 && drop.DataId > 0 && drop.Denominator > 0).Select(drop =>
        {
            double probability = Math.Min(1d, multiplier / drop.Denominator);
            return new DropExpectation(drop, probability, battles * probability, 1d - Math.Pow(1d - probability, battles));
        }).ToArray();
        return new EnemyRewardSummary(enemy.Experience, enemy.Gold, drops);
    }

    public static TroopRewardSummary CalculateTroop(
        MvTroopRecordSnapshot troop,
        IReadOnlyDictionary<int, MvEnemyRecordSnapshot> enemies,
        bool doubleDrops = false,
        bool doubleGold = false)
    {
        ArgumentNullException.ThrowIfNull(troop);
        ArgumentNullException.ThrowIfNull(enemies);
        int experience = 0;
        int gold = 0;
        Dictionary<(int Kind, int DataId), double> drops = [];
        foreach (MvTroopMemberSnapshot member in troop.Members)
        {
            if (!enemies.TryGetValue(member.EnemyId, out MvEnemyRecordSnapshot? enemy)) continue;
            experience += Math.Max(0, enemy.Experience);
            gold += Math.Max(0, enemy.Gold);
            foreach (DropExpectation drop in CalculateEnemy(enemy, 1, doubleDrops).Drops)
            {
                (int Kind, int DataId) key = (drop.Drop.Kind, drop.Drop.DataId);
                drops[key] = drops.GetValueOrDefault(key) + drop.ExpectedPerBattle;
            }
        }
        if (doubleGold) gold *= 2;
        return new TroopRewardSummary(experience, gold, drops);
    }

    public static IReadOnlyDictionary<(int Kind, int DataId), int> SimulateDrops(
        IEnumerable<MvEnemyDropSnapshot> drops,
        int battles,
        int seed,
        bool doubleDrops = false)
    {
        ArgumentNullException.ThrowIfNull(drops);
        battles = Math.Clamp(battles, 1, 10_000_000);
        Random random = new(seed);
        Dictionary<(int Kind, int DataId), int> result = [];
        MvEnemyDropSnapshot[] active = drops.Where(drop => drop.Kind > 0 && drop.DataId > 0 && drop.Denominator > 0).ToArray();
        double multiplier = doubleDrops ? 2d : 1d;
        for (int battle = 0; battle < battles; battle++)
        {
            foreach (MvEnemyDropSnapshot drop in active)
            {
                if (random.NextDouble() * drop.Denominator >= multiplier) continue;
                (int Kind, int DataId) key = (drop.Kind, drop.DataId);
                result[key] = result.GetValueOrDefault(key) + 1;
            }
        }
        return result;
    }
}

public enum TroopFormationPreset
{
    HorizontalLine,
    TwoRows,
    Columns,
    Arc,
    Symmetrical,
    Random
}

public sealed record BattleCanvasProfile(int LogicalWidth, int LogicalHeight, string Source)
{
    public static BattleCanvasProfile MvDefault { get; } = new(816, 624, "RPG Maker MV default");
}

public static class TroopFormationPlanner
{
    public static IReadOnlyList<MvTroopMemberSnapshot> Plan(
        IReadOnlyList<MvTroopMemberSnapshot> members,
        TroopFormationPreset preset,
        BattleCanvasProfile? profile = null,
        int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(members);
        profile ??= BattleCanvasProfile.MvDefault;
        if (members.Count == 0) return [];
        return preset switch
        {
            TroopFormationPreset.HorizontalLine => Horizontal(members, profile),
            TroopFormationPreset.TwoRows => Rows(members, profile, 2),
            TroopFormationPreset.Columns => Columns(members, profile),
            TroopFormationPreset.Arc => Arc(members, profile),
            TroopFormationPreset.Symmetrical => Symmetrical(members, profile),
            TroopFormationPreset.Random => Randomize(members, profile, seed),
            _ => members.ToArray()
        };
    }

    public static IReadOnlyList<MvTroopMemberSnapshot> Move(
        IReadOnlyList<MvTroopMemberSnapshot> members,
        IReadOnlySet<int> selectedIndexes,
        int deltaX,
        int deltaY,
        BattleCanvasProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(selectedIndexes);
        profile ??= BattleCanvasProfile.MvDefault;
        return members.Select(member => selectedIndexes.Contains(member.Index)
            ? member with
            {
                X = Math.Clamp(member.X + deltaX, 0, profile.LogicalWidth),
                Y = Math.Clamp(member.Y + deltaY, 0, profile.LogicalHeight)
            }
            : member).ToArray();
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Horizontal(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile)
    {
        int spacing = Math.Max(56, Math.Min(150, (profile.LogicalWidth - 160) / Math.Max(1, members.Count - 1)));
        int start = profile.LogicalWidth / 2 - spacing * (members.Count - 1) / 2;
        int y = (int)(profile.LogicalHeight * 0.62);
        return members.Select((member, index) => member with { X = start + index * spacing, Y = y }).ToArray();
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Rows(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile, int rows)
    {
        int columns = (int)Math.Ceiling(members.Count / (double)rows);
        int xSpacing = Math.Max(72, (profile.LogicalWidth - 220) / Math.Max(1, columns - 1));
        int ySpacing = 110;
        int startX = profile.LogicalWidth / 2 - xSpacing * (columns - 1) / 2;
        int startY = (int)(profile.LogicalHeight * 0.54);
        return members.Select((member, index) => member with
        {
            X = startX + (index % columns) * xSpacing,
            Y = startY + (index / columns) * ySpacing
        }).ToArray();
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Columns(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile)
    {
        int rows = Math.Min(4, members.Count);
        return Rows(members, profile, rows);
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Arc(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile)
    {
        double centerX = profile.LogicalWidth / 2d;
        double centerY = profile.LogicalHeight * 0.9;
        double radiusX = Math.Min(300d, profile.LogicalWidth * 0.38);
        double radiusY = Math.Min(210d, profile.LogicalHeight * 0.34);
        return members.Select((member, index) =>
        {
            double t = members.Count == 1 ? 0.5 : index / (double)(members.Count - 1);
            double angle = Math.PI * (0.12 + t * 0.76);
            int x = (int)Math.Round(centerX - Math.Cos(angle) * radiusX);
            int y = (int)Math.Round(centerY - Math.Sin(angle) * radiusY);
            return member with { X = x, Y = y };
        }).ToArray();
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Symmetrical(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile)
    {
        IReadOnlyList<MvTroopMemberSnapshot> line = Horizontal(members, profile);
        int center = profile.LogicalWidth / 2;
        return line.Select((member, index) => member with
        {
            X = index % 2 == 0 ? center - Math.Abs(member.X - center) : center + Math.Abs(member.X - center),
            Y = member.Y + (index % 2) * 45
        }).ToArray();
    }

    private static IReadOnlyList<MvTroopMemberSnapshot> Randomize(IReadOnlyList<MvTroopMemberSnapshot> members, BattleCanvasProfile profile, int seed)
    {
        Random random = new(seed);
        List<Point> occupied = [];
        List<MvTroopMemberSnapshot> result = [];
        foreach (MvTroopMemberSnapshot member in members)
        {
            Point point = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                point = new Point(random.Next(90, Math.Max(91, profile.LogicalWidth - 70)), random.Next(230, Math.Max(231, profile.LogicalHeight - 45)));
                if (occupied.All(other => DistanceSquared(point, other) >= 70 * 70)) break;
            }
            occupied.Add(point);
            result.Add(member with { X = point.X, Y = point.Y });
        }
        return result;
    }

    private static int DistanceSquared(Point first, Point second)
    {
        int dx = first.X - second.X;
        int dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private readonly record struct Point(int X, int Y);
}

public sealed record TroopMemberReference(int PageIndex, int? CommandIndex, int MemberIndex, string JsonPath, string Description);

public sealed record TroopMemberReferenceReport(IReadOnlyList<TroopMemberReference> References)
{
    public bool HasReferences => References.Count > 0;
}

public static class TroopMemberReferenceAnalyzer
{
    private static readonly HashSet<int> DirectEnemyTargetCodes = [331, 332, 333, 334, 335, 336, 337];

    public static TroopMemberReferenceReport Analyze(MvTroopRecordSnapshot troop)
    {
        ArgumentNullException.ThrowIfNull(troop);
        List<TroopMemberReference> result = [];
        foreach (MvTroopPageSnapshot page in troop.Pages)
        {
            if (page.Conditions.EnemyValid)
            {
                result.Add(new TroopMemberReference(
                    page.Index,
                    null,
                    page.Conditions.EnemyIndex,
                    $"/{troop.Id}/pages/{page.Index}/conditions/enemyIndex",
                    "Troop-page enemy HP condition"));
            }
            ScanCommands(troop.Id, page, result);
        }
        return new TroopMemberReferenceReport(result);
    }

    public static TroopMemberReferenceReport AnalyzeRemoval(MvTroopRecordSnapshot troop, int removedIndex)
    {
        ArgumentNullException.ThrowIfNull(troop);
        TroopMemberReference[] affected = Analyze(troop).References.Where(reference => reference.MemberIndex >= removedIndex).ToArray();
        return new TroopMemberReferenceReport(affected);
    }

    private static void ScanCommands(int troopId, MvTroopPageSnapshot page, ICollection<TroopMemberReference> result)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(page.CommandListRawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return;
            int commandIndex = 0;
            foreach (JsonElement command in document.RootElement.EnumerateArray())
            {
                if (command.ValueKind != JsonValueKind.Object) { commandIndex++; continue; }
                int code = command.TryGetProperty("code", out JsonElement codeElement) && codeElement.TryGetInt32(out int parsed) ? parsed : 0;
                if (!command.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Array)
                {
                    commandIndex++;
                    continue;
                }
                int[] values = parameters.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToArray();
                int? memberIndex = null;
                string description = string.Empty;
                if (DirectEnemyTargetCodes.Contains(code) && values.Length > 0)
                {
                    memberIndex = values[0];
                    description = $"Battle command {code} targets an enemy member";
                }
                else if (code == 339 && values.Length > 1 && values[0] == 0)
                {
                    memberIndex = values[1];
                    description = "Force Action targets an enemy member";
                }
                else if (code == 111 && values.Length > 1 && values[0] == 5)
                {
                    memberIndex = values[1];
                    description = "Conditional Branch inspects an enemy member";
                }
                if (memberIndex is >= 0)
                {
                    result.Add(new TroopMemberReference(
                        page.Index,
                        commandIndex,
                        memberIndex.Value,
                        $"/{troopId}/pages/{page.Index}/list/{commandIndex}/parameters",
                        description));
                }
                commandIndex++;
            }
        }
        catch (JsonException)
        {
            // Invalid command lists are handled by the event-structure validator; the dependency report remains conservative.
        }
    }
}

public sealed class Phase9CMutationService
{
    private readonly MvEditingSession _session;

    public Phase9CMutationService(MvEditingSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask SetEnemyParametersAsync(string relativePath, int enemyId, IReadOnlyList<int> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count != 8) throw new ArgumentException("MV enemies require exactly eight parameters.", nameof(parameters));
        return SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(enemyId)).Append("params"), JsonSerializer.Serialize(parameters), $"Change enemy {enemyId} parameters", cancellationToken);
    }

    public ValueTask SetEnemyDropsAsync(string relativePath, int enemyId, IReadOnlyList<MvEnemyDropSnapshot> drops, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(enemyId)).Append("dropItems"), SerializeDrops(drops), $"Change enemy {enemyId} drops", cancellationToken);

    public ValueTask SetEnemyActionsAsync(string relativePath, int enemyId, IReadOnlyList<MvEnemyActionSnapshot> actions, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(enemyId)).Append("actions"), SerializeActions(actions), $"Change enemy {enemyId} action patterns", cancellationToken);

    public ValueTask SetTroopMembersAsync(string relativePath, int troopId, IReadOnlyList<MvTroopMemberSnapshot> members, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(troopId)).Append("members"), SerializeMembers(members), $"Change troop {troopId} formation", cancellationToken);

    public ValueTask SetTroopPagesAsync(string relativePath, int troopId, IReadOnlyList<MvTroopPageSnapshot> pages, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(troopId)).Append("pages"), SerializePages(pages), $"Change troop {troopId} event pages", cancellationToken);

    public ValueTask SetTroopPageConditionsAsync(
        string relativePath,
        int troopId,
        int pageIndex,
        MvTroopPageConditionSnapshot conditions,
        int span,
        CancellationToken cancellationToken = default)
    {
        JsonPath page = JsonPath.Root.Append(ValidateId(troopId)).Append("pages").Append(pageIndex);
        return ExecuteAsync(
            $"Change troop {troopId} page {pageIndex + 1} conditions",
            [
                new SetJsonValueCommand(relativePath, page.Append("conditions"), SerializeCondition(conditions)),
                new SetJsonValueCommand(relativePath, page.Append("span"), span.ToString(CultureInfo.InvariantCulture))
            ],
            cancellationToken);
    }

    public ValueTask SetBattlebacksAsync(
        string systemRelativePath,
        string battleback1Name,
        string battleback2Name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "Change troop editor battle background",
            [
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("battleback1Name"), EncodeString(battleback1Name ?? string.Empty)),
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("battleback2Name"), EncodeString(battleback2Name ?? string.Empty))
            ],
            cancellationToken);

    public ValueTask SetBattleTestProfileAsync(string systemRelativePath, BattleTestProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ExecuteAsync(
            $"Configure battle test for troop {profile.TroopId}",
            [
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("testTroopId"), profile.TroopId.ToString(CultureInfo.InvariantCulture)),
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("battleback1Name"), EncodeString(profile.Battleback1Name)),
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("battleback2Name"), EncodeString(profile.Battleback2Name)),
                new SetJsonValueCommand(systemRelativePath, JsonPath.Root.Append("testBattlers"), SerializeBattleTestBattlers(profile.Battlers))
            ],
            cancellationToken);
    }

    public ValueTask SetTroopNameAsync(string relativePath, int troopId, string name, CancellationToken cancellationToken = default) =>
        SetRawAsync(relativePath, JsonPath.Root.Append(ValidateId(troopId)).Append("name"), EncodeString(name), $"Rename troop {troopId}", cancellationToken);

    private ValueTask SetRawAsync(string relativePath, JsonPath path, string rawJson, string description, CancellationToken cancellationToken) =>
        ExecuteAsync(description, [new SetJsonValueCommand(relativePath, path, rawJson, description)], cancellationToken);

    private async ValueTask ExecuteAsync(string description, IReadOnlyList<IEditorCommand> commands, CancellationToken cancellationToken)
    {
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string SerializeActions(IReadOnlyList<MvEnemyActionSnapshot> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        JsonArray array = [];
        foreach (MvEnemyActionSnapshot action in actions)
        {
            JsonObject item = ParseObject(action.RawJson);
            item["skillId"] = action.SkillId;
            item["conditionType"] = action.ConditionType;
            item["conditionParam1"] = action.ConditionParam1;
            item["conditionParam2"] = action.ConditionParam2;
            item["rating"] = action.Rating;
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializeDrops(IReadOnlyList<MvEnemyDropSnapshot> drops)
    {
        ArgumentNullException.ThrowIfNull(drops);
        JsonArray array = [];
        foreach (MvEnemyDropSnapshot drop in drops)
        {
            JsonObject item = ParseObject(drop.RawJson);
            item["kind"] = drop.Kind;
            item["dataId"] = drop.DataId;
            item["denominator"] = drop.Denominator;
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializeMembers(IReadOnlyList<MvTroopMemberSnapshot> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        JsonArray array = [];
        foreach (MvTroopMemberSnapshot member in members.OrderBy(item => item.Index))
        {
            JsonObject item = ParseObject(member.RawJson);
            item["enemyId"] = member.EnemyId;
            item["x"] = member.X;
            item["y"] = member.Y;
            item["hidden"] = member.Hidden;
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializePages(IReadOnlyList<MvTroopPageSnapshot> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        JsonArray array = [];
        foreach (MvTroopPageSnapshot page in pages.OrderBy(item => item.Index))
        {
            JsonObject item = ParseObject(page.RawJson);
            item["conditions"] = JsonNode.Parse(SerializeCondition(page.Conditions));
            item["span"] = page.Span;
            item["list"] = JsonNode.Parse(page.CommandListRawJson) ?? new JsonArray();
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializeBattleTestBattlers(IReadOnlyList<BattleTestBattlerSnapshot> battlers)
    {
        ArgumentNullException.ThrowIfNull(battlers);
        JsonArray array = [];
        foreach (BattleTestBattlerSnapshot battler in battlers)
        {
            JsonObject item = ParseObject(battler.RawJson);
            item["actorId"] = battler.ActorId;
            item["level"] = battler.Level;
            item["equips"] = new JsonArray(battler.Equipment.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            array.Add(item);
        }
        return array.ToJsonString(Compact);
    }

    public static string SerializeCondition(MvTroopPageConditionSnapshot conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        JsonObject item = ParseObject(conditions.RawJson);
        item["turnEnding"] = conditions.TurnEnding;
        item["turnValid"] = conditions.TurnValid;
        item["turnA"] = conditions.TurnA;
        item["turnB"] = conditions.TurnB;
        item["enemyValid"] = conditions.EnemyValid;
        item["enemyIndex"] = conditions.EnemyIndex;
        item["enemyHp"] = conditions.EnemyHp;
        item["actorValid"] = conditions.ActorValid;
        item["actorId"] = conditions.ActorId;
        item["actorHp"] = conditions.ActorHp;
        item["switchValid"] = conditions.SwitchValid;
        item["switchId"] = conditions.SwitchId;
        return item.ToJsonString(Compact);
    }

    private static JsonObject ParseObject(string rawJson) => JsonNode.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson) as JsonObject ?? new JsonObject();
    private static string EncodeString(string value) => Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(value ?? string.Empty));
    private static int ValidateId(int id) => id > 0 ? id : throw new ArgumentOutOfRangeException(nameof(id));
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}


public sealed record EnemyThreatEstimate(
    int EnemyId,
    double Durability,
    double PhysicalPressure,
    double MagicalPressure,
    double SpeedPressure,
    double ControlPressure,
    double CompositeThreat,
    string Assumptions);

public sealed record TroopDifficultyEstimate(
    double Durability,
    double DamagePressure,
    double SpeedPressure,
    double ControlPressure,
    double CompositeThreat,
    double RewardToThreatRatio,
    string Classification,
    string Assumptions);

/// <summary>
/// Explainable, plugin-agnostic approximation for editor comparison. It deliberately does not claim
/// runtime-accurate combat simulation; Phase 11 and the balance laboratory can replace it with
/// project-runtime samples when available.
/// </summary>
public static class TroopDifficultyEstimator
{
    public static EnemyThreatEstimate EstimateEnemy(MvEnemyRecordSnapshot enemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        int[] parameters = enemy.Parameters.Concat(Enumerable.Repeat(0, 8)).Take(8).ToArray();
        double durability = Math.Max(1, parameters[0]) * (1d + Math.Max(0, parameters[3] + parameters[5]) / 400d);
        double physical = Math.Max(0, parameters[2]) * ActionAvailability(enemy, physical: true);
        double magical = Math.Max(0, parameters[4]) * ActionAvailability(enemy, physical: false);
        double speed = Math.Max(0, parameters[6]);
        double control = enemy.Actions.Count(action => action.ConditionType is (int)EnemyActionConditionType.State or (int)EnemyActionConditionType.Switch) * 15d;
        double threat = Math.Sqrt(durability) + physical * 0.8 + magical * 0.8 + speed * 0.2 + control;
        return new EnemyThreatEstimate(enemy.Id, durability, physical, magical, speed, control, threat,
            "MV Core Approximation: uses database parameters and action availability; plugin AI, custom formulas, traits, states, and runtime scripts are not simulated.");
    }

    public static TroopDifficultyEstimate EstimateTroop(
        MvTroopRecordSnapshot troop,
        IReadOnlyDictionary<int, MvEnemyRecordSnapshot> enemies)
    {
        ArgumentNullException.ThrowIfNull(troop);
        ArgumentNullException.ThrowIfNull(enemies);
        EnemyThreatEstimate[] estimates = troop.Members
            .Where(member => enemies.ContainsKey(member.EnemyId))
            .Select(member => EstimateEnemy(enemies[member.EnemyId]))
            .ToArray();
        if (estimates.Length == 0)
        {
            return new TroopDifficultyEstimate(0, 0, 0, 0, 0, 0, "Empty", "No valid enemy members were available.");
        }
        double durability = estimates.Sum(item => item.Durability);
        double damage = estimates.Sum(item => item.PhysicalPressure + item.MagicalPressure);
        double speed = estimates.Average(item => item.SpeedPressure);
        double control = estimates.Sum(item => item.ControlPressure);
        double groupMultiplier = 1d + Math.Max(0, estimates.Length - 1) * 0.12d;
        double composite = estimates.Sum(item => item.CompositeThreat) * groupMultiplier;
        double reward = troop.Members.Where(member => enemies.TryGetValue(member.EnemyId, out _)).Sum(member => enemies[member.EnemyId].Experience + enemies[member.EnemyId].Gold);
        double ratio = composite <= 0 ? 0 : reward / composite;
        string classification = composite switch
        {
            < 150 => "Trivial",
            < 400 => "Light",
            < 900 => "Moderate",
            < 1800 => "Hard",
            < 3500 => "Severe",
            _ => "Boss-scale"
        };
        return new TroopDifficultyEstimate(durability, damage, speed, control, composite, ratio, classification,
            "MV Core Approximation only. Compare encounters within the same project; do not treat the score as a universal balance rating.");
    }

    private static double ActionAvailability(MvEnemyRecordSnapshot enemy, bool physical)
    {
        if (enemy.Actions.Count == 0) return 0.5;
        double baseValue = enemy.Actions.Average(action => Math.Clamp(action.Rating, 1, 9)) / 5d;
        return Math.Clamp(baseValue, 0.25, 2.0) * (physical ? 1d : 0.9d);
    }
}

public sealed record TroopWaveDefinition(string Name, IReadOnlyList<int> MemberIndexes);

public sealed record MultiWaveCompilationResult(
    IReadOnlyList<MvTroopMemberSnapshot> Members,
    IReadOnlyList<MvTroopPageSnapshot> GeneratedPages,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Compiles a conservative wave model to ordinary MV hidden members and troop event commands.
/// Generated pages use Turn span and a script condition inside the page so they continue checking
/// until the prior wave is defeated, while refusing to repeat after the target wave has appeared.
/// </summary>
public static class MultiWaveCompiler
{
    public static MultiWaveCompilationResult Compile(
        IReadOnlyList<MvTroopMemberSnapshot> members,
        IReadOnlyList<TroopWaveDefinition> waves,
        int startingPageIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(waves);
        if (waves.Count < 2) return new MultiWaveCompilationResult(members.ToArray(), [], ["At least two waves are required."]);

        HashSet<int> all = [];
        List<string> warnings = [];
        foreach (TroopWaveDefinition wave in waves)
        {
            foreach (int index in wave.MemberIndexes)
            {
                if (index < 0 || index >= members.Count) warnings.Add($"Wave '{wave.Name}' refers to missing member #{index + 1}.");
                else if (!all.Add(index)) warnings.Add($"Member #{index + 1} appears in more than one wave.");
            }
        }
        if (warnings.Count > 0) return new MultiWaveCompilationResult(members.ToArray(), [], warnings);

        HashSet<int> firstWave = waves[0].MemberIndexes.ToHashSet();
        MvTroopMemberSnapshot[] compiledMembers = members.Select(member => member with { Hidden = !firstWave.Contains(member.Index) }).ToArray();
        List<MvTroopPageSnapshot> pages = [];
        for (int waveIndex = 1; waveIndex < waves.Count; waveIndex++)
        {
            int[] prior = waves.Take(waveIndex).SelectMany(wave => wave.MemberIndexes).Distinct().OrderBy(value => value).ToArray();
            int[] targets = waves[waveIndex].MemberIndexes.Distinct().OrderBy(value => value).ToArray();
            string priorArray = string.Join(',', prior);
            string targetArray = string.Join(',', targets);
            string script = $"(function(){{var m=$gameTroop.members();var p=[{priorArray}],t=[{targetArray}];return p.every(function(i){{return m[i]&&m[i].isDead();}})&&t.some(function(i){{return m[i]&&m[i].isHidden();}});}})()";
            JsonArray commands =
            [
                new JsonObject { ["code"] = 111, ["indent"] = 0, ["parameters"] = new JsonArray(12, script) }
            ];
            foreach (int target in targets)
            {
                commands.Add(new JsonObject { ["code"] = 335, ["indent"] = 1, ["parameters"] = new JsonArray(target) });
            }
            commands.Add(new JsonObject { ["code"] = 412, ["indent"] = 0, ["parameters"] = new JsonArray() });
            commands.Add(new JsonObject { ["code"] = 0, ["indent"] = 0, ["parameters"] = new JsonArray() });
            MvTroopPageConditionSnapshot condition = new(
                TurnEnding: true,
                TurnValid: false,
                TurnA: 0,
                TurnB: 0,
                EnemyValid: false,
                EnemyIndex: 0,
                EnemyHp: 50,
                ActorValid: false,
                ActorId: 1,
                ActorHp: 50,
                SwitchValid: false,
                SwitchId: 1,
                RawJson: "{}");
            string commandJson = commands.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            JsonObject rawPage = new()
            {
                ["conditions"] = JsonNode.Parse(Phase9CMutationService.SerializeCondition(condition)),
                ["list"] = JsonNode.Parse(commandJson),
                ["span"] = (int)TroopPageSpan.Turn
            };
            pages.Add(new MvTroopPageSnapshot(
                startingPageIndex + pages.Count,
                condition,
                (int)TroopPageSpan.Turn,
                commands.Count,
                commandJson,
                rawPage.ToJsonString(new JsonSerializerOptions { WriteIndented = false })));
        }
        warnings.Add("Generated wave pages use ordinary MV script branches and Enemy Appear commands. Review them in the Event Editor before adding custom cutscenes or plugin behavior.");
        return new MultiWaveCompilationResult(compiledMembers, pages, warnings);
    }
}

public sealed record BattleTestBattlerSnapshot(int ActorId, int Level, IReadOnlyList<int> Equipment, string RawJson);

public sealed record BattleTestProfile(
    int TroopId,
    string Battleback1Name,
    string Battleback2Name,
    IReadOnlyList<BattleTestBattlerSnapshot> Battlers,
    string Name = "Default");

public static class BattleTestProfileReader
{
    public static BattleTestProfile Read(MutableJsonProjectDocument systemDocument, int? troopOverride = null)
    {
        ArgumentNullException.ThrowIfNull(systemDocument);
        if (systemDocument.Kind != MvDocumentKind.System) throw new ArgumentException("A System document is required.", nameof(systemDocument));
        List<BattleTestBattlerSnapshot> battlers = [];
        JsonPath path = JsonPath.Root.Append("testBattlers");
        if (systemDocument.Syntax.Resolve(path) is JsonArraySyntax array)
        {
            for (int index = 0; index < array.Elements.Count; index++)
            {
                JsonPath item = path.Append(index);
                if (systemDocument.Syntax.Resolve(item) is not JsonObjectSyntax) continue;
                battlers.Add(new BattleTestBattlerSnapshot(
                    systemDocument.GetInt32(item.Append("actorId")) ?? 1,
                    systemDocument.GetInt32(item.Append("level")) ?? 1,
                    Phase9CRecordReader.ReadIntArray(systemDocument, item.Append("equips")),
                    systemDocument.GetRawJson(item) ?? "{}"));
            }
        }
        return new BattleTestProfile(
            troopOverride ?? systemDocument.GetInt32(JsonPath.Root.Append("testTroopId")) ?? 1,
            systemDocument.GetString(JsonPath.Root.Append("battleback1Name")) ?? string.Empty,
            systemDocument.GetString(JsonPath.Root.Append("battleback2Name")) ?? string.Empty,
            battlers,
            "System battle test");
    }
}

public interface IBattleTestLauncher
{
    bool IsAvailable { get; }
    ValueTask<string> LaunchAsync(BattleTestProfile profile, CancellationToken cancellationToken = default);
}

public interface IEventCommandEditorLauncher
{
    bool IsAvailable { get; }
    ValueTask OpenTroopPageAsync(int troopId, int pageIndex, CancellationToken cancellationToken = default);
}

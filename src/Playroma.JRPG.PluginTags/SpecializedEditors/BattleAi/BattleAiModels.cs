using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public enum BattleAiConditionKind
{
    Always,
    RandomChance,
    Turn,
    Switch,
    Variable,
    StatePresent,
    StateAbsent,
    ElementRate,
    PartyAliveMembers,
    PartyDeadMembers,
    TroopAliveMembers,
    TroopDeadMembers,
    Parameter,
    UserParameter,
    HighestPartyLevel,
    Eval,
    Multiple,
    Raw
}

public enum BattleAiElementCase
{
    Neutral,
    Weakness,
    Resistant,
    Null,
    Absorb
}

public sealed record BattleAiCondition(
    BattleAiConditionKind Kind,
    string Operand = "",
    string Comparison = "",
    string Value = "",
    string SecondaryValue = "",
    IReadOnlyList<BattleAiCondition>? Children = null)
{
    public IReadOnlyList<BattleAiCondition> EffectiveChildren => Children ?? [];
}

public sealed record BattleAiRule(
    BattleAiCondition Condition,
    string SkillToken,
    string Target,
    string? OriginalText = null,
    bool IsEnabled = true)
{
    public bool UsesSkillId => Regex.IsMatch(SkillToken ?? string.Empty, "^\\s*(?:skill\\s+)?\\d+\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
}

public sealed record BattleAiRawLine(string Text, int BeforeRuleIndex);

public sealed record BattleAiPriorityModel(
    int AiLevel,
    bool ConsiderTaunt,
    IReadOnlyList<BattleAiRule> Rules,
    IReadOnlyList<BattleAiRawLine>? UnparsedLines = null)
{
    public IReadOnlyList<BattleAiRawLine> EffectiveUnparsedLines => UnparsedLines ?? [];
}

public sealed record BattleAiValidationIssue(int? RuleIndex, string Severity, string Message);

public static class BattleAiTargets
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Random", "First", "User",
        "Highest MaxHP", "Highest HP", "Highest HP%", "Highest MaxMP", "Highest MP", "Highest MP%",
        "Highest MaxTP", "Highest TP", "Highest TP%", "Highest ATK", "Highest DEF", "Highest MAT",
        "Highest MDF", "Highest AGI", "Highest LUK", "Highest Level",
        "Lowest MaxHP", "Lowest HP", "Lowest HP%", "Lowest MaxMP", "Lowest MP", "Lowest MP%",
        "Lowest MaxTP", "Lowest TP", "Lowest TP%", "Lowest ATK", "Lowest DEF", "Lowest MAT",
        "Lowest MDF", "Lowest AGI", "Lowest LUK", "Lowest Level"
    ];
}

public static class BattleAiConditionCodec
{
    private static readonly Regex Random = new("^Random\\s+(?<value>\\d+(?:\\.\\d+)?)%$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Turn = new("^Turn\\s*(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Switch = new("^Switch\\s+(?<id>\\d+)\\s+(?<value>On|Off)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Variable = new("^Variable\\s+(?<id>\\d+)\\s*(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex State = new("^State\\s*(?<op>===|!==)\\s*(?:State\\s+)?(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Element = new("^Element\\s+(?<element>.+?)\\s+(?<case>Neutral|Weakness|Resistant|Null|Absorb)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Members = new("^(?<group>Party|Troop)\\s+(?<life>Alive|Dead)\\s+Members\\s*(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HighestPartyLevel = new("^Highest\\s+Party\\s+Level\\s*(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex UserParameter = new("^User\\s+(?<param>MaxHP|MaxMP|MaxTP|HP%?|MP%?|TP%?|ATK|DEF|MAT|MDF|AGI|LUK|Level)\\s+(?:Param\\s+)?(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Parameter = new("^(?<param>MaxHP|MaxMP|MaxTP|HP%?|MP%?|TP%?|ATK|DEF|MAT|MDF|AGI|LUK|Level)\\s+(?:Param\\s+)?(?<op>===|!==|>=|<=|>|<|=)\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static BattleAiCondition Parse(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        if (value.Equals("Always", StringComparison.OrdinalIgnoreCase)) return new(BattleAiConditionKind.Always);
        if (value.StartsWith("Eval ", StringComparison.OrdinalIgnoreCase)) return new(BattleAiConditionKind.Eval, Value: value[5..].Trim());
        if (value.Contains("+++", StringComparison.Ordinal))
            return new(BattleAiConditionKind.Multiple, Children: value.Split("+++", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray());

        Match match;
        if ((match = Random.Match(value)).Success) return new(BattleAiConditionKind.RandomChance, Value: match.Groups["value"].Value);
        if ((match = Turn.Match(value)).Success) return new(BattleAiConditionKind.Turn, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        if ((match = Switch.Match(value)).Success) return new(BattleAiConditionKind.Switch, Operand: match.Groups["id"].Value, Value: match.Groups["value"].Value);
        if ((match = Variable.Match(value)).Success) return new(BattleAiConditionKind.Variable, Operand: match.Groups["id"].Value, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        if ((match = State.Match(value)).Success) return new(match.Groups["op"].Value == "!==" ? BattleAiConditionKind.StateAbsent : BattleAiConditionKind.StatePresent, Value: match.Groups["value"].Value);
        if ((match = Element.Match(value)).Success) return new(BattleAiConditionKind.ElementRate, Operand: match.Groups["element"].Value, Value: match.Groups["case"].Value);
        if ((match = Members.Match(value)).Success)
        {
            BattleAiConditionKind kind = (match.Groups["group"].Value.ToLowerInvariant(), match.Groups["life"].Value.ToLowerInvariant()) switch
            {
                ("party", "alive") => BattleAiConditionKind.PartyAliveMembers,
                ("party", "dead") => BattleAiConditionKind.PartyDeadMembers,
                ("troop", "alive") => BattleAiConditionKind.TroopAliveMembers,
                _ => BattleAiConditionKind.TroopDeadMembers
            };
            return new(kind, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        }
        if ((match = HighestPartyLevel.Match(value)).Success) return new(BattleAiConditionKind.HighestPartyLevel, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        if ((match = UserParameter.Match(value)).Success) return new(BattleAiConditionKind.UserParameter, Operand: match.Groups["param"].Value, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        if ((match = Parameter.Match(value)).Success) return new(BattleAiConditionKind.Parameter, Operand: match.Groups["param"].Value, Comparison: NormalizeOperator(match.Groups["op"].Value), Value: match.Groups["value"].Value);
        return new(BattleAiConditionKind.Raw, Value: value);
    }

    public static string Render(BattleAiCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        string op = string.IsNullOrWhiteSpace(condition.Comparison) ? ">=" : NormalizeOperator(condition.Comparison);
        return condition.Kind switch
        {
            BattleAiConditionKind.Always => "Always",
            BattleAiConditionKind.RandomChance => $"Random {EnsurePercentNumber(condition.Value)}%",
            BattleAiConditionKind.Turn => $"Turn {op} {condition.Value}",
            BattleAiConditionKind.Switch => $"Switch {condition.Operand} {NormalizeOnOff(condition.Value)}",
            BattleAiConditionKind.Variable => $"Variable {condition.Operand} {op} {condition.Value}",
            BattleAiConditionKind.StatePresent => $"State === {NormalizeState(condition.Value)}",
            BattleAiConditionKind.StateAbsent => $"State !== {NormalizeState(condition.Value)}",
            BattleAiConditionKind.ElementRate => $"Element {condition.Operand} {NormalizeElementCase(condition.Value)}",
            BattleAiConditionKind.PartyAliveMembers => $"Party Alive Members {op} {condition.Value}",
            BattleAiConditionKind.PartyDeadMembers => $"Party Dead Members {op} {condition.Value}",
            BattleAiConditionKind.TroopAliveMembers => $"Troop Alive Members {op} {condition.Value}",
            BattleAiConditionKind.TroopDeadMembers => $"Troop Dead Members {op} {condition.Value}",
            BattleAiConditionKind.Parameter => $"{condition.Operand} Param {op} {condition.Value}",
            BattleAiConditionKind.UserParameter => $"User {condition.Operand} Param {op} {condition.Value}",
            BattleAiConditionKind.HighestPartyLevel => $"Highest Party Level {op} {condition.Value}",
            BattleAiConditionKind.Eval => "Eval " + condition.Value,
            BattleAiConditionKind.Multiple => string.Join(" +++ ", condition.EffectiveChildren.Select(Render)),
            _ => condition.Value
        };
    }

    private static string NormalizeState(string value)
    {
        string trimmed = value.Trim();
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? $"State {id}" : trimmed;
    }

    private static string NormalizeOperator(string value) => value.Trim() == "=" ? "===" : value.Trim();
    private static string NormalizeOnOff(string value) => value.Equals("off", StringComparison.OrdinalIgnoreCase) ? "Off" : "On";
    private static string NormalizeElementCase(string value) => Enum.TryParse(value, true, out BattleAiElementCase parsed) ? parsed.ToString() : value.Trim();
    private static string EnsurePercentNumber(string value) => value.Trim().TrimEnd('%');
}

public static class BattleAiPriorityCodec
{
    private static readonly Regex AiLevel = new("^<\\s*AI\\s+Level\\s*:\\s*(?<value>\\d+)\\s*>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex ConsiderTaunt = new("^<\\s*AI\\s+Consider\\s+Taunt\\s*>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Opening = new("^<\\s*AI\\s+Priority\\s*>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Closing = new("^<\\s*/\\s*AI\\s+Priority\\s*>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex RuleLine = new("^(?<condition>.+):\\s*(?<skill>[^,]+?)(?:,\\s*(?<target>.+))?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static BattleAiPriorityModel Parse(string? notes)
    {
        string text = notes ?? string.Empty;
        string[] lines = SplitLines(text);
        int aiLevel = 100;
        bool considerTaunt = false;
        bool inBlock = false;
        List<BattleAiRule> rules = [];
        List<BattleAiRawLine> unparsed = [];
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            Match level = AiLevel.Match(line);
            if (level.Success) { aiLevel = int.Parse(level.Groups["value"].Value, CultureInfo.InvariantCulture); continue; }
            if (ConsiderTaunt.IsMatch(line)) { considerTaunt = true; continue; }
            if (Opening.IsMatch(line)) { inBlock = true; continue; }
            if (Closing.IsMatch(line)) { inBlock = false; continue; }
            if (!inBlock) continue;
            if (line.Length == 0) { unparsed.Add(new(raw, rules.Count)); continue; }
            if (TryParseRule(line, out BattleAiRule? rule)) rules.Add(rule!); else unparsed.Add(new(raw, rules.Count));
        }
        return new(Math.Clamp(aiLevel, 0, 100), considerTaunt, rules, unparsed);
    }

    public static bool TryParseRule(string text, out BattleAiRule? rule)
    {
        Match match = RuleLine.Match(text ?? string.Empty);
        if (!match.Success) { rule = null; return false; }
        string condition = match.Groups["condition"].Value.Trim();
        string skill = match.Groups["skill"].Value.Trim();
        string target = match.Groups["target"].Success ? match.Groups["target"].Value.Trim() : "Random";
        if (condition.Length == 0 || skill.Length == 0) { rule = null; return false; }
        rule = new(BattleAiConditionCodec.Parse(condition), skill, target.Length == 0 ? "Random" : target, text);
        return true;
    }

    public static string RenderRule(BattleAiRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        string condition = BattleAiConditionCodec.Render(rule.Condition);
        string skill = NormalizeSkill(rule.SkillToken);
        string target = string.IsNullOrWhiteSpace(rule.Target) || rule.Target.Equals("Random", StringComparison.OrdinalIgnoreCase)
            ? string.Empty : ", " + rule.Target.Trim();
        return $"{condition}: {skill}{target}";
    }

    public static string RenderBlock(BattleAiPriorityModel model, string lineEnding = "\n")
    {
        ArgumentNullException.ThrowIfNull(model);
        List<string> lines = ["<AI Priority>"];
        for (int ruleIndex = 0; ruleIndex <= model.Rules.Count; ruleIndex++)
        {
            lines.AddRange(model.EffectiveUnparsedLines.Where(raw => raw.BeforeRuleIndex == ruleIndex).Select(raw => raw.Text));
            if (ruleIndex < model.Rules.Count && model.Rules[ruleIndex].IsEnabled) lines.Add(RenderRule(model.Rules[ruleIndex]));
        }
        lines.AddRange(model.EffectiveUnparsedLines.Where(raw => raw.BeforeRuleIndex > model.Rules.Count).Select(raw => raw.Text));
        lines.Add("</AI Priority>");
        return string.Join(lineEnding, lines);
    }

    public static IReadOnlyList<BattleAiValidationIssue> Validate(BattleAiPriorityModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<BattleAiValidationIssue> issues = [];
        if (model.AiLevel is < 0 or > 100) issues.Add(new(null, "Error", "AI Level must be between 0 and 100."));
        for (int index = 0; index < model.Rules.Count; index++)
        {
            BattleAiRule rule = model.Rules[index];
            if (string.IsNullOrWhiteSpace(rule.SkillToken)) issues.Add(new(index, "Error", "A skill is required."));
            if (rule.UsesSkillId)
            {
                string digits = Regex.Match(rule.SkillToken, "\\d+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)).Value;
                if (!int.TryParse(digits, out int id) || id <= 0) issues.Add(new(index, "Error", "Skill ID must be greater than zero."));
            }
            if (string.IsNullOrWhiteSpace(BattleAiConditionCodec.Render(rule.Condition))) issues.Add(new(index, "Error", "A condition is required."));
            if (!string.IsNullOrWhiteSpace(rule.Target) && !BattleAiTargets.All.Contains(rule.Target, StringComparer.OrdinalIgnoreCase))
                issues.Add(new(index, "Warning", $"'{rule.Target}' is a custom target selector and will be preserved as written."));
        }
        return issues;
    }

    public static string ReplaceInNotes(string notes, BattleAiPriorityModel model, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(model);
        string result = notes ?? string.Empty;
        result = Regex.Replace(result, @"(?im)^[ \t]*<\s*AI\s+Level\s*:\s*\d+\s*>[ \t]*(?:(?:\r?\n|\r)|$)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        result = Regex.Replace(result, @"(?im)^[ \t]*<\s*AI\s+Consider\s+Taunt\s*>[ \t]*(?:(?:\r?\n|\r)|$)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        result = Regex.Replace(result, @"(?ims)^[ \t]*<\s*AI\s+Priority\s*>[ \t]*(?:\r?\n|\r).*?^[ \t]*<\s*/\s*AI\s+Priority\s*>[ \t]*(?:(?:\r?\n|\r)|$)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
        List<string> generated = [$"<AI Level: {Math.Clamp(model.AiLevel, 0, 100)}>"];
        if (model.ConsiderTaunt) generated.Add("<AI Consider Taunt>");
        if (model.Rules.Count > 0 || model.EffectiveUnparsedLines.Count > 0) generated.Add(RenderBlock(model, lineEnding));
        string block = string.Join(lineEnding, generated);
        result = result.TrimEnd('\r', '\n');
        return result.Length == 0 ? block : result + lineEnding + lineEnding + block;
    }

    private static string NormalizeSkill(string token)
    {
        string trimmed = token.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return $"Skill {id}";
        return trimmed;
    }

    private static string[] SplitLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
}

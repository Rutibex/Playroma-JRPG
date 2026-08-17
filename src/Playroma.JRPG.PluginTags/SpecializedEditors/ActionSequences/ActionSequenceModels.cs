using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public enum ActionSequencePhase
{
    Setup,
    Whole,
    Target,
    Follow,
    Finish
}

public enum ActionSequenceEntryKind
{
    Command,
    Comment,
    Blank,
    Raw
}

public sealed record ActionSequenceEntry(
    ActionSequenceEntryKind Kind,
    string Text,
    string CommandName = "",
    string Arguments = "",
    string? PluginId = null,
    bool IsEnabled = true);

public sealed record ActionSequencePhaseModel(
    ActionSequencePhase Phase,
    IReadOnlyList<ActionSequenceEntry> Entries,
    bool IsPresent = true);

public sealed record ActionSequenceModel(
    IReadOnlyDictionary<ActionSequencePhase, ActionSequencePhaseModel> Phases,
    string? CopyType = null,
    int? CopyId = null)
{
    public ActionSequencePhaseModel Get(ActionSequencePhase phase) =>
        Phases.TryGetValue(phase, out ActionSequencePhaseModel? found) ? found : new(phase, [], false);
}

public sealed record ActionSequenceValidationIssue(ActionSequencePhase Phase, int? EntryIndex, string Severity, string Message);

public sealed record ActionSequenceCommandDescriptor(
    string CommandId,
    string DisplayName,
    string CanonicalLine,
    string PluginId,
    string Category,
    IReadOnlyList<string> SearchTerms,
    string? Description = null,
    bool IsAdvanced = false);

public sealed record ActionSequenceTemplate(
    string TemplateId,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<ActionSequencePhase, IReadOnlyList<string>> Commands,
    IReadOnlyList<string>? RequiredPlugins = null);

public static class ActionSequenceCodec
{
    private static readonly Regex CommandPattern = new("^(?<name>[A-Za-z][A-Za-z0-9 _-]*?)(?:\\s*:\\s*(?<args>.*))?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex ActionCopy = new("^<\\s*action\\s+copy\\s*:\\s*(?<type>skill|item)\\s*:\\s*(?<id>\\d+)\\s*>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public static IReadOnlyDictionary<ActionSequencePhase, (string Open, string Close)> Tags { get; } =
        new Dictionary<ActionSequencePhase, (string, string)>
        {
            [ActionSequencePhase.Setup] = ("<setup action>", "</setup action>"),
            [ActionSequencePhase.Whole] = ("<whole action>", "</whole action>"),
            [ActionSequencePhase.Target] = ("<target action>", "</target action>"),
            [ActionSequencePhase.Follow] = ("<follow action>", "</follow action>"),
            [ActionSequencePhase.Finish] = ("<finish action>", "</finish action>")
        };

    public static ActionSequenceModel Parse(string? notes)
    {
        string text = notes ?? string.Empty;
        Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = [];
        string? copyType = null;
        int? copyId = null;
        string[] lines = SplitLines(text);
        ActionSequencePhase? current = null;
        List<ActionSequenceEntry>? entries = null;
        foreach (string raw in lines)
        {
            string trimmed = raw.Trim();
            Match copy = ActionCopy.Match(trimmed);
            if (copy.Success)
            {
                copyType = copy.Groups["type"].Value.ToLowerInvariant();
                copyId = int.Parse(copy.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            ActionSequencePhase? opening = FindOpening(trimmed);
            if (opening.HasValue)
            {
                current = opening;
                entries = [];
                continue;
            }
            if (current.HasValue && IsClosing(trimmed, current.Value))
            {
                phases[current.Value] = new(current.Value, entries ?? [], true);
                current = null;
                entries = null;
                continue;
            }
            if (current.HasValue) entries!.Add(ParseEntry(raw));
        }
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
            phases.TryAdd(phase, new(phase, [], false));
        return new(phases, copyType, copyId);
    }

    public static ActionSequenceEntry ParseEntry(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return new(ActionSequenceEntryKind.Blank, raw);
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
            return new(ActionSequenceEntryKind.Comment, raw);
        Match match = CommandPattern.Match(trimmed);
        if (!match.Success) return new(ActionSequenceEntryKind.Raw, raw);
        return new(ActionSequenceEntryKind.Command, raw, match.Groups["name"].Value.Trim(), match.Groups["args"].Value.Trim());
    }

    public static string RenderEntry(ActionSequenceEntry entry) => entry.IsEnabled ? entry.Text : string.Empty;

    public static string RenderPhase(ActionSequencePhaseModel phase, string lineEnding = "\n")
    {
        (string opening, string closing) = Tags[phase.Phase];
        List<string> lines = [opening];
        lines.AddRange(phase.Entries.Where(entry => entry.IsEnabled).Select(RenderEntry));
        lines.Add(closing);
        return string.Join(lineEnding, lines);
    }

    public static string RenderAll(ActionSequenceModel model, string lineEnding = "\n")
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!string.IsNullOrWhiteSpace(model.CopyType) && model.CopyId is > 0)
            return $"<action copy: {model.CopyType!.ToLowerInvariant()}:{model.CopyId.Value}>";
        return string.Join(lineEnding + lineEnding, Enum.GetValues<ActionSequencePhase>()
            .Select(model.Get)
            .Where(phase => phase.IsPresent || phase.Entries.Count > 0)
            .Select(phase => RenderPhase(phase, lineEnding)));
    }

    public static IReadOnlyList<ActionSequenceValidationIssue> Validate(ActionSequenceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<ActionSequenceValidationIssue> issues = [];
        if (!string.IsNullOrWhiteSpace(model.CopyType))
        {
            if (model.CopyType is not "skill" and not "item") issues.Add(new(ActionSequencePhase.Setup, null, "Error", "Action Copy type must be skill or item."));
            if (model.CopyId is null or <= 0) issues.Add(new(ActionSequencePhase.Setup, null, "Error", "Action Copy ID must be greater than zero."));
            if (model.Phases.Values.Any(phase => phase.Entries.Count > 0)) issues.Add(new(ActionSequencePhase.Setup, null, "Warning", "Action Copy takes priority over custom phase blocks at runtime."));
        }
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
        {
            ActionSequencePhaseModel data = model.Get(phase);
            int ifDepth = 0;
            for (int index = 0; index < data.Entries.Count; index++)
            {
                ActionSequenceEntry entry = data.Entries[index];
                if (!entry.IsEnabled || entry.Kind is ActionSequenceEntryKind.Blank or ActionSequenceEntryKind.Comment) continue;
                string line = entry.Text.Trim();
                if (line.StartsWith("IF ", StringComparison.OrdinalIgnoreCase)) ifDepth++;
                else if (line.Equals("ELSE", StringComparison.OrdinalIgnoreCase) && ifDepth == 0)
                    issues.Add(new(phase, index, "Error", "ELSE has no matching IF."));
                else if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    if (ifDepth == 0) issues.Add(new(phase, index, "Error", "END has no matching IF."));
                    else ifDepth--;
                }
                if (line.Contains("(frames)", StringComparison.OrdinalIgnoreCase) || line.Contains("(target)", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new(phase, index, "Warning", "This command still contains a documentation placeholder."));
            }
            if (ifDepth > 0) issues.Add(new(phase, null, "Error", $"{ifDepth} IF block(s) are missing END."));
        }
        return issues;
    }

    public static string ReplaceSequencesInNotes(string notes, ActionSequenceModel model, string lineEnding)
    {
        string result = notes ?? string.Empty;
        result = RemoveActionCopy(result);
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>()) result = RemovePhase(result, phase);
        string generated = RenderAll(model, lineEnding);
        if (string.IsNullOrWhiteSpace(generated)) return TrimExcessBlankLines(result);
        result = result.TrimEnd('\r', '\n');
        return result.Length == 0 ? generated : result + lineEnding + lineEnding + generated;
    }

    public static string RemovePhase(string notes, ActionSequencePhase phase)
    {
        (string open, string close) = Tags[phase];
        string pattern = $"(?ims)^[ \\t]*{Regex.Escape(open)}[ \\t]*(?:\\r?\\n|\\r).*?^[ \\t]*{Regex.Escape(close)}[ \\t]*(?:(?:\\r?\\n|\\r)|$)";
        return Regex.Replace(notes ?? string.Empty, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));
    }

    public static string RemoveActionCopy(string notes) => Regex.Replace(notes ?? string.Empty,
        "(?im)^[ \\t]*<\\s*action\\s+copy\\s*:\\s*(?:skill|item)\\s*:\\s*\\d+\\s*>[ \\t]*(?:(?:\\r?\\n|\\r)|$)",
        string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    private static ActionSequencePhase? FindOpening(string line)
    {
        foreach (KeyValuePair<ActionSequencePhase, (string Open, string Close)> pair in Tags)
            if (line.Equals(pair.Value.Open, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        return null;
    }

    private static bool IsClosing(string line, ActionSequencePhase phase) => line.Equals(Tags[phase].Close, StringComparison.OrdinalIgnoreCase);
    private static string[] SplitLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static string TrimExcessBlankLines(string value) => Regex.Replace(value.TrimEnd('\r', '\n'), "(?:\\r?\\n|\\r){3,}", Environment.NewLine + Environment.NewLine, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
}

public static class ActionSequenceTemplates
{
    public static IReadOnlyList<ActionSequenceTemplate> BuiltIn { get; } =
    [
        new("default-physical", "Default Physical Attack", "Approach, strike, apply damage, and return.", new Dictionary<ActionSequencePhase, IReadOnlyList<string>>
        {
            [ActionSequencePhase.Setup] = ["DISPLAY ACTION", "IMMORTAL: TARGETS, TRUE", "PERFORM START", "WAIT FOR MOVEMENT"],
            [ActionSequencePhase.Target] = ["MOVE USER: TARGET, FRONT BASE, 20", "WAIT FOR MOVEMENT", "MOTION ATTACK: USER", "WAIT: 10", "ACTION ANIMATION", "WAIT FOR ANIMATION", "ACTION EFFECT", "DEATH BREAK"],
            [ActionSequencePhase.Follow] = ["IMMORTAL: TARGETS, FALSE"],
            [ActionSequencePhase.Finish] = ["WAIT FOR NEW LINE", "CLEAR BATTLE LOG", "PERFORM FINISH", "WAIT FOR MOVEMENT"]
        }, ["YEP_BattleEngineCore", "YEP_X_ActSeqPack1", "YEP_X_ActSeqPack2"]),
        new("default-spell", "Default Spell Cast", "Show the cast animation, play the action animation, and apply the effect.", new Dictionary<ActionSequencePhase, IReadOnlyList<string>>
        {
            [ActionSequencePhase.Setup] = ["DISPLAY ACTION", "IMMORTAL: TARGETS, TRUE", "CAST ANIMATION", "WAIT FOR ANIMATION"],
            [ActionSequencePhase.Target] = ["ACTION ANIMATION", "WAIT FOR ANIMATION", "ACTION EFFECT", "DEATH BREAK"],
            [ActionSequencePhase.Follow] = ["IMMORTAL: TARGETS, FALSE"],
            [ActionSequencePhase.Finish] = ["WAIT FOR NEW LINE", "CLEAR BATTLE LOG"]
        }, ["YEP_BattleEngineCore", "YEP_X_ActSeqPack1"]),
        new("jumping-strike", "Jumping Strike", "Approach the target, leap into an attack, apply damage, and return.", new Dictionary<ActionSequencePhase, IReadOnlyList<string>>
        {
            [ActionSequencePhase.Setup] = ["DISPLAY ACTION", "IMMORTAL: TARGETS, TRUE", "PERFORM START"],
            [ActionSequencePhase.Target] = ["MOVE USER: TARGET, FRONT BASE, 20", "WAIT FOR MOVEMENT", "JUMP USER: 150%, 24", "WAIT FOR JUMP", "MOTION ATTACK: USER", "ACTION ANIMATION", "WAIT FOR ANIMATION", "ACTION EFFECT"],
            [ActionSequencePhase.Follow] = ["IMMORTAL: TARGETS, FALSE"],
            [ActionSequencePhase.Finish] = ["PERFORM FINISH", "WAIT FOR MOVEMENT"]
        }, ["YEP_BattleEngineCore", "YEP_X_ActSeqPack1", "YEP_X_ActSeqPack2"]),
        new("cinematic", "Cinematic Skill", "Focus the camera, zoom, play the skill, then restore the view.", new Dictionary<ActionSequencePhase, IReadOnlyList<string>>
        {
            [ActionSequencePhase.Setup] = ["CAMERA FOCUS: USER, FRONT CENTER, 30", "ZOOM: 125%, 30", "WAIT FOR CAMERA"],
            [ActionSequencePhase.Target] = ["CAMERA FOCUS: TARGET, CENTER, 20", "ACTION ANIMATION", "WAIT FOR ANIMATION", "ACTION EFFECT"],
            [ActionSequencePhase.Finish] = ["RESET CAMERA: 30", "RESET ZOOM: 30", "WAIT FOR CAMERA"]
        }, ["YEP_BattleEngineCore", "YEP_X_ActSeqPack1", "YEP_X_ActSeqPack3"]),
        new("multi-hit", "Three-Hit Combo", "Perform three timed effects against the active target.", new Dictionary<ActionSequencePhase, IReadOnlyList<string>>
        {
            [ActionSequencePhase.Setup] = ["IMMORTAL: TARGETS, TRUE", "PERFORM START"],
            [ActionSequencePhase.Target] = ["MOTION ATTACK: USER", "ACTION ANIMATION", "ACTION EFFECT", "WAIT: 8", "MOTION ATTACK: USER", "ACTION EFFECT", "WAIT: 8", "MOTION ATTACK: USER", "ACTION EFFECT"],
            [ActionSequencePhase.Follow] = ["IMMORTAL: TARGETS, FALSE"],
            [ActionSequencePhase.Finish] = ["PERFORM FINISH", "WAIT FOR MOVEMENT"]
        }, ["YEP_BattleEngineCore"])
    ];
}

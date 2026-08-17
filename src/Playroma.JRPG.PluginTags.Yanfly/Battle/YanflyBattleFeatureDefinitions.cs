using System.Text.RegularExpressions;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyBattleFeatureDefinitions
{
    public const string BattleAiEditorId = "yep.battle-ai-priority";
    public const string ActionSequenceEditorId = "yep.action-sequence";

    public static IEnumerable<PluginFeatureDefinition> ApplyCorrections(IEnumerable<PluginFeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (PluginFeatureDefinition feature in features)
        {
            if (feature.PluginId.Equals("YEP_BattleAICore", StringComparison.OrdinalIgnoreCase) &&
                feature.SpecializedEditorId?.Equals(BattleAiEditorId, StringComparison.OrdinalIgnoreCase) == true)
            {
                yield return feature with { DocumentKinds = new HashSet<PluginDocumentKind> { PluginDocumentKind.Enemy } };
                continue;
            }
            yield return feature;
        }
    }

    public static IReadOnlyList<PluginFeatureDefinition> CreatePhaseDefinitions() =>
    [
        CreatePhase(ActionSequencePhase.Setup, "action-sequence.phase.setup", "Setup Action Sequence", 100),
        CreatePhase(ActionSequencePhase.Whole, "action-sequence.phase.whole", "Whole Action Sequence", 99),
        CreatePhase(ActionSequencePhase.Target, "action-sequence.phase.target", "Target Action Sequence", 98),
        CreatePhase(ActionSequencePhase.Follow, "action-sequence.phase.follow", "Follow Action Sequence", 97),
        CreatePhase(ActionSequencePhase.Finish, "action-sequence.phase.finish", "Finish Action Sequence", 96)
    ];

    public static void RegisterSpecializedEditors(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlySet<PluginFeatureKey> aiFeatures = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(BattleAiEditorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        IReadOnlySet<PluginFeatureKey> sequenceFeatures = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(ActionSequenceEditorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        registry.Register(new(BattleAiEditorId, "Battle AI Priority", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote }, aiFeatures, true,
            "Structured enemy decision rules, AI level, skill selection, target selection, and taunt awareness."), replace: true);
        registry.Register(new(ActionSequenceEditorId, "Action Sequences", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote, PluginAuthoringSurface.ActionSequence }, sequenceFeatures, true,
            "Five-phase Battle Engine action-sequence authoring with command palette and templates."), replace: true);
    }

    private static PluginFeatureDefinition CreatePhase(ActionSequencePhase phase, string id, string displayName, int priority)
    {
        (string opening, string closing) = ActionSequenceCodec.Tags[phase];
        string tagName = phase.ToString().ToLowerInvariant();
        return new(
            YanflyProviderIdentity.ProviderId,
            "YEP_BattleEngineCore",
            id,
            displayName,
            $"Visually edit the {phase.ToString().ToLowerInvariant()} phase of a Yanfly Battle Engine action sequence.",
            PluginAuthoringSurface.DatabaseNote,
            new HashSet<PluginDocumentKind> { PluginDocumentKind.Skill, PluginDocumentKind.Item },
            new PluginSyntaxDefinition(
                PluginSyntaxKind.OrderedBlock,
                opening,
                [new($"^<\\s*{tagName}\\s+action\\s*>$", true, true, "YEP Battle Engine action-sequence opening tag.")],
                closing,
                [new($"^<\\s*/\\s*{tagName}\\s+action\\s*>$", true, true, "YEP Battle Engine action-sequence closing tag.")],
                true),
            [],
            PluginFeatureMultiplicity.Singleton,
            ["YEP_BattleEngineCore"],
            [],
            [],
            ActionSequenceEditorId,
            PluginFeatureComplexity.Specialized,
            null,
            "https://www.yanfly.moe/wiki/Battle_Engine_Core_(YEP)",
            ["action sequence", phase.ToString(), "battle animation", "sequence phase"],
            priority);
    }
}

public static class YanflyActionSequenceCommandCatalog
{
    public static IReadOnlyList<ActionSequenceCommandDescriptor> Create(PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Dictionary<string, ActionSequenceCommandDescriptor> commands = new(StringComparer.OrdinalIgnoreCase);
        foreach (PluginFeatureDefinition feature in catalog.Features.Where(feature => feature.Surface == PluginAuthoringSurface.ActionSequence))
        {
            string line = feature.Syntax.CanonicalTemplate.Trim();
            if (line.Length == 0 || line.StartsWith('<')) continue;
            string key = Normalize(line);
            if (commands.ContainsKey(key)) continue;
            string name = ExtractName(line);
            commands[key] = new(
                feature.FeatureId,
                FriendlyName(name),
                line,
                feature.PluginId,
                Classify(name, line),
                feature.EffectiveSearchTerms.Concat([feature.DisplayName, feature.PluginId, name]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                feature.Summary,
                feature.Complexity == PluginFeatureComplexity.AdvancedCode || line.StartsWith("EVAL", StringComparison.OrdinalIgnoreCase));
        }

        foreach (ActionSequenceCommandDescriptor command in CuratedCommands())
            commands[Normalize(command.CanonicalLine)] = command;

        return commands.Values
            .OrderBy(command => command.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.CanonicalLine, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<ActionSequenceCommandDescriptor> CuratedCommands()
    {
        yield return C("display-action", "Display Action", "DISPLAY ACTION", "YEP_X_ActSeqPack1", "Battle Log");
        yield return C("perform-start", "Perform Start", "PERFORM START", "YEP_X_ActSeqPack1", "Motion");
        yield return C("perform-action", "Perform Action", "PERFORM ACTION", "YEP_X_ActSeqPack1", "Motion");
        yield return C("perform-finish", "Perform Finish", "PERFORM FINISH", "YEP_X_ActSeqPack1", "Motion");
        yield return C("action-animation", "Action Animation", "ACTION ANIMATION", "YEP_X_ActSeqPack1", "Animation");
        yield return C("action-effect", "Action Effect", "ACTION EFFECT", "YEP_X_ActSeqPack1", "Damage and Effects");
        yield return C("death-break", "Death Break", "DEATH BREAK", "YEP_X_ActSeqPack1", "Flow");
        yield return C("wait", "Wait Frames", "WAIT: 12", "YEP_X_ActSeqPack1", "Wait");
        yield return C("wait-animation", "Wait for Animation", "WAIT FOR ANIMATION", "YEP_X_ActSeqPack1", "Wait");
        yield return C("wait-movement", "Wait for Movement", "WAIT FOR MOVEMENT", "YEP_X_ActSeqPack1", "Wait");
        yield return C("immortal-on", "Make Targets Immortal", "IMMORTAL: TARGETS, TRUE", "YEP_X_ActSeqPack1", "Battler State");
        yield return C("immortal-off", "Clear Target Immortality", "IMMORTAL: TARGETS, FALSE", "YEP_X_ActSeqPack1", "Battler State");
        yield return C("if", "Conditional IF", "IF user.hpRate() <= 0.5", "YEP_X_ActSeqPack1", "Flow", true);
        yield return C("else", "Conditional ELSE", "ELSE", "YEP_X_ActSeqPack1", "Flow", true);
        yield return C("end", "Conditional END", "END", "YEP_X_ActSeqPack1", "Flow", true);
        yield return C("move-user", "Move User to Target", "MOVE USER: TARGET, FRONT BASE, 20", "YEP_X_ActSeqPack2", "Movement");
        yield return C("motion-attack", "Attack Motion", "MOTION ATTACK: USER", "YEP_X_ActSeqPack2", "Motion");
        yield return C("jump-user", "Jump User", "JUMP USER: 150%, 24", "YEP_X_ActSeqPack2", "Movement");
        yield return C("wait-jump", "Wait for Jump", "WAIT FOR JUMP", "YEP_X_ActSeqPack2", "Wait");
        yield return C("attack-animation", "Attack Animation", "ATTACK ANIMATION: TARGET", "YEP_X_ActSeqPack2", "Animation");
        yield return C("camera-focus", "Camera Focus", "CAMERA FOCUS: TARGET, CENTER, 30", "YEP_X_ActSeqPack3", "Camera");
        yield return C("zoom", "Camera Zoom", "ZOOM: 125%, 30", "YEP_X_ActSeqPack3", "Camera");
        yield return C("reset-camera", "Reset Camera", "RESET CAMERA: 30", "YEP_X_ActSeqPack3", "Camera");
        yield return C("reset-zoom", "Reset Zoom", "RESET ZOOM: 30", "YEP_X_ActSeqPack3", "Camera");
    }

    private static ActionSequenceCommandDescriptor C(string id, string name, string line, string plugin, string category, bool advanced = false) =>
        new("curated." + id, name, line, plugin, category, [name, line, category], null, advanced);

    private static string Normalize(string line) => Regex.Replace(line.Trim().ToUpperInvariant(), "\\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static string ExtractName(string line)
    {
        int colon = line.IndexOf(':');
        return (colon < 0 ? line : line[..colon]).Trim();
    }
    private static string FriendlyName(string name) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    private static string Classify(string name, string line)
    {
        string value = (name + " " + line).ToLowerInvariant();
        if (value.Contains("camera") || value.Contains("zoom")) return "Camera";
        if (value.Contains("move") || value.Contains("jump") || value.Contains("float")) return "Movement";
        if (value.Contains("motion") || value.Contains("perform")) return "Motion";
        if (value.Contains("animation")) return "Animation";
        if (value.Contains("wait")) return "Wait";
        if (value.Contains("effect") || value.Contains("damage") || value.Contains("state") || value.Contains("buff")) return "Damage and Effects";
        if (value.StartsWith("if") || value == "else" || value == "end" || value.Contains("break")) return "Flow";
        if (value.Contains("log") || value.Contains("display") || value.Contains("message")) return "Battle Log";
        if (value.Contains("screen") || value.Contains("flash") || value.Contains("shake") || value.Contains("tint")) return "Screen";
        if (value.Contains("audio") || value.Contains("bgm") || value.Contains("bgs") || value.Contains("se")) return "Audio";
        return "General";
    }
}

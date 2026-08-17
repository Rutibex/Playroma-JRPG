using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflySkillProgressionFeatureDefinitions
{
    public const string SkillDesignerEditorId = "yep.skill-designer";
    public const string ProgressionDesignerEditorId = "yep.progression-designer";

    private static readonly IReadOnlyDictionary<string, SkillProgressionArea> PluginAreas =
        new Dictionary<string, SkillProgressionArea>(StringComparer.OrdinalIgnoreCase)
        {
            ["YEP_SkillCore"] = SkillProgressionArea.Execution,
            ["YEP_X_SkillCooldowns"] = SkillProgressionArea.Cooldowns,
            ["YEP_X_LimitedSkillUses"] = SkillProgressionArea.LimitedUses,
            ["YEP_SkillMasteryLevels"] = SkillProgressionArea.Mastery,
            ["YEP_X_SkillCostItems"] = SkillProgressionArea.Costs,
            ["YEP_Z_SkillRewards"] = SkillProgressionArea.Rewards,
            ["YEP_MultiTypeSkills"] = SkillProgressionArea.SkillTypes,
            ["YEP_InstantCast"] = SkillProgressionArea.InstantCast,
            ["YEP_X_PartyLimitGauge"] = SkillProgressionArea.PartyLimit,
            ["YEP_SkillLearnSystem"] = SkillProgressionArea.Learning,
            ["YEP_JobPoints"] = SkillProgressionArea.JobPoints,
            ["YEP_ClassChangeCore"] = SkillProgressionArea.Classes,
            ["YEP_X_Subclass"] = SkillProgressionArea.Subclasses,
            ["YEP_StatAllocation"] = SkillProgressionArea.StatAllocation,
            ["YEP_EnhancedTP"] = SkillProgressionArea.TpModes,
            ["YEP_EquipBattleSkills"] = SkillProgressionArea.EquippedSkills,
            ["YEP_X_EquipSkillTiers"] = SkillProgressionArea.EquippedSkills,
            ["YEP_LevelUpGrowthEffects"] = SkillProgressionArea.LevelGrowth
        };

    private static readonly HashSet<string> SkillPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_SkillCore", "YEP_X_SkillCooldowns", "YEP_X_LimitedSkillUses", "YEP_SkillMasteryLevels",
        "YEP_X_SkillCostItems", "YEP_Z_SkillRewards", "YEP_MultiTypeSkills", "YEP_InstantCast",
        "YEP_X_PartyLimitGauge"
    };

    public static IReadOnlySet<string> CoveredPluginIds { get; } = PluginAreas.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<PluginDocumentKind> DatabaseKinds =
    [
        PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Skill, PluginDocumentKind.Item,
        PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.Enemy, PluginDocumentKind.State
    ];

    public static IEnumerable<PluginFeatureDefinition> ApplyCorrections(IEnumerable<PluginFeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (PluginFeatureDefinition feature in features)
        {
            if (!PluginAreas.ContainsKey(feature.PluginId))
            {
                yield return feature;
                continue;
            }

            if (feature.Surface == PluginAuthoringSurface.DatabaseNote)
            {
                yield return Route(feature);
                continue;
            }

            // Phase 2 conservatively classified mixed Event/Database headings from these two plugins
            // as EventNote. Retain the event feature and add a database-surface definition for the
            // database kinds explicitly named by the supplied plugin help and runtime parsers.
            yield return feature;
            if (feature.Surface == PluginAuthoringSurface.EventNote &&
                (feature.PluginId.Equals("YEP_InstantCast", StringComparison.OrdinalIgnoreCase) ||
                 feature.PluginId.Equals("YEP_X_Subclass", StringComparison.OrdinalIgnoreCase)))
            {
                IReadOnlySet<PluginDocumentKind> kinds = feature.DocumentKinds.Where(DatabaseKinds.Contains).ToHashSet();
                if (kinds.Count > 0)
                {
                    yield return Route(feature with
                    {
                        FeatureId = feature.FeatureId + ".database",
                        Surface = PluginAuthoringSurface.DatabaseNote,
                        DocumentKinds = kinds,
                        MatchPriority = feature.MatchPriority + 10
                    });
                }
            }
        }
    }

    private static PluginFeatureDefinition Route(PluginFeatureDefinition feature)
    {
        string editorId = SkillPlugins.Contains(feature.PluginId) ? SkillDesignerEditorId : ProgressionDesignerEditorId;
        SkillProgressionArea area = Classify(feature) ?? PluginAreas[feature.PluginId];
        return feature with
        {
            SpecializedEditorId = editorId,
            SearchTerms = feature.EffectiveSearchTerms
                .Concat(["skill progression", FriendlyArea(area), editorId == SkillDesignerEditorId ? "skill designer" : "progression designer"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public static SkillProgressionArea? Classify(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!PluginAreas.TryGetValue(feature.PluginId, out SkillProgressionArea fallback)) return null;
        if (!SkillPlugins.Contains(feature.PluginId))
            return feature.Complexity == PluginFeatureComplexity.AdvancedCode ? SkillProgressionArea.Advanced : fallback;
        string text = string.Join(" ", feature.DisplayName, feature.Summary, feature.Syntax.CanonicalTemplate,
            string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();
        if (text.Contains("cost") || text.Contains("require"))
            return text.Contains("require") ? SkillProgressionArea.Requirements : SkillProgressionArea.Costs;
        if (text.Contains("cooldown") || text.Contains("warmup")) return SkillProgressionArea.Cooldowns;
        if (text.Contains("limited use") || text.Contains("use max") || text.Contains("uses recover")) return SkillProgressionArea.LimitedUses;
        if (text.Contains("mastery")) return SkillProgressionArea.Mastery;
        if (text.Contains("reward")) return SkillProgressionArea.Rewards;
        if (text.Contains("skill type") || text.Contains("stype")) return SkillProgressionArea.SkillTypes;
        if (text.Contains("instant")) return SkillProgressionArea.InstantCast;
        if (text.Contains("party limit") || text.Contains("troop limit")) return SkillProgressionArea.PartyLimit;
        if (text.Contains("learn")) return SkillProgressionArea.Learning;
        if (text.Contains("job point") || text.Contains("starting jp") || text.Contains("jp ") || text.Contains("<jp")) return SkillProgressionArea.JobPoints;
        if (text.Contains("subclass")) return SkillProgressionArea.Subclasses;
        if (text.Contains("stat allocation")) return SkillProgressionArea.StatAllocation;
        if (text.Contains("tp mode")) return SkillProgressionArea.TpModes;
        if (text.Contains("equip skill") || text.Contains("skill tier")) return SkillProgressionArea.EquippedSkills;
        if (text.Contains("level up")) return SkillProgressionArea.LevelGrowth;
        if (feature.Complexity == PluginFeatureComplexity.AdvancedCode) return SkillProgressionArea.Advanced;
        return fallback;
    }

    public static void RegisterSpecializedEditors(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlySet<PluginFeatureKey> skill = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        IReadOnlySet<PluginFeatureKey> progression = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        registry.Register(new(SkillDesignerEditorId, "Skill Designer", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote }, skill, true,
            "Unified skill costs, requirements, execution, cooldowns, uses, mastery, rewards, types, instant cast and party-limit authoring."), replace: true);
        registry.Register(new(ProgressionDesignerEditorId, "Progression Designer", new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote }, progression, true,
            "Skill learning, job points, class/subclass rules, stat allocation, TP modes, equipped skills and level-growth authoring."), replace: true);
    }

    public static IReadOnlyList<SkillProgressionPreset> CreatePresets(PluginTagCatalog catalog, PluginDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        List<SkillProgressionPreset> result = [];
        AddPreset(result, catalog, kind, "cooldown-technique", "Cooldown Technique",
            "Give a skill a three-turn cooldown.", [("YEP_X_SkillCooldowns", "<Cooldown:", ["3"])]);
        AddPreset(result, catalog, kind, "instant-technique", "Instant Technique",
            "Make a skill execute without consuming the normal action timing.", [("YEP_InstantCast", "<Instant Cast>", [])]);
        AddPreset(result, catalog, kind, "mastered-signature", "Mastered Signature Skill",
            "Enable ten mastery levels for a signature ability.", [("YEP_SkillMasteryLevels", "<Max Mastery Level:", ["10"])]);
        AddPreset(result, catalog, kind, "jp-learnable", "JP-Learnable Skill",
            "Require level 5 and 100 JP to learn the skill.",
            [("YEP_SkillLearnSystem", "<Learn Require Level:", ["5"]), ("YEP_SkillLearnSystem", " JP>", ["100"])]);
        AddPreset(result, catalog, kind, "multi-type-skill", "Multi-Type Skill",
            "Place a skill in three skill-type menus.", [("YEP_MultiTypeSkills", "<Skill Types:", ["1", "2", "3"])]);
        AddPreset(result, catalog, kind, "four-equipped-slots", "Four Extra Equipped Skills",
            "Increase the battler's equipped battle-skill capacity by four.", [("YEP_EquipBattleSkills", "<Equip Skill Slots: +", ["4"])]);
        return result;
    }

    private static void AddPreset(
        List<SkillProgressionPreset> result,
        PluginTagCatalog catalog,
        PluginDocumentKind kind,
        string id,
        string name,
        string description,
        IReadOnlyList<(string PluginId, string SyntaxContains, string[] Values)> selectors)
    {
        List<SkillProgressionPresetStep> steps = [];
        foreach ((string pluginId, string syntaxContains, string[] values) in selectors)
        {
            PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
                candidate.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Surface == PluginAuthoringSurface.DatabaseNote &&
                candidate.Syntax.CanonicalTemplate.Contains(syntaxContains, StringComparison.OrdinalIgnoreCase) &&
                (candidate.DocumentKinds.Count == 0 || candidate.DocumentKinds.Contains(PluginDocumentKind.Unknown) || candidate.DocumentKinds.Contains(kind)));
            if (feature is null) return;
            List<KeyValuePair<string, string>> arguments = [];
            for (int index = 0; index < feature.Arguments.Count; index++)
                arguments.Add(new(feature.Arguments[index].Name, index < values.Length ? values[index] : feature.Arguments[index].DefaultValue ?? string.Empty));
            steps.Add(new(feature.Key, PluginFeatureValues.FromStrings(arguments)));
        }
        result.Add(new(id, name, description, new HashSet<PluginDocumentKind> { kind }, steps,
            [name, description, "Yanfly", "skill progression"]));
    }

    public static string FriendlyArea(SkillProgressionArea area) => area switch
    {
        SkillProgressionArea.LimitedUses => "Limited Uses",
        SkillProgressionArea.SkillTypes => "Skill Types",
        SkillProgressionArea.InstantCast => "Instant Cast",
        SkillProgressionArea.PartyLimit => "Party Limit",
        SkillProgressionArea.JobPoints => "Job Points",
        SkillProgressionArea.StatAllocation => "Stat Allocation",
        SkillProgressionArea.TpModes => "TP Modes",
        SkillProgressionArea.EquippedSkills => "Equipped Skills",
        SkillProgressionArea.LevelGrowth => "Level Growth",
        _ => area.ToString()
    };
}

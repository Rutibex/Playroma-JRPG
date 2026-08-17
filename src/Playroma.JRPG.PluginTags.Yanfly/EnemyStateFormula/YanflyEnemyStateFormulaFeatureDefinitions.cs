using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyEnemyStateFormulaFeatureDefinitions
{
    public const string EnemyDesignerEditorId = "yep.enemy-designer";
    public const string StateLifecycleEditorId = "yep.state-lifecycle";
    public const string PassiveAuraEditorId = "yep.passive-state-aura";
    public const string FormulaLabEditorId = "yep.formula-lab";

    private static readonly IReadOnlyDictionary<string, (string EditorId, EnemyStateFormulaArea Area)> PluginRoutes =
        new Dictionary<string, (string, EnemyStateFormulaArea)>(StringComparer.OrdinalIgnoreCase)
        {
            ["KELYEP_DragonBones"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyAnimation),
            ["YEP_BattleBgmControl"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyAudio),
            ["YEP_EnemyLevels"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyLevels),
            ["YEP_ExtraEnemyDrops"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyRewards),
            ["YEP_HitDamageSounds"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyAudio),
            ["YEP_OverkillBonus"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyRewards),
            ["YEP_RowFormation"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyFormation),
            ["YEP_SwapEnemies"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyBehavior),
            ["YEP_Taunt"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyBehavior),
            ["YEP_X_AnimatedSVEnemies"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyAppearance),
            ["YEP_X_DifficultySlider"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyLevels),
            ["YEP_X_VisualATBGauge"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyGauges),
            ["YEP_X_VisualHpGauge"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyGauges),
            ["YEP_X_WeakEnemyPoses"] = (EnemyDesignerEditorId, EnemyStateFormulaArea.EnemyAppearance),

            ["YEP_BuffsStatesCore"] = (StateLifecycleEditorId, EnemyStateFormulaArea.StateLifecycle),
            ["YEP_X_ExtDoT"] = (StateLifecycleEditorId, EnemyStateFormulaArea.DamageOverTime),
            ["YEP_X_StateCategories"] = (StateLifecycleEditorId, EnemyStateFormulaArea.StateCategories),
            ["YEP_X_VisualStateFX"] = (StateLifecycleEditorId, EnemyStateFormulaArea.StateVisuals),
            ["YEP_Z_StateProtection"] = (StateLifecycleEditorId, EnemyStateFormulaArea.StateProtection),

            ["YEP_AutoPassiveStates"] = (PassiveAuraEditorId, EnemyStateFormulaArea.PassiveStates),
            ["YEP_X_PassiveAuras"] = (PassiveAuraEditorId, EnemyStateFormulaArea.Auras),
            ["YEP_Z_PassiveCases"] = (PassiveAuraEditorId, EnemyStateFormulaArea.AuraConditions),

            ["YEP_AbsorptionBarrier"] = (FormulaLabEditorId, EnemyStateFormulaArea.Barriers),
            ["YEP_BaseParamControl"] = (FormulaLabEditorId, EnemyStateFormulaArea.BaseParameters),
            ["YEP_DamageCore"] = (FormulaLabEditorId, EnemyStateFormulaArea.DamageRules),
            ["YEP_ElementAbsorb"] = (FormulaLabEditorId, EnemyStateFormulaArea.Elements),
            ["YEP_ElementCore"] = (FormulaLabEditorId, EnemyStateFormulaArea.Elements),
            ["YEP_ElementReflect"] = (FormulaLabEditorId, EnemyStateFormulaArea.Elements),
            ["YEP_ExtraParamFormula"] = (FormulaLabEditorId, EnemyStateFormulaArea.ExtraParameters),
            ["YEP_SpecialParamFormula"] = (FormulaLabEditorId, EnemyStateFormulaArea.SpecialParameters),
            ["YEP_X_ClassBaseParam"] = (FormulaLabEditorId, EnemyStateFormulaArea.BaseParameters),
            ["YEP_X_CriticalControl"] = (FormulaLabEditorId, EnemyStateFormulaArea.Criticals),
            ["YEP_X_EnemyBaseParam"] = (FormulaLabEditorId, EnemyStateFormulaArea.BaseParameters),
            ["YEP_Z_CriticalSway"] = (FormulaLabEditorId, EnemyStateFormulaArea.Criticals)
        };

    public static IReadOnlySet<string> CoveredPluginIds { get; } =
        PluginRoutes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> EnemyPluginIds { get; } = PluginRoutes
        .Where(pair => pair.Value.EditorId == EnemyDesignerEditorId)
        .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> StatePluginIds { get; } = PluginRoutes
        .Where(pair => pair.Value.EditorId == StateLifecycleEditorId)
        .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> PassiveAuraPluginIds { get; } = PluginRoutes
        .Where(pair => pair.Value.EditorId == PassiveAuraEditorId)
        .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> FormulaPluginIds { get; } = PluginRoutes
        .Where(pair => pair.Value.EditorId == FormulaLabEditorId)
        .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<PluginFeatureDefinition> ApplyCorrections(IEnumerable<PluginFeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (PluginFeatureDefinition feature in features)
        {
            if (feature.Surface != PluginAuthoringSurface.DatabaseNote ||
                !PluginRoutes.TryGetValue(feature.PluginId, out (string EditorId, EnemyStateFormulaArea Area) route))
            {
                yield return feature;
                continue;
            }

            EnemyStateFormulaArea area = Classify(feature) ?? route.Area;
            yield return feature with
            {
                SpecializedEditorId = route.EditorId,
                SearchTerms = feature.EffectiveSearchTerms
                    .Concat(["enemies states auras formulas", FriendlyArea(area), FriendlyEditor(route.EditorId), "phase 9"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }

    public static EnemyStateFormulaArea? Classify(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!PluginRoutes.TryGetValue(feature.PluginId, out (string EditorId, EnemyStateFormulaArea Area) route)) return null;
        string text = string.Join(" ", feature.DisplayName, feature.Summary, feature.Syntax.CanonicalTemplate,
            string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();

        if (feature.Complexity == PluginFeatureComplexity.AdvancedCode) return EnemyStateFormulaArea.Advanced;

        if (route.EditorId == EnemyDesignerEditorId)
        {
            if (text.Contains("level") || text.Contains("difficulty")) return EnemyStateFormulaArea.EnemyLevels;
            if (text.Contains("drop") || text.Contains("reward") || text.Contains("overkill")) return EnemyStateFormulaArea.EnemyRewards;
            if (text.Contains("gauge") || text.Contains("atb")) return EnemyStateFormulaArea.EnemyGauges;
            if (text.Contains("bgm") || text.Contains("sound")) return EnemyStateFormulaArea.EnemyAudio;
            if (text.Contains("row") || text.Contains("formation")) return EnemyStateFormulaArea.EnemyFormation;
            if (text.Contains("swap") || text.Contains("taunt")) return EnemyStateFormulaArea.EnemyBehavior;
            if (text.Contains("dragonbone") || text.Contains("animation") || text.Contains("motion")) return EnemyStateFormulaArea.EnemyAnimation;
            if (text.Contains("sideview") || text.Contains("battler") || text.Contains("breathing") ||
                text.Contains("floating") || text.Contains("weak pose") || text.Contains("collapse"))
                return EnemyStateFormulaArea.EnemyAppearance;
            return route.Area;
        }

        if (route.EditorId == StateLifecycleEditorId)
        {
            if (text.Contains("dot") || text.Contains("regen")) return EnemyStateFormulaArea.DamageOverTime;
            if (text.Contains("category")) return EnemyStateFormulaArea.StateCategories;
            if (text.Contains("protect") || text.Contains("bypass")) return EnemyStateFormulaArea.StateProtection;
            if (text.Contains("motion") || text.Contains("overlay") || text.Contains("animation") || text.Contains("icon"))
                return EnemyStateFormulaArea.StateVisuals;
            if (text.Contains("counter")) return EnemyStateFormulaArea.StateCounters;
            if (text.Contains("turn") || text.Contains("tick")) return EnemyStateFormulaArea.StateTiming;
            return EnemyStateFormulaArea.StateLifecycle;
        }

        if (route.EditorId == PassiveAuraEditorId)
        {
            if (feature.PluginId.Equals("YEP_AutoPassiveStates", StringComparison.OrdinalIgnoreCase))
                return EnemyStateFormulaArea.PassiveStates;
            if (feature.PluginId.Equals("YEP_Z_PassiveCases", StringComparison.OrdinalIgnoreCase) || text.Contains("condition"))
                return EnemyStateFormulaArea.AuraConditions;
            return EnemyStateFormulaArea.Auras;
        }

        if (text.Contains("barrier")) return EnemyStateFormulaArea.Barriers;
        if (text.Contains("critical")) return EnemyStateFormulaArea.Criticals;
        if (text.Contains("element")) return EnemyStateFormulaArea.Elements;
        if (feature.PluginId.Equals("YEP_ExtraParamFormula", StringComparison.OrdinalIgnoreCase)) return EnemyStateFormulaArea.ExtraParameters;
        if (feature.PluginId.Equals("YEP_SpecialParamFormula", StringComparison.OrdinalIgnoreCase)) return EnemyStateFormulaArea.SpecialParameters;
        if (feature.PluginId.Equals("YEP_DamageCore", StringComparison.OrdinalIgnoreCase)) return EnemyStateFormulaArea.DamageRules;
        return EnemyStateFormulaArea.BaseParameters;
    }

    public static void RegisterSpecializedEditors(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        Register(registry, catalog, EnemyDesignerEditorId, "Enemy Designer",
            "Enemy levels, drops, sideview and DragonBones appearance, gauges, sounds, rows, taunts and battle behavior.");
        Register(registry, catalog, StateLifecycleEditorId, "State Lifecycle",
            "State timing, lifecycle scripts, counters, damage over time, categories, protection and visual effects.");
        Register(registry, catalog, PassiveAuraEditorId, "Passive States & Auras",
            "Automatic passive states, party/troop/ally/foe auras and advanced passive conditions.");
        Register(registry, catalog, FormulaLabEditorId, "Formula Lab",
            "Barriers, base/extra/special parameters, damage caps, elements and critical formulas.");
    }

    private static void Register(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog,
        string editorId, string displayName, string description)
    {
        IReadOnlySet<PluginFeatureKey> supported = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        registry.Register(new(editorId, displayName, new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote }, supported, true, description), replace: true);
    }

    public static IReadOnlyList<EnemyStateFormulaPreset> CreatePresets(
        PluginTagCatalog catalog, PluginDocumentKind kind, string editorId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        List<EnemyStateFormulaPreset> result = [];

        if (editorId == EnemyDesignerEditorId)
        {
            AddPreset(result, catalog, kind, editorId, "scaled-enemy", "Scaled Enemy",
                "Give an enemy a level range from 1 to 99.",
                [("YEP_EnemyLevels", "<Minimum Level:", ["1"]), ("YEP_EnemyLevels", "<Maximum Level:", ["99"])]);
            AddPreset(result, catalog, kind, editorId, "visible-boss-gauge", "Visible Boss Gauge",
                "Force a visible 480-pixel HP gauge for a major enemy.",
                [("YEP_X_VisualHpGauge", "<Show HP Gauge>", []), ("YEP_X_VisualHpGauge", "<HP Gauge Width:", ["480"])]);
            AddPreset(result, catalog, kind, editorId, "floating-enemy", "Floating Enemy",
                "Enable floating and floating death animation behavior.",
                [("YEP_X_AnimatedSVEnemies", "<Floating>", []), ("YEP_X_AnimatedSVEnemies", "<Floating Death>", [])]);
        }
        else if (editorId == StateLifecycleEditorId)
        {
            AddPreset(result, catalog, kind, editorId, "damage-over-time", "Damage Over Time",
                "Apply a simple 100-damage periodic effect with animation 59.",
                [("YEP_X_ExtDoT", "<DoT Formula: 100>", []), ("YEP_X_ExtDoT", "<DoT Animation: 59>", [])]);
            AddPreset(result, catalog, kind, editorId, "abnormal-motion", "Abnormal State Motion",
                "Display the abnormal sideview motion while the state is active.",
                [("YEP_X_VisualStateFX", "<State Motion: Abnormal>", [])]);
            AddPreset(result, catalog, kind, editorId, "protected-state", "Protected State",
                "Add a state-protection rule through the typed protection catalog.",
                [("YEP_Z_StateProtection", "<HP Protection: Damage Null 20%>", [])]);
        }
        else if (editorId == PassiveAuraEditorId)
        {
            AddPreset(result, catalog, kind, editorId, "passive-state", "Passive State",
                "Grant state 4 automatically while this object is active.",
                [("YEP_AutoPassiveStates", "<Passive State: {stateId}>", ["4"])]);
            AddPreset(result, catalog, kind, editorId, "ally-aura", "Living Ally Aura",
                "Grant state 4 to living allies.",
                [("YEP_X_PassiveAuras", "<Alive Ally Aura: {value1}>", ["4"])]);
            AddPreset(result, catalog, kind, editorId, "foe-aura", "Foe Aura",
                "Grant state 5 to foes while the aura source is active.",
                [("YEP_X_PassiveAuras", "<Foe Aura: {value1}>", ["5"])]);
        }
        else if (editorId == FormulaLabEditorId)
        {
            AddPreset(result, catalog, kind, editorId, "barrier-100", "100 Barrier Points",
                "Grant 100 absorption barrier points.",
                [("YEP_AbsorptionBarrier", "<Barrier Points: +", ["100"])]);
            AddPreset(result, catalog, kind, editorId, "damage-cap", "9,999 Damage Cap",
                "Set the damage cap to 9,999.",
                [("YEP_DamageCore", "<Damage Cap:", ["9999"])]);
            AddPreset(result, catalog, kind, editorId, "ten-percent-parameter", "110% Parameter Rate",
                "Set a selected base parameter rate to 110 percent.",
                [("YEP_BaseParamControl", " Rate:", ["atk", "110"])]);
        }

        return result;
    }

    private static void AddPreset(List<EnemyStateFormulaPreset> result, PluginTagCatalog catalog,
        PluginDocumentKind kind, string editorId, string id, string name, string description,
        IReadOnlyList<(string PluginId, string SyntaxContains, string[] Values)> selectors)
    {
        List<EnemyStateFormulaPresetStep> steps = [];
        foreach ((string pluginId, string syntaxContains, string[] values) in selectors)
        {
            PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
                candidate.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Surface == PluginAuthoringSurface.DatabaseNote &&
                candidate.SpecializedEditorId?.Equals(editorId, StringComparison.OrdinalIgnoreCase) == true &&
                candidate.Syntax.CanonicalTemplate.Contains(syntaxContains, StringComparison.OrdinalIgnoreCase) &&
                (candidate.DocumentKinds.Count == 0 || candidate.DocumentKinds.Contains(PluginDocumentKind.Unknown) || candidate.DocumentKinds.Contains(kind)));
            if (feature is null) return;

            List<KeyValuePair<string, string>> arguments = [];
            for (int index = 0; index < feature.Arguments.Count; index++)
                arguments.Add(new(feature.Arguments[index].Name,
                    index < values.Length ? values[index] : feature.Arguments[index].DefaultValue ?? string.Empty));
            steps.Add(new(feature.Key, PluginFeatureValues.FromStrings(arguments)));
        }

        result.Add(new(id, name, description, editorId, new HashSet<PluginDocumentKind> { kind }, steps,
            [name, description, FriendlyEditor(editorId), "Yanfly"]));
    }

    public static string FriendlyEditor(string editorId) => editorId switch
    {
        EnemyDesignerEditorId => "Enemy Designer",
        StateLifecycleEditorId => "State Lifecycle",
        PassiveAuraEditorId => "Passive States & Auras",
        FormulaLabEditorId => "Formula Lab",
        _ => "Enemy / State Designer"
    };

    public static string FriendlyArea(EnemyStateFormulaArea area) => area switch
    {
        EnemyStateFormulaArea.EnemyLevels => "Enemy Levels",
        EnemyStateFormulaArea.EnemyRewards => "Drops & Rewards",
        EnemyStateFormulaArea.EnemyAppearance => "Enemy Appearance",
        EnemyStateFormulaArea.EnemyAnimation => "Enemy Animation",
        EnemyStateFormulaArea.EnemyGauges => "Enemy Gauges",
        EnemyStateFormulaArea.EnemyAudio => "Enemy Audio",
        EnemyStateFormulaArea.EnemyFormation => "Rows & Formation",
        EnemyStateFormulaArea.EnemyBehavior => "Enemy Behavior",
        EnemyStateFormulaArea.StateTiming => "State Timing",
        EnemyStateFormulaArea.StateLifecycle => "State Lifecycle",
        EnemyStateFormulaArea.StateCounters => "State Counters",
        EnemyStateFormulaArea.DamageOverTime => "Damage Over Time",
        EnemyStateFormulaArea.StateCategories => "State Categories",
        EnemyStateFormulaArea.StateProtection => "State Protection",
        EnemyStateFormulaArea.StateVisuals => "State Visuals",
        EnemyStateFormulaArea.PassiveStates => "Passive States",
        EnemyStateFormulaArea.AuraConditions => "Aura Conditions",
        EnemyStateFormulaArea.BaseParameters => "Base Parameters",
        EnemyStateFormulaArea.ExtraParameters => "Extra Parameters",
        EnemyStateFormulaArea.SpecialParameters => "Special Parameters",
        EnemyStateFormulaArea.DamageRules => "Damage Rules",
        _ => area.ToString()
    };
}

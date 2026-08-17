using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal static class PluginSpecializedEditorRouter
{
    public static bool CanHandle(PluginFeatureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.SpecializedEditorId?.Equals(YanflyBattleFeatureDefinitions.BattleAiEditorId, StringComparison.OrdinalIgnoreCase) == true)
            return definition.PluginId.Equals("YEP_BattleAICore", StringComparison.OrdinalIgnoreCase);
        if (definition.SpecializedEditorId?.Equals(YanflyBattleFeatureDefinitions.ActionSequenceEditorId, StringComparison.OrdinalIgnoreCase) == true)
            return definition.FeatureId.StartsWith("action-sequence.phase.", StringComparison.OrdinalIgnoreCase) ||
                   definition.Syntax.CanonicalTemplate.StartsWith("<action copy:", StringComparison.OrdinalIgnoreCase);
        if (definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ||
            definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
            return YanflySkillProgressionFeatureDefinitions.CoveredPluginIds.Contains(definition.PluginId);
        if (definition.SpecializedEditorId?.Equals(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
            return YanflyItemCommerceFeatureDefinitions.CoveredPluginIds.Contains(definition.PluginId);
        if (definition.SpecializedEditorId is string phase9Editor &&
            (phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, StringComparison.OrdinalIgnoreCase)))
            return YanflyEnemyStateFormulaFeatureDefinitions.CoveredPluginIds.Contains(definition.PluginId);
        if (definition.SpecializedEditorId is string phase10Editor &&
            (phase10Editor.Equals(YanflyMapEventFeatureDefinitions.MapTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.RegionRulesEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.TilesetTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.EventTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.EventCommentEditorId, StringComparison.OrdinalIgnoreCase)))
            return YanflyMapEventFeatureDefinitions.IsPhase10Feature(definition);
        return false;
    }

    public static string EditorLabel(PluginFeatureDefinition definition)
    {
        if (!CanHandle(definition)) return PluginGenericEditorCoverage.CreatePlan(definition).RequiresSpecializedEditor ? "Planned" :
            definition.Complexity == PluginFeatureComplexity.AdvancedCode ? "Code" : "Typed";
        if (definition.SpecializedEditorId?.Equals(YanflyBattleFeatureDefinitions.BattleAiEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Battle AI";
        if (definition.SpecializedEditorId?.Equals(YanflyBattleFeatureDefinitions.ActionSequenceEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Sequence";
        if (definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Skill";
        if (definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Progression";
        if (definition.SpecializedEditorId?.Equals(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Items & Shop";
        if (definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Enemy";
        if (definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) == true) return "State";
        if (definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Aura";
        if (definition.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.MapTagsEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Map";
        if (definition.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.RegionRulesEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Regions";
        if (definition.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.TilesetTagsEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Tileset";
        if (definition.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.EventTagsEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Event";
        if (definition.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.EventCommentEditorId, StringComparison.OrdinalIgnoreCase) == true) return "Comments";
        return "Formula";
    }

    public static bool TryEdit(
        IWin32Window owner,
        MvProjectSession projectSession,
        PluginTagCatalog catalog,
        PluginFeatureDefinition definition,
        PluginTagContext context,
        string currentNotes,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability,
        out string resultNotes)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(projectSession);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        resultNotes = currentNotes;
        if (!CanHandle(definition)) return false;

        if (definition.SpecializedEditorId?.Equals(YanflyBattleFeatureDefinitions.BattleAiEditorId, StringComparison.OrdinalIgnoreCase) == true)
        {
            using BattleAiPriorityEditorDialog dialog = new(projectSession, currentNotes);
            if (dialog.ShowDialog(owner) != DialogResult.OK) return true;
            resultNotes = dialog.ResultNotes;
            return true;
        }


        bool isSkillDesigner = definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true;
        bool isProgressionDesigner = definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true;
        if (isSkillDesigner || isProgressionDesigner)
        {
            using SkillProgressionDesignerDialog designer = new(projectSession, catalog, context, currentNotes, availability, isProgressionDesigner);
            if (designer.ShowDialog(owner) != DialogResult.OK) return true;
            resultNotes = designer.ResultNotes;
            return true;
        }

        if (definition.SpecializedEditorId?.Equals(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
        {
            using ItemCommerceDesignerDialog designer = new(projectSession, catalog, context, currentNotes, availability);
            if (designer.ShowDialog(owner) != DialogResult.OK) return true;
            resultNotes = designer.ResultNotes;
            return true;
        }

        if (definition.SpecializedEditorId is string phase9Editor &&
            (phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase9Editor.Equals(YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, StringComparison.OrdinalIgnoreCase)))
        {
            using EnemyStateFormulaDesignerDialog designer = new(projectSession, catalog, context, currentNotes, availability, phase9Editor);
            if (designer.ShowDialog(owner) != DialogResult.OK) return true;
            resultNotes = designer.ResultNotes;
            return true;
        }

        if (definition.SpecializedEditorId is string phase10Editor &&
            (phase10Editor.Equals(YanflyMapEventFeatureDefinitions.MapTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.RegionRulesEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.TilesetTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.EventTagsEditorId, StringComparison.OrdinalIgnoreCase) ||
             phase10Editor.Equals(YanflyMapEventFeatureDefinitions.EventCommentEditorId, StringComparison.OrdinalIgnoreCase)))
        {
            using MapEventFeatureDesignerDialog designer = new(projectSession, catalog, context, currentNotes, availability, phase10Editor);
            if (designer.ShowDialog(owner) != DialogResult.OK) return true;
            resultNotes = designer.ResultText;
            return true;
        }

        using ActionSequenceEditorDialog sequence = new(projectSession, catalog, currentNotes, availability);
        if (sequence.ShowDialog(owner) != DialogResult.OK) return true;
        resultNotes = sequence.ResultNotes;
        return true;
    }
}

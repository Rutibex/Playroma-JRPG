namespace Playroma.JRPG.PluginTags;

public sealed record PluginTagRecipeStep(
    PluginFeatureKey Feature,
    PluginFeatureValues SuggestedValues,
    bool IsOptional = false,
    string? Guidance = null);

public sealed record PluginTagRecipeDefinition(
    string RecipeId,
    string DisplayName,
    string Summary,
    IReadOnlySet<PluginDocumentKind> DocumentKinds,
    IReadOnlyList<PluginTagRecipeStep> Steps,
    IReadOnlyList<string>? SearchTerms = null)
{
    public IReadOnlyList<string> EffectiveSearchTerms => SearchTerms ?? [];
}

public sealed record PluginTagRecipePreview(
    PluginTagRecipeDefinition Recipe,
    string Before,
    string After,
    IReadOnlyList<PluginTagLineChange> Lines,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => !string.Equals(Before, After, StringComparison.Ordinal);
}

public sealed class PluginTagRecipeCatalog
{
    private readonly PluginTagCatalog catalog;
    private readonly IReadOnlyList<PluginTagRecipeDefinition> recipes;

    public PluginTagRecipeCatalog(PluginTagCatalog catalog, IEnumerable<PluginTagRecipeDefinition> recipes)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.recipes = recipes?.ToArray() ?? throw new ArgumentNullException(nameof(recipes));
        foreach (PluginTagRecipeDefinition recipe in this.recipes)
        foreach (PluginTagRecipeStep step in recipe.Steps)
            _ = catalog.GetRequiredFeature(step.Feature);
    }

    public IReadOnlyList<PluginTagRecipeDefinition> Query(PluginTagContext context, string? searchText = null) => recipes
        .Where(recipe => recipe.DocumentKinds.Count == 0 || recipe.DocumentKinds.Contains(context.DocumentKind))
        .Where(recipe => string.IsNullOrWhiteSpace(searchText) ||
            recipe.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            recipe.Summary.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            recipe.EffectiveSearchTerms.Any(term => term.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(recipe => recipe.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public PluginTagRecipePreview Preview(
        PluginTextEditingSession session,
        PluginTagRecipeDefinition recipe,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureValues>? overrides = null)
    {
        PluginTextEditingSession clone = new(catalog, session.Context, session.WorkingText);
        List<string> warnings = [];
        foreach (PluginTagRecipeStep step in recipe.Steps)
        {
            PluginFeatureDefinition feature = catalog.GetRequiredFeature(step.Feature);
            if (feature.Surface != session.Context.Surface ||
                feature.DocumentKinds.Count > 0 && !feature.DocumentKinds.Contains(PluginDocumentKind.Unknown) && !feature.DocumentKinds.Contains(session.Context.DocumentKind))
            {
                if (!step.IsOptional) warnings.Add(feature.DisplayName + " is not valid for this record type.");
                continue;
            }
            PluginFeatureValues values = overrides is not null && overrides.TryGetValue(step.Feature, out PluginFeatureValues? replacement)
                ? replacement : step.SuggestedValues;
            int count = clone.ParsedDocument.Find(step.Feature).Count;
            int index = feature.Multiplicity == PluginFeatureMultiplicity.Singleton ? 0 : count;
            try { clone.UpsertFeature(step.Feature, values, index); }
            catch (PluginTextMutationException exception)
            {
                if (!step.IsOptional) warnings.Add(exception.Message);
            }
        }
        return new(recipe, session.WorkingText, clone.WorkingText,
            PluginTagTextDiff.Create(session.WorkingText, clone.WorkingText), warnings);
    }
}

/// <summary>Builds licensing-safe, independently-authored beginner recipes from catalog semantics.</summary>
public static class YanflyBeginnerRecipes
{
    public static PluginTagRecipeCatalog Create(PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        List<PluginTagRecipeDefinition> recipes = [];
        AddSingle(recipes, catalog, "barrier-shield", "Barrier Shield", "Give a battler or item a starting absorption barrier.",
            "YEP_AbsorptionBarrier", "<Barrier Points: +{value1}>", null, Suggested("value1", "100"),
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy, PluginDocumentKind.Skill, PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State),
            ["shield", "barrier", "absorption"]);
        AddSingle(recipes, catalog, "bypass-cooldown", "Ignore Skill Cooldowns", "Allow this object to bypass the normal cooldown system.",
            "YEP_X_SkillCooldowns", null, "Bypass Cooldown", PluginFeatureValues.Empty,
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy, PluginDocumentKind.Skill, PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State),
            ["cooldown", "instant"]);
        AddSingle(recipes, catalog, "life-steal-immunity", "Prevent Life Steal", "Cancel HP and MP life-steal effects for this object.",
            "YEP_LifeSteal", null, "Cancel Life Steal", PluginFeatureValues.Empty,
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy, PluginDocumentKind.Skill, PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State),
            ["lifesteal", "immunity"]);
        AddSingle(recipes, catalog, "item-category", "Custom Item Category", "Place an item, weapon, or armor in a named menu category.",
            "YEP_X_ItemCategories", "<Menu Category: {value1}>", null, Suggested("value1", "Crafting"),
            Kinds(PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor), ["menu", "inventory", "category"]);
        AddSingle(recipes, catalog, "state-category", "State Category", "Assign a reusable category name to a state or state operation.",
            "YEP_X_StateCategories", "<Category: {value1}>", null, Suggested("value1", "Poison"),
            Kinds(PluginDocumentKind.State, PluginDocumentKind.Skill, PluginDocumentKind.Item), ["state", "category", "cleanse"]);
        AddSingle(recipes, catalog, "equip-cooldown", "Equipment Change Cooldown", "Add a short cooldown after changing equipment in battle.",
            "YEP_X_ChangeBattleEquip", "<Change Battle Equip Cooldown: +", null, Suggested("value1", "1"),
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State), ["equipment", "cooldown"]);
        AddSingle(recipes, catalog, "hide-enemy-level", "Hide Enemy Level", "Hide the visible enemy level while retaining level scaling.",
            "YEP_EnemyLevels", null, "Hide Level", PluginFeatureValues.Empty, Kinds(PluginDocumentKind.Enemy), ["enemy", "level", "hidden"]);
        AddSingle(recipes, catalog, "no-mastery-cooldown", "Mastery Does Not Change Cooldown", "Keep cooldown unchanged as a skill gains mastery levels.",
            "YEP_SkillMasteryLevels", null, "No Cooldown Mastery Effect", PluginFeatureValues.Empty,
            Kinds(PluginDocumentKind.Skill, PluginDocumentKind.Actor, PluginDocumentKind.Enemy), ["mastery", "cooldown"]);
        AddSingle(recipes, catalog, "no-mastery-hp-cost", "Mastery Does Not Change HP Cost", "Keep HP cost unchanged as a skill gains mastery levels.",
            "YEP_SkillMasteryLevels", null, "No Hp Cost Mastery Effect", PluginFeatureValues.Empty,
            Kinds(PluginDocumentKind.Skill, PluginDocumentKind.Actor, PluginDocumentKind.Enemy), ["mastery", "hp cost"]);
        AddSingle(recipes, catalog, "bypass-durability", "Unbreakable Equipment", "Exclude an item or equipment record from durability loss.",
            "YEP_X_ItemDurability", null, "Bypass Durability", PluginFeatureValues.Empty,
            Kinds(PluginDocumentKind.Item, PluginDocumentKind.Skill, PluginDocumentKind.Weapon, PluginDocumentKind.Armor), ["durability", "unbreakable"]);
        AddSingle(recipes, catalog, "barrier-penetration", "Barrier-Piercing Attack", "Let an action or battler penetrate a percentage of absorption barriers.",
            "YEP_AbsorptionBarrier", "<Barrier Penetration: +{value1}%>", null, Suggested("value1", "25"),
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy, PluginDocumentKind.Skill, PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State), ["pierce", "barrier"]);
        AddSingle(recipes, catalog, "after-battle-cooldown", "Recover Cooldown After Battle", "Reduce remaining cooldowns when battle ends.",
            "YEP_X_SkillCooldowns", "<After Battle Cooldown: -{value1}>", null, Suggested("value1", "1"),
            Kinds(PluginDocumentKind.Actor, PluginDocumentKind.Class, PluginDocumentKind.Enemy, PluginDocumentKind.Skill, PluginDocumentKind.Item, PluginDocumentKind.Weapon, PluginDocumentKind.Armor, PluginDocumentKind.State), ["cooldown", "battle end"]);
        return new(catalog, recipes);
    }

    private static IReadOnlySet<PluginDocumentKind> Kinds(params PluginDocumentKind[] kinds) =>
        new HashSet<PluginDocumentKind>(kinds);

    private static void AddSingle(
        ICollection<PluginTagRecipeDefinition> recipes,
        PluginTagCatalog catalog,
        string id,
        string name,
        string summary,
        string pluginId,
        string? templateContains = null,
        string? displayEquals = null,
        PluginFeatureValues? values = null,
        IReadOnlySet<PluginDocumentKind>? kinds = null,
        IReadOnlyList<string>? terms = null)
    {
        PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
            string.Equals(candidate.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) &&
            (templateContains is null || candidate.Syntax.CanonicalTemplate.Contains(templateContains, StringComparison.OrdinalIgnoreCase)) &&
            (displayEquals is null || string.Equals(candidate.DisplayName, displayEquals, StringComparison.OrdinalIgnoreCase)));
        if (feature is null) return;
        recipes.Add(new(id, name, summary, kinds ?? feature.DocumentKinds,
            [new PluginTagRecipeStep(feature.Key, values ?? PluginFeatureValues.Empty)], terms));
    }

    private static PluginFeatureValues Suggested(string name, string value) =>
        PluginFeatureValues.FromStrings([new KeyValuePair<string, string>(name, value)]);
}

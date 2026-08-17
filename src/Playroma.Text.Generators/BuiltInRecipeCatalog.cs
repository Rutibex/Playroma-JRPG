using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators;

/// <summary>One stable view of all built-in recipes available through Phase 14.</summary>
public static class BuiltInRecipeCatalog
{
    public static IReadOnlyList<TextRecipe> All { get; } = BuiltInPhase1Recipes.All
        .Concat(BuiltInPhase2Recipes.All)
        .Concat(BuiltInPhase3Recipes.All)
        .Concat(BuiltInPhase4Recipes.All)
        .Concat(BuiltInPhase5Recipes.All)
        .Concat(BuiltInPhase6Recipes.All)
        .Concat(BuiltInPhase8Recipes.All)
        .Concat(BuiltInPhase9Recipes.All)
        .Concat(BuiltInPhase10Recipes.All)
        .Concat(BuiltInPhase12Recipes.All)
        .Concat(BuiltInPhase14Recipes.All)
        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Last())
        .OrderBy(x => CategoryFor(x), StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string CategoryFor(TextRecipe recipe) => recipe.Kind switch
    {
        TextContentKind.PersonName => "People",
        TextContentKind.PlaceName or TextContentKind.CountryName => "Places",
        TextContentKind.ObjectName or TextContentKind.Item or TextContentKind.Weapon or TextContentKind.Armor => "Items & Objects",
        TextContentKind.Skill or TextContentKind.Spell => "Magic & Skills",
        TextContentKind.Monster or TextContentKind.Enemy => "Creatures",
        TextContentKind.Faction => "Society",
        TextContentKind.Dialogue => "Dialogue",
        TextContentKind.Quest or TextContentKind.Event or TextContentKind.Rumor => "Narrative",
        TextContentKind.Lore or TextContentKind.Note or TextContentKind.Description => "Lore & Notes",
        _ => "More"
    };
}

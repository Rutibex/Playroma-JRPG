using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

public static class BuiltInPhase2Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } = new TextRecipe[]
    {
        PersonGiven(), PersonFamily(), PersonFull(), Country(), City(), Village(), Region(), River(), Mountain(), Forest(), Dungeon(),
        Deity(), Faction(), NobleHouse(), Monster(), Spell(), Weapon(), Armor(), Artifact(), Language(), SettlementSet(), RegionalNamingKit(), UmbralMonster(), SylvanSettlement(), KharicStronghold(), ValestrianPerson(), NorthreachPerson()
    };

    public static TextRecipe PersonGiven() => N("starter.names.person-given", "Cultural Given Name", TextContentKind.PersonName, "common", "personGiven");
    public static TextRecipe PersonFamily() => N("starter.names.person-family", "Cultural Family Name", TextContentKind.PersonName, "common", "personFamily");
    public static TextRecipe Country() => N("starter.names.country", "Cultural Country Name", TextContentKind.CountryName, "common", "country");
    public static TextRecipe City() => N("starter.names.city", "Cultural City Name", TextContentKind.PlaceName, "common", "city");
    public static TextRecipe Village() => N("starter.names.village", "Cultural Village Name", TextContentKind.PlaceName, "common", "village");
    public static TextRecipe Region() => N("starter.names.region", "Cultural Region Name", TextContentKind.PlaceName, "common", "region");
    public static TextRecipe River() => N("starter.names.river", "Cultural River Name", TextContentKind.PlaceName, "common", "river");
    public static TextRecipe Mountain() => N("starter.names.mountain", "Cultural Mountain Name", TextContentKind.PlaceName, "common", "mountain");
    public static TextRecipe Forest() => N("starter.names.forest", "Cultural Forest Name", TextContentKind.PlaceName, "sylvan", "forest");
    public static TextRecipe Dungeon() => N("starter.names.dungeon", "Cultural Dungeon Name", TextContentKind.PlaceName, "umbral", "dungeon");
    public static TextRecipe Deity() => N("starter.names.deity", "Cultural Deity Name", TextContentKind.PersonName, "common", "deity");
    public static TextRecipe Faction() => N("starter.names.faction", "Cultural Faction Name", TextContentKind.Faction, "common", "faction");
    public static TextRecipe NobleHouse() => N("starter.names.noble-house", "Cultural Noble House", TextContentKind.Faction, "valestrian", "nobleHouse");
    public static TextRecipe Monster() => N("starter.names.monster", "Cultural Monster Name", TextContentKind.Monster, "umbral", "monster");
    public static TextRecipe Spell() => N("starter.names.spell", "Cultural Spell Name", TextContentKind.Spell, "umbral", "spell");
    public static TextRecipe Weapon() => N("starter.names.weapon", "Cultural Weapon Name", TextContentKind.Weapon, "kharic", "weapon");
    public static TextRecipe Armor() => N("starter.names.armor", "Cultural Armor Name", TextContentKind.Armor, "kharic", "armor");
    public static TextRecipe Artifact() => N("starter.names.artifact", "Cultural Artifact Name", TextContentKind.ObjectName, "umbral", "artifact");
    public static TextRecipe Language() => N("starter.names.language", "Cultural Language Name", TextContentKind.ObjectName, "common", "language");
    public static TextRecipe UmbralMonster() => N("starter.names.umbral-monster", "Umbral Monster Name", TextContentKind.Monster, "umbral", "monster", "hybrid", 48);
    public static TextRecipe SylvanSettlement() => N("starter.names.sylvan-settlement", "Sylvan Settlement Name", TextContentKind.PlaceName, "sylvan", "settlement", "hybrid", 40);
    public static TextRecipe KharicStronghold() => N("starter.names.kharic-stronghold", "Kharic Stronghold Name", TextContentKind.PlaceName, "kharic", "settlement", "morphological", 40);
    public static TextRecipe ValestrianPerson() => PersonFullFor("starter.names.valestrian-person", "Valestrian Full Name", "valestrian");
    public static TextRecipe NorthreachPerson() => PersonFullFor("starter.names.northreach-person", "Northreach Full Name", "northreach");

    public static TextRecipe PersonFull() => PersonFullFor("starter.names.person-full", "Cultural Full Name", "common");
    private static TextRecipe PersonFullFor(string id, string name, string profile) => new(id, 1, name, TextContentKind.PersonName, new[]
    {
        Stage("given", NameGeneratorStage.StageKind, new { profile, domain="personGiven", strategy="hybrid", candidateCount=40, field="given", isPrimary=false }),
        Stage("family", NameGeneratorStage.StageKind, new { profile, domain="personFamily", strategy="hybrid", candidateCount=40, field="family", isPrimary=false }),
        Stage("format", TemplateStage.StageKind, new { template="{{given}} {{family}}", field="name", isPrimary=true })
    }, Description: "A given name and family name produced independently from one coherent culture profile.");

    public static TextRecipe SettlementSet() => new("starter.names.settlement-set", 1, "Settlement Name Set", TextContentKind.PlaceName, new[]
    {
        Stage("set", NameSetStage.StageKind, new { profile="common", entries=new object[] { new { field="settlements", domain="settlement", count=8, separator="\n", isPrimary=true } } })
    }, Description: "Eight non-colliding settlement names from one culture.");

    public static TextRecipe RegionalNamingKit() => new("starter.names.regional-kit", 1, "Regional Naming Kit", TextContentKind.Custom, new[]
    {
        Stage("set", NameSetStage.StageKind, new { profile="common", entries=new object[]
        {
            new { field="country", domain="country", count=1, isPrimary=true },
            new { field="capital", domain="city", count=1 },
            new { field="towns", domain="settlement", count=6, separator="\n" },
            new { field="people", domain="personGiven", count=8, separator="\n" },
            new { field="families", domain="personFamily", count=6, separator="\n" },
            new { field="river", domain="river", count=1 },
            new { field="mountain", domain="mountain", count=1 },
            new { field="language", domain="language", count=1 }
        } })
    }, Description: "A coherent mini naming kit for a whole region; set GenerationContext['culture'] to switch culture without editing the recipe.");

    private static TextRecipe N(string id, string name, TextContentKind kind, string profile, string domain, string strategy="hybrid", int candidates=32)
        => new(id, 1, name, kind, new[] { Stage("generate", NameGeneratorStage.StageKind, new { profile, domain, strategy, candidateCount=candidates, field="name", isPrimary=true }) },
            Description: "Cultural name generator. GenerationContext['culture'] can override the built-in profile.");

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        var element = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        return new TextRecipeStage(id, kind, Settings: element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase));
    }
}

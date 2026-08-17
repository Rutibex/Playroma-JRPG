using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

public static class BuiltInPhase1Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } = new TextRecipe[]
    {
        FantasyPersonName(), FantasyCountryName(), FantasySettlementName(), FantasyWeaponName(), FantasySpellName(),
        FantasyMonsterName(), FantasyTavernName(), FantasyFactionName(), FantasyRumor(), FantasyItemDescription(),
        FantasyArmorName(), FantasyPotionName(), FantasyDungeonName(), FantasyForestName(), FantasyShopName(), FantasyBookTitle(),
        FantasyArtifactName(), FantasyQuestTitle(), FantasyEventHook(), FantasyBattleBark(), FantasyNpcGreeting(), FantasyKeyItemName()
    };

    public static TextRecipe FantasyPersonName() => new("starter.fantasy.person-name", 1, "Fantasy Person Name", TextContentKind.PersonName, new[]
    {
        Stage("given", RandomListStage.StageKind, new { items = new[] { "Alden", "Brenna", "Caelan", "Daria", "Edrin", "Fiora", "Garrick", "Helena", "Ilyra", "Joren", "Kael", "Liora", "Marek", "Nessa", "Orin", "Petra", "Quillan", "Rhea", "Soren", "Talia" }, field = "given", isPrimary = false }),
        Stage("family", RandomListStage.StageKind, new { items = new[] { "Ashford", "Blackmere", "Crowley", "Dawnward", "Emberfall", "Frost", "Greymark", "Holloway", "Ironwood", "Kingsley", "Lightfoot", "Moon", "Nightbriar", "Oakheart", "Pike", "Ravencrest", "Stone", "Thorne", "Vale", "Wintermere" }, field = "family", isPrimary = false }),
        Stage("format", TemplateStage.StageKind, new { template = "{{given}} {{family}}", field = "name", isPrimary = true })
    }, Description: "Reliable two-part fantasy personal names assembled from reusable lists.");

    public static TextRecipe FantasyCountryName() => new("starter.fantasy.country-name", 1, "Fantasy Country Name", TextContentKind.CountryName, new[]
    {
        Stage("construct", AffixComposeStage.StageKind, new { prefixes = new[] { "Ael", "Ar", "Cal", "Dor", "Eld", "Gal", "Ith", "Mor", "Nor", "Val" }, roots = new[] { "ber", "dor", "gath", "land", "mere", "rath", "ros", "thal", "var", "wyn" }, suffixes = new[] { "ia", "en", "or", "um", "ara", "eth" }, prefixProbability = 0.9, suffixProbability = 0.8, transforms = new[] { "capitalize" }, field = "name", isPrimary = true }),
        Stage("length", ConstraintStage.StageKind, new { minLength = 4, maxLength = 18 })
    }, Description: "Compact prefix/root/suffix country-name construction.");

    public static TextRecipe FantasySettlementName() => new("starter.fantasy.settlement-name", 1, "Fantasy Settlement Name", TextContentKind.PlaceName, new[]
    {
        Stage("descriptor", RandomListStage.StageKind, new { items = new[] { "Ash", "Black", "Bright", "Deep", "Dragon", "Eagle", "Elder", "Fair", "Green", "High", "Iron", "Moon", "Oak", "Raven", "Red", "Silver", "Stone", "West", "White", "Wolf" }, field = "descriptor", isPrimary = false }),
        Stage("place", WeightedTableStage.StageKind, new { entries = new object[] { new { text="ford", weight=5 }, new { text="haven", weight=4 }, new { text="keep", weight=2 }, new { text="mere", weight=3 }, new { text="rest", weight=2 }, new { text="ridge", weight=3 }, new { text="vale", weight=4 }, new { text="watch", weight=2 }, new { text="wick", weight=3 }, new { text="wood", weight=4 } }, field = "place", isPrimary = false }),
        Stage("format", TemplateStage.StageKind, new { template = "{{descriptor}}{{place}}", field = "name", isPrimary = true })
    });

    public static TextRecipe FantasyWeaponName() => new("starter.fantasy.weapon-name", 1, "Fantasy Weapon Name", TextContentKind.Weapon, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "{{?epic:The ||}}{{adjective|title}} {{weapon|title}}[[35%: of {{concept|title}}]]", slots = new Dictionary<string, object> { ["epic"] = new[] { "yes", "", "" }, ["adjective"] = new[] { "ashen", "black", "crimson", "dawn", "elder", "fallen", "frozen", "gilded", "grim", "hollow", "iron", "moonlit", "raven", "silent", "storm" }, ["weapon"] = new[] { "blade", "brand", "edge", "fang", "hammer", "lance", "saber", "spear", "thorn", "wrath" }, ["concept"] = new[] { "embers", "kings", "night", "oaths", "ruin", "stars", "storms", "winter" } }, field = "name", isPrimary = true })
    });

    public static TextRecipe FantasySpellName() => new("starter.fantasy.spell-name", 1, "Fantasy Spell Name", TextContentKind.Spell, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "{{adjective|title}} {{noun|title}}[[35%: of {{concept|title}}]]", slots = new Dictionary<string, object> { ["adjective"] = new object[] { "arcane", "ashen", "astral", "crimson", "eldritch", "frozen", "luminous", "silent", "spectral", "stormbound", "umbral" }, ["noun"] = new[] { "aegis", "bolt", "crown", "gate", "lance", "requiem", "seal", "shroud", "star", "torrent", "ward" }, ["concept"] = new[] { "ashes", "dreams", "frost", "memory", "night", "ruin", "stars", "the void", "winter" } }, field = "name", isPrimary = true })
    });

    public static TextRecipe FantasyMonsterName() => new("starter.fantasy.monster-name", 1, "Fantasy Monster Name", TextContentKind.Monster, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "{{descriptor|title}} {{creature|title}}", slots = new Dictionary<string, object> { ["descriptor"] = new[] { "ash", "bog", "bone", "carrion", "cave", "dusk", "ember", "frost", "gloom", "grave", "mire", "moon", "rot", "shadow", "thorn" }, ["creature"] = new[] { "crawler", "fiend", "hound", "lurker", "maw", "reaver", "stalker", "whelp", "wight", "wyrm" } }, field = "name", isPrimary = true })
    });

    public static TextRecipe FantasyTavernName() => new("starter.fantasy.tavern-name", 1, "Fantasy Tavern Name", TextContentKind.PlaceName, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "The {{adjective|title}} {{noun|title}}", slots = new Dictionary<string, object> { ["adjective"] = new[] { "Broken", "Copper", "Dancing", "Drunken", "Golden", "Laughing", "Old", "Red", "Silver", "Sleeping", "Three", "Wandering" }, ["noun"] = new[] { "Anvil", "Boar", "Candle", "Crow", "Dragon", "Fox", "Goblet", "Griffin", "Lantern", "Stag", "Sword", "Tankard" } }, field = "name", isPrimary = true })
    });

    public static TextRecipe FantasyFactionName() => new("starter.fantasy.faction-name", 1, "Fantasy Faction Name", TextContentKind.Faction, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "The {{order|title}} of {{concept|title}}", slots = new Dictionary<string, object> { ["order"] = new[] { "brotherhood", "circle", "company", "covenant", "keepers", "knights", "order", "wardens", "watch" }, ["concept"] = new[] { "ashes", "dawn", "embers", "glass", "iron", "night", "seven stars", "the black rose", "the old road", "winter" } }, field = "name", isPrimary = true })
    });

    public static TextRecipe FantasyRumor() => new("starter.fantasy.rumor", 1, "Fantasy Tavern Rumor", TextContentKind.Rumor, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "{{choice:They say||I heard||Word is}} {{subject}} {{event}}[[35%:, somewhere near {{place}}]].", slots = new Dictionary<string, object> { ["subject"] = new[] { "an old knight", "a merchant caravan", "the duke's hunters", "a wandering mage", "something from the marsh", "the miners" }, ["event"] = new[] { "found a door beneath the ruins", "vanished before sunrise", "saw lights moving under the lake", "brought back a coin no scholar recognizes", "heard bells from the abandoned chapel", "refuses to travel after dark" }, ["place"] = new[] { "Blackmere", "the old north road", "Raven Hill", "the western woods", "Frostford", "the king's barrow" } }, field = "text", isPrimary = true }),
        Stage("clean", TransformStage.StageKind, new { transforms = new[] { "collapse-space" } })
    });

    public static TextRecipe FantasyItemDescription() => new("starter.fantasy.item-description", 1, "Fantasy Item Description", TextContentKind.Description, new[]
    {
        Stage("adlib", AdLibStage.StageKind, new { template = "{{aesthetic|sentence}} {{item}} {{detail}}[[40%:. It {{effect}}]].", slots = new Dictionary<string, object> { ["aesthetic"] = new[] { "a battered", "a finely made", "a strange", "an old", "a surprisingly light", "a rune-marked" }, ["item"] = new[] { "charm", "flask", "key", "medallion", "ring", "stone", "token" }, ["detail"] = new[] { "wrapped in faded cloth", "etched with tiny stars", "cold to the touch", "bearing an unfamiliar crest", "with a hairline crack through its center", "that smells faintly of cedar smoke" }, ["effect"] = new[] { "hums when danger is near", "grows warm beneath moonlight", "seems heavier after sunset", "reflects light that is not there", "never gathers dust" } }, field = "description", isPrimary = true }),
        Stage("clean", TransformStage.StageKind, new { transforms = new[] { "collapse-space" } })
    });


    public static TextRecipe FantasyArmorName() => AdLibRecipe("starter.fantasy.armor-name", "Fantasy Armor Name", TextContentKind.Armor,
        "{{adjective|title}} {{armor|title}}[[30%: of {{concept|title}}]]",
        new Dictionary<string, object> { ["adjective"] = new[] { "ashen", "gilded", "iron", "moonlit", "runed", "scarlet", "silver", "storm", "warded" }, ["armor"] = new[] { "cuirass", "mail", "mantle", "plate", "raiment", "vestment" }, ["concept"] = new[] { "dawn", "embers", "kings", "night", "oaths", "winter" } });

    public static TextRecipe FantasyPotionName() => AdLibRecipe("starter.fantasy.potion-name", "Fantasy Potion Name", TextContentKind.Item,
        "{{form|title}} of {{effect|title}}",
        new Dictionary<string, object> { ["form"] = new[] { "draught", "elixir", "essence", "philter", "tonic", "vial" }, ["effect"] = new[] { "clear sight", "embers", "fleet feet", "giant strength", "moon sleep", "second breath", "stone skin", "winter calm" } });

    public static TextRecipe FantasyDungeonName() => AdLibRecipe("starter.fantasy.dungeon-name", "Fantasy Dungeon Name", TextContentKind.PlaceName,
        "The {{descriptor|title}} {{place|title}}[[35%: of {{concept|title}}]]",
        new Dictionary<string, object> { ["descriptor"] = new[] { "buried", "drowned", "forgotten", "hollow", "iron", "lost", "shattered", "sunken" }, ["place"] = new[] { "crypt", "halls", "keep", "labyrinth", "sanctum", "spire", "vault" }, ["concept"] = new[] { "ashes", "glass", "kings", "night", "the old gods", "whispers" } });

    public static TextRecipe FantasyForestName() => AdLibRecipe("starter.fantasy.forest-name", "Fantasy Forest Name", TextContentKind.PlaceName,
        "{{descriptor|title}} {{wood|title}}",
        new Dictionary<string, object> { ["descriptor"] = new[] { "amber", "black", "elder", "green", "mist", "moon", "silver", "thorn", "whispering" }, ["wood"] = new[] { "briar", "forest", "grove", "weald", "wilds", "wood" } });

    public static TextRecipe FantasyShopName() => AdLibRecipe("starter.fantasy.shop-name", "Fantasy Shop Name", TextContentKind.PlaceName,
        "{{choice:The||}} {{adjective|title}} {{noun|title}}",
        new Dictionary<string, object> { ["adjective"] = new[] { "brass", "clever", "curious", "golden", "honest", "lucky", "old", "silver" }, ["noun"] = new[] { "bottle", "fox", "hammer", "lantern", "needle", "quill", "satchel", "shield" } }, transforms: new[] { "collapse-space", "trim" });

    public static TextRecipe FantasyBookTitle() => AdLibRecipe("starter.fantasy.book-title", "Fantasy Book Title", TextContentKind.ObjectName,
        "{{work|title}} of {{subject|title}}",
        new Dictionary<string, object> { ["work"] = new[] { "annals", "book", "chronicle", "concordance", "history", "observations", "treatise" }, ["subject"] = new[] { "ancient crowns", "dreaming stars", "forgotten roads", "glass and flame", "moon rites", "the elder kings", "the western marches" } });

    public static TextRecipe FantasyArtifactName() => AdLibRecipe("starter.fantasy.artifact-name", "Fantasy Artifact Name", TextContentKind.ObjectName,
        "The {{relic|title}} of {{name|title}}",
        new Dictionary<string, object> { ["relic"] = new[] { "bell", "crown", "eye", "heart", "key", "mirror", "seal", "stone" }, ["name"] = new[] { "a thousand winters", "ashes", "dawn", "lost kings", "night", "seven stars", "the first flame" } });

    public static TextRecipe FantasyQuestTitle() => AdLibRecipe("starter.fantasy.quest-title", "Fantasy Quest Title", TextContentKind.Quest,
        "{{choice:The||}} {{noun|title}} {{link|title}} {{subject|title}}",
        new Dictionary<string, object> { ["noun"] = new[] { "echo", "road", "secret", "shadow", "song", "trail", "watch" }, ["link"] = new[] { "beyond", "beneath", "of", "under" }, ["subject"] = new[] { "black hill", "fallen stars", "frostmere", "old stones", "the barrow", "the red moon" } }, transforms: new[] { "collapse-space", "trim" });

    public static TextRecipe FantasyEventHook() => AdLibRecipe("starter.fantasy.event-hook", "Fantasy Event Hook", TextContentKind.Event,
        "{{trigger|sentence}} {{discovery}}[[35%:, but {{complication}}]].",
        new Dictionary<string, object> { ["trigger"] = new[] { "at sunset", "during the market", "on the old road", "while the party rests", "when the bells ring" }, ["discovery"] = new[] { "a sealed wagon arrives without a driver", "an injured messenger asks for help", "a child produces a royal signet", "the river suddenly runs black", "a stranger calls one hero by an old name" }, ["complication"] = new[] { "someone is already watching", "the local guard refuses to intervene", "the evidence points to an ally", "the event repeats at midnight" } });

    public static TextRecipe FantasyBattleBark() => AdLibRecipe("starter.fantasy.battle-bark", "Fantasy Battle Bark", TextContentKind.Dialogue,
        "{{line|sentence|exclaim}}",
        new Dictionary<string, object> { ["line"] = new[] { "hold the line", "not one step farther", "now we finish this", "stay behind me", "you picked the wrong road", "your luck ends here" } });

    public static TextRecipe FantasyNpcGreeting() => AdLibRecipe("starter.fantasy.npc-greeting", "Fantasy NPC Greeting", TextContentKind.Dialogue,
        "{{greeting|sentence}}[[40%: {{followup|sentence}}]]",
        new Dictionary<string, object> { ["greeting"] = new[] { "good evening, traveler", "haven't seen you here before", "mind the mud by the door", "welcome in", "you look like you've come a long way" }, ["followup"] = new[] { "the road has been strange lately", "ask if you need directions", "we've still got hot stew", "keep your purse close after sunset" } });

    public static TextRecipe FantasyKeyItemName() => AdLibRecipe("starter.fantasy.key-item-name", "Fantasy Key Item Name", TextContentKind.Item,
        "{{descriptor|title}} {{object|title}}",
        new Dictionary<string, object> { ["descriptor"] = new[] { "ancient", "cracked", "etched", "forgotten", "royal", "sealed", "silver", "weathered" }, ["object"] = new[] { "crest", "key", "letter", "medallion", "seal", "tablet", "token" } });

    private static TextRecipe AdLibRecipe(string id, string name, TextContentKind kind, string template, Dictionary<string, object> slots, string[]? transforms = null)
    {
        var stages = new List<TextRecipeStage>
        {
            Stage("generate", AdLibStage.StageKind, new { template, slots, field = kind is TextContentKind.Dialogue or TextContentKind.Event ? "text" : "name", isPrimary = true })
        };
        if (transforms is { Length: > 0 }) stages.Add(Stage("clean", TransformStage.StageKind, new { transforms }));
        return new TextRecipe(id, 1, name, kind, stages);
    }

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        var element = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        var dictionary = element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        return new TextRecipeStage(id, kind, Settings: dictionary);
    }
}

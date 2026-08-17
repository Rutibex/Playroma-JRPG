using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;

namespace Playroma.Text.Generators;

public static class BuiltInPhase3Grammars
{
    private static TraceryRuleDefinition R(string text, double weight = 1, string? condition = null, IReadOnlyDictionary<string, string>? metadata = null)
        => new(text, weight, metadata, condition);
    private static IReadOnlyList<TraceryRuleDefinition> Rules(params string[] rules) => rules.Select(x => R(x)).ToArray();
    private static IReadOnlyDictionary<string, IReadOnlyList<TraceryRuleDefinition>> S(params (string Key, IReadOnlyList<TraceryRuleDefinition> Rules)[] entries)
        => entries.ToDictionary(x => x.Key, x => x.Rules, StringComparer.OrdinalIgnoreCase);

    public static TraceryGrammarDocument ClassicStory() => new(
        "builtin.tracery.classic-story", 1, "Classic Tracery Story",
        S(
            ("origin", Rules("#[hero:#name#][heroPet:#animal#]story#")),
            ("name", Rules("Arin", "Mira", "Tomas", "Nia", "Kellan", "Suri")),
            ("animal", Rules("raven", "fox", "owl", "hound", "lizard", "cat")),
            ("mood", Rules("wistful", "courteous", "vexed", "bold", "patient", "restless")),
            ("story", Rules("#hero# traveled with #heroPet.a#. #hero# was never #mood#, for #heroPet.a# was always too #mood#."))),
        Compatibility: TraceryCompatibilityMode.Classic,
        Description: "Exercises classic symbol expansion, modifiers, and scoped actions.");

    public static TraceryGrammarDocument SpellDescription() => new(
        "builtin.tracery.spell-description", 1, "Spell Description",
        S(
            ("origin", Rules("#opening.capitalize# #effect# #target#.", "#spellName# — #effect.capitalize# #target#.")),
            ("opening", Rules("with a whispered invocation", "at the caster's command", "in a burst of arcane force", "for a heartbeat")),
            ("spellName", Rules("#adjective.capitalize# #noun.capitalize#")),
            ("adjective", Rules("ashen", "glacial", "radiant", "stormbound", "hollow", "verdant")),
            ("noun", Rules("requiem", "lance", "crown", "seal", "veil", "torrent")),
            ("effect", Rules("scours", "binds", "shields", "marks", "freezes", "illuminates")),
            ("target", Rules("one enemy", "all nearby foes", "the caster", "an ally", "the battlefield"))),
        Tags: new HashSet<string>(["spell", "description"], StringComparer.OrdinalIgnoreCase));

    public static TraceryGrammarDocument NpcGreeting() => new(
        "builtin.tracery.npc-greeting", 1, "NPC Greeting",
        S(
            ("origin", [
                R("#friendly#", 3, "mood=friendly", new Dictionary<string,string>{{"tone","friendly"}}),
                R("#hostile#", 3, "mood=hostile", new Dictionary<string,string>{{"tone","hostile"}}),
                R("#neutral#", 2, "mood=neutral", new Dictionary<string,string>{{"tone","neutral"}}),
                R("#neutral#", 1, "!mood")
            ]),
            ("friendly", Rules("Good to see you, traveler!", "Welcome! You look like someone with stories to tell.", "Ah, a friendly face at last.")),
            ("hostile", Rules("Keep your hands where I can see them.", "State your business and move along.", "I don't know you, and I'd prefer to keep it that way.")),
            ("neutral", Rules("Need something?", "What brings you here?", "Evening, traveler."))),
        Parameters: new Dictionary<string, TraceryParameterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["mood"] = new("mood", RecipeParameterType.Choice, false, Json("\"neutral\""), "NPC disposition.", ["friendly", "neutral", "hostile"])
        });

    public static TraceryGrammarDocument BindingDemo() => new(
        "builtin.tracery.binding-demo", 1, "Persistent Character Binding",
        S(
            ("origin", Rules("#[hero:#name#][home:#town#]story#")),
            ("name", Rules("Mara", "Orin", "Selka", "Beren")),
            ("town", Rules("Greyhaven", "Westmere", "Dunwall", "Oakrest")),
            ("story", Rules("#hero# left #home# before dawn. By sunset, #hero# already missed #home#."))));

    public static TraceryGrammarDocument MonsterEpithet() => new(
        "builtin.tracery.monster-epithet", 1, "Monster Epithet",
        S(
            ("origin", Rules("The #adjective.capitalize# #beast.capitalize#", "#beast.capitalize# of the #place.capitalize#")),
            ("adjective", Rules("hollow", "red", "gloom", "iron", "starved", "ancient")),
            ("beast", Rules("crawler", "wyrm", "stalker", "maw", "watcher", "brute")),
            ("place", Rules("deep", "marsh", "old road", "black tower", "sunken shrine"))));

    public static TraceryGrammarDocument TavernRumor() => new(
        "builtin.tracery.tavern-rumor", 1, "Tavern Rumor",
        S(
            ("origin", Rules("They say #subject# #event# #place#.", "I heard #subject# #event# #place#, but don't say it came from me.")),
            ("subject", Rules("the old baron", "a merchant caravan", "the gravekeeper", "three children", "a masked knight")),
            ("event", Rules("vanished near", "found a sealed door beneath", "saw blue fire above", "heard bells ringing under", "was followed home from")),
            ("place", Rules("the northern ruins", "Blackwater Ford", "the abandoned mill", "Saint Orra's hill", "the old mines"))));

    public static TraceryGrammarDocument ItemLore() => new(
        "builtin.tracery.item-lore", 1, "Item Lore",
        S(
            ("origin", Rules("#object.a.capitalize# #history#", "#history.capitalize# It is now known as #object.a#.")),
            ("object", Rules("silver key", "cracked idol", "blackened crown", "glass dagger", "funerary bell")),
            ("history", Rules("was carried through three wars and returned without its owner.", "was recovered from a shrine no map admits exists.", "bears a maker's mark belonging to a kingdom erased from every chronicle."))));

    public static TraceryGrammarDocument QuestHook() => new(
        "builtin.tracery.quest-hook", 1, "Quest Hook",
        S(
            ("origin", Rules("#giver.capitalize# needs someone to #task# before #deadline#.", "#problem.capitalize#; #giver# is offering #reward# to anyone willing to #task#.")),
            ("giver", Rules("a frightened apothecary", "the village reeve", "a retired soldier", "a traveling scholar")),
            ("task", Rules("recover a stolen ledger", "find a missing scout", "carry a sealed letter through the marsh", "investigate lights in the crypt")),
            ("deadline", Rules("the next full moon", "the caravan leaves", "the river floods", "the magistrate arrives")),
            ("problem", Rules("someone has been opening graves", "a bridge toll has become deadly", "livestock are vanishing without tracks", "the old watchtower is lit again")),
            ("reward", Rules("a purse of gold", "an old family weapon", "access to a forbidden archive", "a valuable favor"))));

    public static IReadOnlyList<TraceryGrammarDocument> All { get; } =
    [ ClassicStory(), SpellDescription(), NpcGreeting(), BindingDemo(), MonsterEpithet(), TavernRumor(), ItemLore(), QuestHook() ];

    private static JsonElement Json(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
}

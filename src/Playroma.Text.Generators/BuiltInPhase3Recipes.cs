using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

public static class BuiltInPhase3Recipes
{
    private static TextRecipe FromGrammar(TraceryGrammarDocument grammar, TextContentKind kind, IReadOnlyDictionary<string, JsonElement>? extra = null)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["grammar"] = JsonElementFor(grammar),
            ["field"] = Json("\"text\""),
            ["isPrimary"] = Json("true"),
            ["emitMetadataTags"] = Json("true")
        };
        if (extra is not null) foreach (var x in extra) settings[x.Key] = x.Value;
        return new TextRecipe("phase3." + grammar.Id.Replace("builtin.tracery.", string.Empty), 1, grammar.Name, kind,
            [new TextRecipeStage("tracery", TraceryGrammarStage.StageKind, Settings: settings)],
            Description: grammar.Description);
    }

    public static TextRecipe ClassicStory() => FromGrammar(BuiltInPhase3Grammars.ClassicStory(), TextContentKind.Lore);
    public static TextRecipe SpellDescription() => FromGrammar(BuiltInPhase3Grammars.SpellDescription(), TextContentKind.Spell);
    public static TextRecipe BindingDemo() => FromGrammar(BuiltInPhase3Grammars.BindingDemo(), TextContentKind.Lore);
    public static TextRecipe MonsterEpithet() => FromGrammar(BuiltInPhase3Grammars.MonsterEpithet(), TextContentKind.Monster);
    public static TextRecipe TavernRumor() => FromGrammar(BuiltInPhase3Grammars.TavernRumor(), TextContentKind.Rumor);
    public static TextRecipe ItemLore() => FromGrammar(BuiltInPhase3Grammars.ItemLore(), TextContentKind.Lore);
    public static TextRecipe QuestHook() => FromGrammar(BuiltInPhase3Grammars.QuestHook(), TextContentKind.Quest);
    public static TextRecipe FriendlyGreeting() => FromGrammar(BuiltInPhase3Grammars.NpcGreeting(), TextContentKind.Dialogue,
        new Dictionary<string, JsonElement> { ["parameters"] = Json("{\"mood\":\"friendly\"}") });
    public static TextRecipe HostileGreeting() => FromGrammar(BuiltInPhase3Grammars.NpcGreeting(), TextContentKind.Dialogue,
        new Dictionary<string, JsonElement> { ["parameters"] = Json("{\"mood\":\"hostile\"}") });

    public static IReadOnlyList<TextRecipe> All { get; } =
    [ ClassicStory(), SpellDescription(), BindingDemo(), MonsterEpithet(), TavernRumor(), ItemLore(), QuestHook(), FriendlyGreeting(), HostileGreeting() ];

    private static JsonElement JsonElementFor(TraceryGrammarDocument grammar)
    {
        using var doc = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(grammar, false));
        return doc.RootElement.Clone();
    }
    private static JsonElement Json(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
}

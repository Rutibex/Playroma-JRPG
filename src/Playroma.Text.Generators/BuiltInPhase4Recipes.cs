using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Playground-oriented recipes added with the Phase 4 authoring UI.</summary>
public static class BuiltInPhase4Recipes
{
    public static TextRecipe AdjustableNpcGreeting()
    {
        var grammar = BuiltInPhase3Grammars.NpcGreeting();
        using var grammarDoc = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(grammar, false));
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["grammar"] = grammarDoc.RootElement.Clone(),
            ["field"] = Json("\"text\""),
            ["isPrimary"] = Json("true"),
            ["emitMetadataTags"] = Json("true")
        };
        var parameters = new Dictionary<string, RecipeParameterDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["mood"] = new("mood", RecipeParameterType.Choice, false, Json("\"neutral\""), "NPC disposition used to select dialogue rules.", ["friendly", "neutral", "hostile"])
        };
        return new TextRecipe("phase4.playground.npc-greeting", 1, "NPC Greeting — Adjustable", TextContentKind.Dialogue,
            [new TextRecipeStage("tracery", TraceryGrammarStage.StageKind, Settings: settings)], parameters,
            Description: "A live-playground greeting recipe with an exposed mood control.");
    }

    public static IReadOnlyList<TextRecipe> All { get; } = [AdjustableNpcGreeting()];

    private static JsonElement Json(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
}

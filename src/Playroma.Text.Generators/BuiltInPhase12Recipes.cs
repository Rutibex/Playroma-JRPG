using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Small, composable realization recipes used by the Phase 12 quest planner.</summary>
public static class BuiltInPhase12Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } = [QuestTitle(), Objective(), QuestSummary(), JournalUpdate(), QuestComplete()];

    private static TextRecipe QuestTitle() => new(
        "phase12.quest.title", 1, "Planned Quest Title", TextContentKind.Quest,
        [Stage("title", TemplateStage.StageKind, new { template="The {{quest.object|title}}", field="title", isPrimary=true, missing="keep" })],
        Description:"Creates a short title from quest-planner bindings.");

    private static TextRecipe Objective() => new(
        "phase12.quest.objective", 1, "Planned Quest Objective", TextContentKind.Quest,
        [Stage("text", TemplateStage.StageKind, new { template="{{objective.text}}", field="text", isPrimary=true })],
        Description:"Realizes a planner-supplied objective string without altering its semantics.");

    private static TextRecipe QuestSummary() => new(
        "phase12.quest.summary", 1, "Planned Quest Summary", TextContentKind.Quest,
        [Stage("text", AdLibStage.StageKind, new { template="{{opening|sentence}} {{quest.summary}}", slots=new Dictionary<string,object>{ ["opening"]=new[]{"A local problem has grown too serious to ignore.","Someone is asking for capable help.","A simple request may lead farther than expected.","The situation will not resolve itself."} }, field="text", isPrimary=true })],
        Description:"Wraps a causally validated plan in concise quest-journal prose.");

    private static TextRecipe JournalUpdate() => new(
        "phase12.quest.journal-update", 1, "Planned Quest Journal Update", TextContentKind.Quest,
        [Stage("text", AdLibStage.StageKind, new { template="{{objective.text}} {{suffix|sentence}}", slots=new Dictionary<string,object>{ ["suffix"]=new[]{"the next step is clear","there may be more to discover","someone nearby may know more","the trail continues from here"} }, field="text", isPrimary=true })],
        Description:"Produces a journal update for one planned quest step.");

    private static TextRecipe QuestComplete() => new(
        "phase12.quest.complete", 1, "Planned Quest Completion", TextContentKind.Quest,
        [Stage("text", RandomListStage.StageKind, new { items=new[]{"The objective is complete. Return for the promised reward.","The immediate danger has passed. Report what happened.","The task is finished, though its consequences may continue.","Everything requested has been done. It is time to return."}, field="text", isPrimary=true })],
        Description:"Completion text for a planned quest.");

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        JsonElement e = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        return new(id, kind, Settings:e.EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.Clone(),StringComparer.OrdinalIgnoreCase));
    }
}

using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Generators;

/// <summary>Phase 14 release examples. Optional AI is intentionally not a built-in default recipe.</summary>
public static class BuiltInPhase14Recipes
{
    public static IReadOnlyList<TextRecipe> All { get; } = [QualityDiverseRelicTitle()];

    public static TextRecipe QualityDiverseRelicTitle()
    {
        var stages = new[]
        {
            Stage("adlib", AdLibStage.StageKind, new
            {
                template = "The {{adjective|title}} {{object|title}} of {{concept|title}}",
                slots = new Dictionary<string, object>
                {
                    ["adjective"] = new[] { "ashen", "silver", "hollow", "forgotten", "radiant", "winter", "veiled", "broken", "starless", "verdant" },
                    ["object"] = new[] { "crown", "bell", "key", "mirror", "blade", "lantern", "seal", "chalice", "gate", "harp" },
                    ["concept"] = new[] { "dawn", "memory", "storms", "the deep", "kings", "silence", "embers", "dreams", "the moon", "lost roads" }
                },
                field = "name",
                isPrimary = true
            })
        };
        var pipeline = new CandidatePipelineDefinition(
            CandidateCount: 128,
            Scorers:
            [
                new CandidateScorerDefinition("length", CandidateScorerKind.CharacterLength, .7, Ideal: 30, Minimum: 16, Maximum: 52),
                new CandidateScorerDefinition("variety", CandidateScorerKind.UniqueWordRatio, .35)
            ],
            DiversityWeight: .45,
            MinimumDiversity: .18,
            Search: new CandidateSearchDefinition(CandidateSearchMode.QualityDiversity, PopulationSize: 24, Iterations: 4, ArchiveBins: 8, NoveltyWeight: .35));
        return new TextRecipe("phase14.search.quality-diverse-relic", 1, "Quality-Diverse Relic Title", TextContentKind.ObjectName, stages,
            Description: "Demonstration of quality-diversity search over deterministic generator seed space.", CandidatePipeline: pipeline);
    }

    private static TextRecipeStage Stage(string id, string kind, object settings)
    {
        var element = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
        return new TextRecipeStage(id, kind, Settings: element.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase));
    }
}

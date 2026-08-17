using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.Text.Core.Generation;

public sealed record TextRecipe(
    string Id,
    int Version,
    string Name,
    TextContentKind Kind,
    IReadOnlyList<TextRecipeStage> Stages,
    IReadOnlyDictionary<string, RecipeParameterDefinition>? Parameters = null,
    IReadOnlySet<string>? Tags = null,
    string? Description = null,
    CandidatePipelineDefinition? CandidatePipeline = null)
{
    public string Fingerprint()
    {
        var json = JsonSerializer.Serialize(this, RecipeJson.Options);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record TextRecipeStage(
    string Id,
    string Kind,
    bool Enabled = true,
    IReadOnlyDictionary<string, JsonElement>? Settings = null);

public enum RecipeParameterType { String, Integer, Number, Boolean, Choice, StringList }

public sealed record RecipeParameterDefinition(
    string Name,
    RecipeParameterType Type,
    bool Required = false,
    JsonElement? DefaultValue = null,
    string? Description = null,
    IReadOnlyList<string>? Choices = null);

public static class RecipeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class TextRecipeValidator
{
    public static IReadOnlyList<GenerationDiagnostic> Validate(TextRecipe recipe)
    {
        var result = new List<GenerationDiagnostic>();
        if (string.IsNullOrWhiteSpace(recipe.Id)) result.Add(Error("Recipe id is required."));
        if (recipe.Version < 1) result.Add(Error("Recipe version must be >= 1."));
        if (string.IsNullOrWhiteSpace(recipe.Name)) result.Add(Error("Recipe name is required."));
        if (recipe.Stages.Count == 0) result.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidRecipe, GenerationDiagnosticSeverity.Warning, "Recipe has no stages."));
        var duplicate = recipe.Stages.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (duplicate is not null) result.Add(Error("Stage ids must be non-empty and unique."));
        foreach (var stage in recipe.Stages.Where(s => string.IsNullOrWhiteSpace(s.Kind))) result.Add(Error("Stage kind is required.", stage.Id));
        if (recipe.CandidatePipeline is { } pipeline)
        {
            if (pipeline.CandidateCount < 1 || pipeline.CandidateCount > 512) result.Add(Error("Candidate pipeline candidateCount must be between 1 and 512."));
            if (pipeline.DiversityWeight < 0 || pipeline.DiversityWeight > 10) result.Add(Error("Candidate pipeline diversityWeight must be between 0 and 10."));
            if (pipeline.MinimumDiversity < 0 || pipeline.MinimumDiversity > 1) result.Add(Error("Candidate pipeline minimumDiversity must be between 0 and 1."));
            foreach (var filter in pipeline.EffectiveFilters) if (string.IsNullOrWhiteSpace(filter.Id) || string.IsNullOrWhiteSpace(filter.Predicate)) result.Add(Error("Candidate filters require non-empty id and predicate."));
            foreach (var scorer in pipeline.EffectiveScorers) if (string.IsNullOrWhiteSpace(scorer.Id) || double.IsNaN(scorer.Weight) || double.IsInfinity(scorer.Weight)) result.Add(Error("Candidate scorers require a valid id and finite weight."));
            if (pipeline.Search is { } search)
            {
                if (search.PopulationSize < 2 || search.PopulationSize > 128) result.Add(Error("Search populationSize must be between 2 and 128."));
                if (search.Iterations < 1 || search.Iterations > 24) result.Add(Error("Search iterations must be between 1 and 24."));
                if (search.EliteCount < 1 || search.EliteCount > search.PopulationSize) result.Add(Error("Search eliteCount must be between 1 and populationSize."));
                if (search.BeamWidth < 1 || search.BeamWidth > search.PopulationSize) result.Add(Error("Search beamWidth must be between 1 and populationSize."));
                if (search.ArchiveBins < 2 || search.ArchiveBins > 32) result.Add(Error("Search archiveBins must be between 2 and 32."));
                if (search.NoveltyWeight < 0 || search.NoveltyWeight > 5) result.Add(Error("Search noveltyWeight must be between 0 and 5."));
                if (search.RandomImmigrantRate < 0 || search.RandomImmigrantRate > 0.75) result.Add(Error("Search randomImmigrantRate must be between 0 and 0.75."));
            }
        }
        return result;
    }

    private static GenerationDiagnostic Error(string message, string? stageId = null) => new(GenerationDiagnosticCodes.InvalidRecipe, GenerationDiagnosticSeverity.Error, message, stageId);
}

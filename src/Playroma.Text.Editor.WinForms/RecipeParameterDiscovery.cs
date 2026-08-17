using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Editor.WinForms;

public static class RecipeParameterDiscovery
{
    public static IReadOnlyDictionary<string, RecipeParameterDefinition> Discover(TextRecipe recipe)
    {
        var result = new Dictionary<string, RecipeParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        if (recipe.Parameters is not null)
            foreach (var pair in recipe.Parameters) result[pair.Key] = pair.Value;

        foreach (var stage in recipe.Stages)
        {
            if (string.Equals(stage.Kind, NameGeneratorStage.StageKind, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stage.Kind, NameSetStage.StageKind, StringComparison.OrdinalIgnoreCase))
            {
                if (!result.ContainsKey("culture"))
                    result["culture"] = new RecipeParameterDefinition("culture", RecipeParameterType.Choice, false,
                        JsonSerializer.SerializeToElement("common"), "Language/culture profile used for generated names.",
                        ["common", "valestrian", "northreach", "sylvan", "kharic", "umbral"]);
            }

            if (!string.Equals(stage.Kind, TraceryGrammarStage.StageKind, StringComparison.OrdinalIgnoreCase) || stage.Settings is null ||
                !stage.Settings.TryGetValue("grammar", out var grammarElement) || grammarElement.ValueKind != JsonValueKind.Object) continue;
            try
            {
                var grammar = TraceryGrammarJson.Parse(grammarElement, recipe.Id + ".inline", recipe.Name + " Grammar");
                var fixedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (stage.Settings.TryGetValue("parameters", out var fixedElement) && fixedElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in fixedElement.EnumerateObject()) fixedParameters.Add(property.Name);
                foreach (var pair in grammar.EffectiveParameters)
                    if (!fixedParameters.Contains(pair.Key) && !result.ContainsKey(pair.Key))
                        result[pair.Key] = new RecipeParameterDefinition(pair.Key, pair.Value.Type, pair.Value.Required,
                            pair.Value.DefaultValue, pair.Value.Description, pair.Value.Choices);
            }
            catch { /* Invalid grammar diagnostics are displayed by generation/designer validation. */ }
        }
        return result;
    }
}

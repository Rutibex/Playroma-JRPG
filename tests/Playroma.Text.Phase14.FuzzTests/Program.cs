using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;

var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase14Registry());
var rng = new Random(140014);
var failures = new List<string>();

for (var i = 0; i < 750; i++)
{
    var template = FuzzTemplate(rng, rng.Next(1, 180));
    var settings = JsonSerializer.SerializeToElement(new { template, slots = new Dictionary<string, string[]> { ["x"] = ["one", "two", "three"] }, field = "text", isPrimary = true }, RecipeJson.Options)
        .EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    var recipe = new TextRecipe("fuzz." + i, 1, "Fuzz", TextContentKind.Custom, [new TextRecipeStage("template", AdLibStage.StageKind, Settings: settings)]);
    try
    {
        var bundle = await engine.GenerateAsync(recipe, seed: (ulong)i, budgetOptions: new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromMilliseconds(250), MaxOutputCharacters: 4096, MaxTraceNodes: 256));
        if ((bundle.PrimaryText?.Length ?? 0) > 4096) failures.Add("Output budget exceeded at case " + i);
    }
    catch (Exception ex) { failures.Add($"Unhandled fuzz exception at {i}: {ex.GetType().Name}: {ex.Message}"); }
}

foreach (var recipe in BuiltInRecipeCatalog.All)
{
    for (ulong seed = 0; seed < 5; seed++)
    {
        try
        {
            var bundle = await engine.GenerateAsync(recipe, seed: 0x140000 + seed, budgetOptions: new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(2), MaxOutputCharacters: 16_384, MaxTraceNodes: 4096));
            if ((bundle.PrimaryText?.Length ?? 0) > 16_384) failures.Add("Built-in output exceeded budget: " + recipe.Id);
        }
        catch (Exception ex) { failures.Add($"Built-in {recipe.Id} threw {ex.GetType().Name}: {ex.Message}"); }
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Phase 14 fuzz/stress tests FAILED:");
    foreach (var f in failures.Take(50)) Console.Error.WriteLine(" - " + f);
    return 1;
}
Console.WriteLine("Phase 14 fuzz/stress tests passed (750 malformed-template cases + built-in smoke matrix).");
return 0;

static string FuzzTemplate(Random rng, int length)
{
    const string alphabet = "abc XYZ0123{}[]|:%#\\\n\t!?.,'";
    var chars = new char[length];
    for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
    if (rng.NextDouble() < .5) return "{{x}} " + new string(chars);
    return new string(chars);
}

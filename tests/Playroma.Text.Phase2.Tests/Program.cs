using Playroma.Text.Authoring;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Names;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Names;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var profiles = LanguageProfileRegistry.CreateBuiltIn();
foreach (var id in new[] { "common", "valestrian", "northreach", "sylvan", "kharic", "umbral" })
{
    var profile = profiles.Resolve(id);
    Check(profile is not null, "profile resolves " + id);
    Check(profile is null || (profile.Onsets.Count > 0 && profile.Nuclei.Count > 0 && profile.Codas.Count > 0), "profile phonology " + id);
    Check(profile is null || LanguageProfileValidator.Validate(profile).All(x => x.Severity != GenerationDiagnosticSeverity.Error), "profile validates " + id);
}
Check(profiles.Resolve("valestrian")?.EffectiveDomains.ContainsKey(NameDomain.Monster) == true, "culture inheritance retains parent domains");

var registry = GeneratorCatalog.CreatePhase2Registry(profiles: profiles);
foreach (var kind in new[] { NameGeneratorStage.StageKind, NameSetStage.StageKind, NameCollisionStage.StageKind })
    Check(registry.Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase), "registered stage " + kind);

var names = new NameGenerationEngine(profiles);
var request = new NameGenerationRequest("sylvan", NameDomain.PersonGiven, CandidateCount: 48);
var a = names.Generate(request, new Xoshiro256StarStar(777));
var b = names.Generate(request, new Xoshiro256StarStar(777));
Check(a.Success && a.Text == b.Text, "name determinism");
Check(a.Text.Length >= 4, "name minimum length");

var cultural = new[] { "valestrian", "northreach", "sylvan", "kharic", "umbral" }
    .Select(id => names.Generate(new NameGenerationRequest(id, NameDomain.Country, CandidateCount: 48), new Xoshiro256StarStar(991)).Text)
    .ToArray();
Check(cultural.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 4, "culture profiles materially differ");

var markovOne = names.Generate(new NameGenerationRequest("valestrian", NameDomain.PersonGiven, NameGenerationStrategy.Markov, CandidateCount: 64, MaxCorpusSimilarity: .985), new Xoshiro256StarStar(123));
var markovTwo = names.Generate(new NameGenerationRequest("valestrian", NameDomain.PersonGiven, NameGenerationStrategy.Markov, CandidateCount: 64, MaxCorpusSimilarity: .985), new Xoshiro256StarStar(123));
Check(markovOne.Text == markovTwo.Text && !string.IsNullOrWhiteSpace(markovOne.Text), "markov deterministic generation");

var collisionSeed = names.Generate(new NameGenerationRequest("common", NameDomain.Country, CandidateCount: 64), new Xoshiro256StarStar(42));
var collisionAvoided = names.Generate(new NameGenerationRequest("common", NameDomain.Country, CandidateCount: 64, ExistingNames: new[] { collisionSeed.Text }, MaxExistingSimilarity: .8), new Xoshiro256StarStar(42));
Check(collisionAvoided.Success && !string.Equals(collisionSeed.Text, collisionAvoided.Text, StringComparison.OrdinalIgnoreCase), "project collision avoidance");

Check(NameSimilarity.Composite("Valeria", "Valeria") == 1, "similarity exact");
Check(NameSimilarity.Composite("Valeria", "Zuthrok") < NameSimilarity.Composite("Valeria", "Valeria"), "similarity ordering");

var engine = new TextGenerationEngine(registry);
foreach (var recipe in BuiltInPhase2Recipes.All)
{
    Check(TextRecipeValidator.Validate(recipe).All(x => x.Severity != GenerationDiagnosticSeverity.Error), "phase2 recipe validates " + recipe.Id);
    var output = await engine.GenerateAsync(recipe, seed: 45678, budgetOptions: new GenerationBudgetOptions(MaxCandidateAttempts: 6000));
    Check(!string.IsNullOrWhiteSpace(output.PrimaryText), "phase2 recipe output " + recipe.Id);
    Check(output.Diagnostics.All(x => x.Severity != GenerationDiagnosticSeverity.Error), "phase2 recipe diagnostics " + recipe.Id);
}

var kit = await engine.GenerateAsync(BuiltInPhase2Recipes.RegionalNamingKit(), new GenerationContext(Values: new Dictionary<string, object?> { ["culture"] = "northreach" }), seed: 8080, budgetOptions: new GenerationBudgetOptions(MaxCandidateAttempts: 6000));
Check(kit.Fields.Any(x => x.Name == "towns" && x.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6), "regional kit unique towns");
Check(kit.Fields.Any(x => x.Name == "people" && x.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 8), "regional kit people");

var exportRoot = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase2-" + Guid.NewGuid().ToString("N"));
var recipeDir = Path.Combine(exportRoot, "recipes");
var cultureDir = Path.Combine(exportRoot, "cultures");
await BuiltInRecipeExporter.ExportPhase2Async(recipeDir);
await BuiltInLanguageProfileExporter.ExportPhase2Async(cultureDir);
var recipeLibrary = await new TextRecipeLibrary().LoadDirectoryAsync(recipeDir);
var loadedProfiles = await new LanguageProfileStore().LoadDirectoryAsync(cultureDir);
Check(recipeLibrary.Count == BuiltInPhase2Recipes.All.Count, "phase2 recipe export count");
Check(loadedProfiles.Count == BuiltInLanguageProfiles.All.Count, "language profile export count");
Check(loadedProfiles.All(x => !string.IsNullOrWhiteSpace(x.Id)), "language profile round trip");
Directory.Delete(exportRoot, true);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 2 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 2 tests passed.");
return 0;

using System.Text.Json;
using Playroma.Text.Authoring;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Selection;
using Playroma.Text.Generators.Stages;
using Playroma.Text.Generators.Templates;
using Playroma.Text.Generators.Transforms;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

// Weighted selection is deterministic and honors a single positive entry.
var weights = new[] { new WeightedText("never", 0), new WeightedText("always", 10) };
Check(WeightedSelector.Select(weights, x => x.Weight, new Xoshiro256StarStar(1)).Text == "always", "weighted selection");

// Shuffle bags emit every member before refilling.
var bag = new ShuffleBag<string>(new[] { "a", "b", "c" }, new Xoshiro256StarStar(2));
var firstCycle = bag.DrawMany(3);
Check(firstCycle.Distinct().Count() == 3, "shuffle bag unique first cycle");

// Built-in transforms.
var transforms = TextTransformRegistry.CreateDefault();
Check(transforms.TryApply("title", "ashen crown", Array.Empty<string>(), out var title) && title == "Ashen Crown", "title transform");
Check(transforms.TryApply("a", "ember", Array.Empty<string>(), out var article) && article == "an ember", "article transform");

// Template rendering: substitutions, transforms, conditional, choice and optional fragments.
var renderer = new TemplateRenderer(transforms);
var rendered = renderer.Render("{{name|title}} {{?hero:returns||flees}} {{choice:today||tomorrow}}[[100%:!]]",
    new Dictionary<string, object?> { ["name"] = "ashen knight", ["hero"] = true }, new Xoshiro256StarStar(3));
Check(rendered.Text.StartsWith("Ashen Knight returns ", StringComparison.Ordinal) && rendered.Text.EndsWith('!'), "template features");
Check(rendered.MissingKeys.Count == 0 && rendered.UnknownTransforms.Count == 0, "template diagnostics");

// Phase-1 registry has every concrete stage.
var registry = GeneratorCatalog.CreatePhase1Registry();
foreach (var kind in new[] { RandomListStage.StageKind, WeightedTableStage.StageKind, NestedTableStage.StageKind, ShuffleBagStage.StageKind, TemplateStage.StageKind, AdLibStage.StageKind, AffixComposeStage.StageKind, TransformStage.StageKind, ConstraintStage.StageKind })
    Check(registry.Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase), "registered stage " + kind);

// Recursive named tables expand through guarded @{table} references.
var nestedSettings = JsonSerializer.SerializeToElement(new
{
    origin = "origin",
    tables = new Dictionary<string, object>
    {
        ["origin"] = new[] { "The @{adjective} @{noun}" },
        ["adjective"] = new[] { "Ashen" },
        ["noun"] = new[] { "Crown" }
    },
    field = "name",
    isPrimary = true
}, RecipeJson.Options).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
var nestedRecipe = new TextRecipe("test.nested", 1, "Nested", TextContentKind.ObjectName,
    new[] { new TextRecipeStage("nested", NestedTableStage.StageKind, Settings: nestedSettings) });
var nestedOutput = await new TextGenerationEngine(registry).GenerateAsync(nestedRecipe, seed: 10);
Check(nestedOutput.PrimaryText == "The Ashen Crown", "nested table expansion");

// Deterministic recipe generation.
var engine = new TextGenerationEngine(registry);
var recipe = BuiltInPhase1Recipes.FantasySpellName();
var one = await engine.GenerateAsync(recipe, seed: 777);
var two = await engine.GenerateAsync(recipe, seed: 777);
Check(one.PrimaryText == two.PrimaryText, "recipe determinism");
Check(!string.IsNullOrWhiteSpace(one.PrimaryText), "spell recipe produces output");
Check(one.Provenance?.RecipeId == recipe.Id, "recipe provenance");

// Every starter recipe validates and generates without errors for a fixed seed.
foreach (var starter in BuiltInPhase1Recipes.All)
{
    Check(TextRecipeValidator.Validate(starter).All(x => x.Severity != GenerationDiagnosticSeverity.Error), "starter validates " + starter.Id);
    var output = await engine.GenerateAsync(starter, seed: 12345);
    Check(!string.IsNullOrWhiteSpace(output.PrimaryText), "starter output " + starter.Id);
    Check(output.Diagnostics.All(x => x.Severity != GenerationDiagnosticSeverity.Error), "starter diagnostics " + starter.Id);
}

// Constraints can hard-fail or warn.
var constraintSettings = JsonSerializer.SerializeToElement(new { minLength = 10, mode = "error" }, RecipeJson.Options).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
var shortRecipe = new TextRecipe("test.constraint", 1, "Constraint Test", TextContentKind.Custom, new[]
{
    new TextRecipeStage("make", RandomListStage.StageKind, Settings: JsonSerializer.SerializeToElement(new { items = new[] { "tiny" } }, RecipeJson.Options).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())),
    new TextRecipeStage("check", ConstraintStage.StageKind, Settings: constraintSettings)
});
var constrained = await engine.GenerateAsync(shortRecipe, seed: 5);
Check(constrained.Diagnostics.Any(x => x.Code == GenerationDiagnosticCodes.ConstraintFailed && x.Severity == GenerationDiagnosticSeverity.Error), "hard constraint failure");

// Exported recipes round-trip through the authoring store/library.
var exportRoot = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase1-" + Guid.NewGuid().ToString("N"));
await BuiltInRecipeExporter.ExportPhase1Async(exportRoot);
var library = await new TextRecipeLibrary().LoadDirectoryAsync(exportRoot);
Check(library.Count == BuiltInPhase1Recipes.All.Count, "starter recipe export count");
Check(library.All(x => x.Recipe.Fingerprint().Length == 64), "exported recipe fingerprints");
Directory.Delete(exportRoot, true);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 1 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 1 tests passed.");
return 0;

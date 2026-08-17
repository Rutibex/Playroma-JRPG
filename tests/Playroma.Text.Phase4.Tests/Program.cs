using Playroma.Text.Core.Generation;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(BuiltInRecipeCatalog.All.Count >= 59, "unified built-in recipe catalog");
Check(BuiltInRecipeCatalog.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInRecipeCatalog.All.Count, "built-in ids unique");


var adjustable = BuiltInPhase4Recipes.AdjustableNpcGreeting();
var adjustableParameters = RecipeParameterDiscovery.Discover(adjustable);
Check(adjustableParameters.ContainsKey("mood"), "Phase 4 adjustable Tracery parameter exposed");

var nameRecipe = BuiltInPhase2Recipes.Country();
var nameParameters = RecipeParameterDiscovery.Discover(nameRecipe);
Check(nameParameters.TryGetValue("culture", out var culture) && culture.Type == RecipeParameterType.Choice, "name culture parameter inferred");

var traceryParameters = RecipeParameterDiscovery.Discover(BuiltInPhase3Recipes.FriendlyGreeting());
Check(!traceryParameters.ContainsKey("mood"), "fixed Tracery parameter not exposed");

var root = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase4-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "data"));
try
{
    using var session = new TextStudioSession(new TextStudioOptions(root, "Phase 4 Test"));
    await session.InitializeAsync();
    Check(session.Recipes.Count >= BuiltInRecipeCatalog.All.Count, "session loads built-ins");

    var spell = session.Recipes.First(x => x.Recipe.Id.Equals("starter.fantasy.spell-name", StringComparison.OrdinalIgnoreCase));
    session.SelectRecipe(spell);
    session.Seed = 123456789UL;
    session.SeedLocked = true;
    await session.GenerateAsync(1);
    var first = session.Results.Single();
    Check(!string.IsNullOrWhiteSpace(first.Text), "playground generates text");
    await session.GenerateAsync(1);
    var repeated = session.Results.Single();
    Check(first.Text == repeated.Text && first.Seed == repeated.Seed, "locked seed reproduces result");

    session.ToggleLock(repeated.Id);
    await session.GenerateAsync(20);
    Check(session.Results.Count == 20, "generate 20 result count");
    Check(session.Results.Any(x => x.Id == repeated.Id && x.IsLocked), "locked result survives reroll");
    Check(session.Results.Select(x => x.Seed).Distinct().Count() == session.Results.Count, "locked result does not duplicate deterministic candidate seed");

    var source = session.Results.First(x => !x.IsLocked);
    var varied = await session.VaryAsync(source.Id);
    Check(varied is not null && varied.Seed != source.Seed && varied.Variation == 1, "result variation derives new seed");

    var favTarget = session.Results.First();
    await session.ToggleFavoriteAsync(favTarget.Id);
    Check(session.Favorites.Count == 1, "favorite added");
    await session.SaveStateAsync();

    using var reload = new TextStudioSession(new TextStudioOptions(root, "Phase 4 Test"));
    await reload.InitializeAsync();
    Check(reload.Favorites.Count == 1, "favorites persist");
    Check(reload.History.Count > 0, "history persists");

    var editor = new RecipeEditorDocument(spell.Recipe, true, null);
    editor.AddStage(ConstraintStage.StageKind);
    Check(editor.Working.Stages.Count == spell.Recipe.Stages.Count + 1, "recipe designer adds stage");
    var copy = editor.CreateProjectCopy();
    Check(copy.Id.EndsWith(".custom", StringComparison.OrdinalIgnoreCase), "built-in save creates project copy id");
    var savedPath = await reload.SaveRecipeAsync(copy);
    Check(File.Exists(savedPath), "project recipe saved");
    Check(reload.Recipes.Any(x => x.Recipe.Id.Equals(copy.Id, StringComparison.OrdinalIgnoreCase) && !x.IsBuiltIn), "project recipe reloads");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 4 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 4 tests passed.");
return 0;

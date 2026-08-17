using System.Text.Json;
using Playroma.JRPG.Editor.Shell;
using Playroma.Text.Core.Generation;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Generators.Stages;
using Playroma.Text.Generators;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Text engine version marker available on current engine");
Check(ShellCommandIds.ToolsOpenTextStudio == "Tools.OpenTextStudio", "Text Studio shell command ID");
Check(EditorWindowKind.TextStudio.ToString() == "TextStudio", "Text Studio remains a first-class editor window kind");
Check(GeneratorCatalog.CreatePhase7Registry().Find(RandomListStage.StageKind) is not null, "Phase 7 registry preserves earlier generation stages");

var basic = TextStudioProjectContextSnapshot.CreateBasic("C:/Games/Test", "Test RPG");
Check((string?)basic.Values["project.title"] == "Test RPG", "basic project context exposes title");
Check(basic.EffectiveSummary.Count >= 2, "basic project context summary");

var workspace = new TextStudioWorkspaceState(
    SelectedRecipeId: "phase6.semantic.npc-greeting",
    SearchText: "greeting",
    Category: "Dialogue",
    Mode: TextStudioWorkspaceMode.RecipeDesigner,
    Seed: 123456UL,
    SeedLocked: true);
var workspaceJson = JsonSerializer.Serialize(workspace, RecipeJson.Options);
var workspaceRoundTrip = JsonSerializer.Deserialize<TextStudioWorkspaceState>(workspaceJson, RecipeJson.Options);
Check(workspaceRoundTrip?.Mode == TextStudioWorkspaceMode.RecipeDesigner && workspaceRoundTrip.Seed == 123456UL, "workspace state JSON round trip");

var templateSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
{
    ["items"] = JsonSerializer.SerializeToElement(new[] { "Context probe" }),
    ["field"] = JsonSerializer.SerializeToElement("text"),
    ["isPrimary"] = JsonSerializer.SerializeToElement(true)
};
var semanticSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
{
    ["fromContext"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
    {
        ["projectName"] = "project.title",
        ["actorCount"] = "project.database.actors.count"
    })
};
var contextRecipe = new TextRecipe(
    "phase7.tests.project-context",
    1,
    "Project Context Probe",
    TextContentKind.Custom,
    [
        new TextRecipeStage("text", RandomListStage.StageKind, Settings: templateSettings),
        new TextRecipeStage("context", SemanticAnnotateStage.StageKind, Settings: semanticSettings)
    ],
    Description: "Verifies that Phase 7 host context reaches the generation pipeline.");

var root = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase7-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "data"));
try
{
    using var session = new TextStudioSession(new TextStudioOptions(root, "Initial Project"));
    await session.InitializeAsync();
    var context = new TextStudioProjectContextSnapshot(
        root,
        "Native Project",
        7,
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["project.root"] = root,
            ["project.title"] = "Native Project",
            ["project.database.actors.count"] = 42,
            ["project.maps"] = new[] { "Town", "Forest" }
        },
        [new("Actors", "42", "Database")]);
    session.UpdateProjectContext(context);
    var path = await session.SaveRecipeAsync(contextRecipe);
    Check(File.Exists(path), "project-context recipe saved through Text Studio catalog");
    Check(session.SelectedRecipe?.Recipe.Id == contextRecipe.Id, "saved recipe selected after reload");
    session.Seed = 777UL;
    session.SeedLocked = true;
    await session.GenerateAsync(1);
    var result = session.Results.Single().Bundle;
    Check(result.PrimaryText == "Context probe", "project-context recipe generated");
    Check(result.Features.TryGetValue("projectName", out var projectName) && projectName.DisplayText == "Native Project", "host project title injected into generation context");
    Check(result.Features.TryGetValue("actorCount", out var actorCount) && actorCount.DisplayText == "42", "database count injected into generation context");

    var otherRoot = Path.Combine(Path.GetTempPath(), "AnotherProject");
    bool mismatchRejected = false;
    try { session.UpdateProjectContext(TextStudioProjectContextSnapshot.CreateBasic(otherRoot, "Wrong Project")); }
    catch (InvalidOperationException) { mismatchRejected = true; }
    Check(mismatchRejected, "Text Studio refuses in-place cross-project context switching");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

var originalRecipe = contextRecipe;
var editedRecipe = contextRecipe with { Name = "Edited Context Probe" };
var recipeRecovery = new RecipeDesignerRecoveryState(originalRecipe, editedRecipe, false, "C:/test/recipe.playtext.json");
var localRecovery = new TextStudioLocalRecoveryState(
    TextStudioLocalRecoveryState.CurrentVersion,
    recipeRecovery,
    new CorpusLabRecoveryState("{\"id\":\"draft\"}", "C:/test/draft.playcorpus.json"),
    workspace);
var recoveryJson = JsonSerializer.Serialize(localRecovery, RecipeJson.Options);
var recoveryRoundTrip = JsonSerializer.Deserialize<TextStudioLocalRecoveryState>(recoveryJson, RecipeJson.Options);
Check(recoveryRoundTrip?.Recipe?.Working.Name == "Edited Context Probe", "recipe recovery state round trip");
Check(recoveryRoundTrip?.Corpus?.CorpusJson.Contains("draft", StringComparison.Ordinal) == true, "corpus recovery payload round trip");

var recoveredDocument = new RecipeEditorDocument(
    recipeRecovery.Original,
    recipeRecovery.Working,
    recipeRecovery.IsBuiltIn,
    recipeRecovery.Path);
Check(recoveredDocument.IsDirty, "recovered recipe document retains dirty baseline");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 7 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 7 tests passed.");
return 0;

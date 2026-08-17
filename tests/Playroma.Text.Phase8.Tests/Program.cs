using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;
using Playroma.Text.RpgMakerMV;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Text engine version marker available on current engine");
Check(GeneratorCatalog.CreatePhase8Registry().Find(RandomListStage.StageKind) is not null, "Phase 8 registry preserves generation stack");
Check(BuiltInPhase8Recipes.All.Count >= 14, "Phase 8 database recipe coverage");
Check(BuiltInPhase8Recipes.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInPhase8Recipes.All.Count, "Phase 8 recipe IDs unique");

string[] supported = ["Actors", "Classes", "Skills", "Items", "Weapons", "Armors", "Enemies", "Troops", "States", "Animations", "Tilesets", "CommonEvents"];
foreach (string kind in supported)
{
    Check(MvDatabaseTextTargetCatalog.SupportsKind(kind), "database target catalog supports " + kind);
    Check(MvDatabaseTextTargetCatalog.ForKind(kind).Count > 0, "database target catalog exposes fields for " + kind);
}
Check(MvDatabaseTextTargetCatalog.Find("Skills", "description")?.RecommendedRecipeIds?.Contains("phase8.mv.skill-text") == true, "Skill description recommends complete Skill bundle");
Check(MvDatabaseTextTargetCatalog.Find("Enemies", "note")?.RecommendedRecipeIds?.Contains("phase8.mv.enemy-text") == true, "Enemy Note recommends complete Enemy bundle");
Check(MvDatabaseTextTargetCatalog.Find("Items", "note")?.RecommendedRecipeIds?.Contains("phase8.mv.item-text") != true, "Item Note does not recommend bundle that lacks a Note field");

var skillFields = new[]
{
    new TextTargetDescriptor("database.skills", "data/Skills.json", "7", "/name", "name", "Name", "Fire", false, false, 64, FieldKey: "name"),
    new TextTargetDescriptor("database.skills", "data/Skills.json", "7", "/description", "description", "Description", "Old description", true, false, 300, FieldKey: "description"),
    new TextTargetDescriptor("database.skills", "data/Skills.json", "7", "/message1", "message1", "Message 1", "%1 casts Fire!", false, false, 160, FieldKey: "message1"),
    new TextTargetDescriptor("database.skills", "data/Skills.json", "7", "/message2", "message2", "Message 2", "", false, false, 160, FieldKey: "message2")
};
var skillTarget = new TextRecordTargetDescriptor("database.skills", "data/Skills.json", "7", "Skill 0007 — Fire", skillFields, "name");
var skillBundle = new GeneratedTextBundle
{
    Kind = TextContentKind.Skill,
    PrimaryText = "Glacial Requiem",
    Fields = new[]
    {
        new GeneratedField("name", "Glacial Requiem", "name", true),
        new GeneratedField("description", "Freezes all enemies.", "description"),
        new GeneratedField("message1", "%1 invokes Glacial Requiem!", "message1"),
        new GeneratedField("message2", "A frozen wind engulfs the battlefield!", "message2")
    }
};
var skillPlan = TextImportPlanner.Build(skillTarget, skillBundle,
    skillFields.Select(field => new TextImportFieldSelection(field.SemanticField, true, TextImportMode.Replace)).ToArray());
Check(skillPlan.CanApply, "multi-field Skill import plan is applicable");
Check(skillPlan.Changes.Count == 4, "multi-field Skill bundle maps all four text fields");
Check(skillPlan.Changes.Single(x => x.SemanticField == "description").NewValue == "Freezes all enemies.", "Skill description maps by semantic field");
Check(skillPlan.Changes.Single(x => x.SemanticField == "message1").NewValue.StartsWith("%1", StringComparison.Ordinal), "Skill message placeholder retained in generated bundle");

var noteField = new TextTargetDescriptor("database.enemies", "data/Enemies.json", "4", "/note", "note", "Note", "<AI Level: 8>\nExisting notes", true, true, FieldKey: "note");
var noteTarget = new TextRecordTargetDescriptor("database.enemies", "data/Enemies.json", "4", "Enemy 0004", [noteField], "note");
var noteBundle = new GeneratedTextBundle { PrimaryText = "A territorial cave predator.", Fields = [new GeneratedField("note", "A territorial cave predator.", "note", true)] };
var blockedNote = TextImportPlanner.Build(noteTarget, noteBundle, [new TextImportFieldSelection("note", true, TextImportMode.Replace)]);
Check(!blockedNote.CanApply && blockedNote.Issues.Any(x => x.Code == "PTXTMV0001" && x.IsError), "plugin-tag Note replacement is blocked");
var appendedNote = TextImportPlanner.Build(noteTarget, noteBundle, [new TextImportFieldSelection("note", true, TextImportMode.Append)]);
Check(appendedNote.CanApply && appendedNote.Changes.Single().NewValue.EndsWith("A territorial cave predator.", StringComparison.Ordinal), "plugin-tag Note append remains available");

var blankTarget = new TextRecordTargetDescriptor("database.actors", "data/Actors.json", "2", "Actor 0002",
    [new TextTargetDescriptor("database.actors", "data/Actors.json", "2", "/nickname", "nickname", "Nickname", "", false, false, FieldKey: "nickname")], "nickname");
var fillPlan = TextImportPlanner.Build(blankTarget, new GeneratedTextBundle { PrimaryText = "Sparrow" }, [new TextImportFieldSelection("nickname", true, TextImportMode.FillIfBlank)]);
Check(fillPlan.CanApply && fillPlan.Changes.Single().NewValue == "Sparrow", "FillIfBlank fills empty database text");

var creation = new TextImportRecordCreation("data/Skills.json", 11, "{\"id\":11}", "Skill 11");
var createPlan = TextImportPlanner.Build(skillTarget, skillBundle,
    [new TextImportFieldSelection("name", true), new TextImportFieldSelection("description", true)], creation);
Check(createPlan.CanApply && createPlan.Creation?.RecordId == 11, "Create New Record import plan retains record creation metadata");
Check(createPlan.Changes.All(x => x.RecordId == "11" && x.OldValue.Length == 0), "created-record field changes target new record and empty baseline");

var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase8Registry());
foreach (var recipe in BuiltInPhase8Recipes.All)
{
    var bundle = await engine.GenerateAsync(recipe, new GenerationContext("phase8-tests"), SeedFactory.Derive(8800UL, recipe.Id));
    Check(!string.IsNullOrWhiteSpace(bundle.PrimaryText), "Phase 8 built-in generates: " + recipe.Id);
    Check(!bundle.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), "Phase 8 built-in has no generation errors: " + recipe.Id);
}
var generatedSkill = await engine.GenerateAsync(BuiltInPhase8Recipes.CompleteSkillText(), new GenerationContext("phase8-tests"), 812345UL);
string[] skillSemanticFields = ["name", "description", "message1", "message2"];
Check(skillSemanticFields.All(name => generatedSkill.Fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || string.Equals(field.SemanticRole, name, StringComparison.OrdinalIgnoreCase))), "complete Skill recipe emits all importable text semantics");
var generatedState = await engine.GenerateAsync(BuiltInPhase8Recipes.CompleteStateText(), new GenerationContext("phase8-tests"), 812346UL);
Check(new[] { "message1", "message2", "message3", "message4" }.All(name => generatedState.Fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))), "complete State recipe emits all four MV state messages");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 8 tests FAILED:");
    foreach (string failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 8 tests passed.");
return 0;

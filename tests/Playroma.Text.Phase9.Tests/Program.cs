using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;
using Playroma.Text.RpgMakerMV;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }
int Code(string raw) { using JsonDocument json = JsonDocument.Parse(raw); return json.RootElement.GetProperty("code").GetInt32(); }
int Indent(string raw) { using JsonDocument json = JsonDocument.Parse(raw); return json.RootElement.GetProperty("indent").GetInt32(); }
JsonElement Parameters(string raw) { using JsonDocument json = JsonDocument.Parse(raw); return json.RootElement.GetProperty("parameters").Clone(); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Phase 9 engine/version marker remains available on current engine");
Check(GeneratorCatalog.CreatePhase9Registry().Find(RandomListStage.StageKind) is not null, "Phase 9 registry preserves generation stack");
Check(BuiltInPhase9Recipes.All.Count >= 12, "Phase 9 map/event recipe coverage");
Check(BuiltInPhase9Recipes.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInPhase9Recipes.All.Count, "Phase 9 recipe IDs unique");
Check(MvMapTextTargets.Specs.Count == 3, "Map target catalog exposes editor name, display name, and note");
Check(MvMapTextTargets.Specs.Single(x => x.Key == "note").IsNote, "Map note retains Note safety semantics");

var analysis = MvMessageSyntax.Analyze(@"Hello \N[1]! \C[2]HP: \V[3]\C[0] \I[12] \G");
Check(analysis.ControlCodes.Count >= 6, "MV parser identifies standard message controls");
Check(!analysis.HasUnknownControlCodes, "standard MV message controls are known");
Check(MvMessageSyntax.Analyze(@"Test \ZZ[3]").HasUnknownControlCodes, "unknown MV control codes are preserved and reported");
Check(MvMessageSyntax.EstimateVisibleColumns(@"\C[2]Hello\C[0]") == 5, "format controls do not inflate visible width");

var pages = MvMessagePaginator.Paginate("one\ntwo\nthree\nfour\nfive");
Check(pages.Count == 2 && pages[0].Lines.Count == 4 && pages[1].Lines.Count == 1, "Show Text auto-paginates at four lines");
var wrapped = MvMessagePaginator.Paginate("This is a deliberately long RPG Maker message line that should wrap into more than one visible line in the preview.", 4, 28);
Check(wrapped.SelectMany(x => x.Lines).Count() > 1, "long message line word-wraps");

var show = MvEventTextCompiler.CompileShowText("Line 1\nLine 2\nLine 3\nLine 4\nLine 5", 2);
Check(show.CanApply, "Show Text plan applicable");
Check(show.RawCommands.Count(x => Code(x) == 101) == 2, "five lines compile into two Show Text openings");
Check(show.RawCommands.Count(x => Code(x) == 401) == 5, "Show Text preserves all five continuation lines");
Check(show.RawCommands.All(x => Indent(x) == 2), "Show Text command indent preserved");
var styledShow = MvEventTextCompiler.CompileShowText("Portrait test", 0, new MvShowTextSettings("Actor1", 3, 1, 0));
JsonElement showParams = Parameters(styledShow.RawCommands[0]);
Check(showParams[0].GetString() == "Actor1" && showParams[1].GetInt32() == 3 && showParams[2].GetInt32() == 1 && showParams[3].GetInt32() == 0, "Show Text presentation settings compile intact");

var scroll = MvEventTextCompiler.CompileScrollingText("A\nB\nC", 1);
Check(Code(scroll.RawCommands[0]) == 105 && scroll.RawCommands.Skip(1).All(x => Code(x) == 405), "Scrolling Text compiles to 105/405");
var styledScroll = MvEventTextCompiler.CompileScrollingText("Scroll", 0, new MvScrollingTextSettings(6, true));
JsonElement scrollParams = Parameters(styledScroll.RawCommands[0]);
Check(scrollParams[0].GetInt32() == 6 && scrollParams[1].GetBoolean(), "Scrolling Text presentation settings compile intact");
var comment = MvEventTextCompiler.CompileComment("First\nSecond", 3);
Check(Code(comment.RawCommands[0]) == 108 && Code(comment.RawCommands[1]) == 408, "Comment compiles to 108/408");

var validChoices = Enumerable.Range(1, 6).Select(i => new MvGeneratedChoice("Choice " + i, "Response " + i)).ToArray();
var choices = MvEventTextCompiler.CompileChoices(validChoices, 0);
Check(choices.CanApply, "six choices are valid");
Check(Code(choices.RawCommands[0]) == 102 && choices.RawCommands.Count(x => Code(x) == 402) == 6 && Code(choices.RawCommands[^1]) == 404, "Show Choices compiles opening, branches, closing");
var styledChoices = MvEventTextCompiler.CompileChoices([new MvGeneratedChoice("Yes"), new MvGeneratedChoice("No")], 0, new MvChoiceSettings(-1, 1, 1, 2));
JsonElement choiceParams = Parameters(styledChoices.RawCommands[0]);
Check(choiceParams[1].GetInt32() == -1 && choiceParams[2].GetInt32() == 1 && choiceParams[3].GetInt32() == 1 && choiceParams[4].GetInt32() == 2, "Show Choices presentation settings compile intact");
var tooMany = MvEventTextCompiler.CompileChoices(Enumerable.Range(1, 7).Select(i => new MvGeneratedChoice("Choice " + i)).ToArray());
Check(!tooMany.CanApply && tooMany.Issues.Any(x => x.Code == "PTXTMV0902" && x.IsError), "seven choices blocked by MV six-choice limit");

var branchBundle = new GeneratedTextBundle
{
    Kind = TextContentKind.Dialogue,
    PrimaryText = "What do you want?",
    Fields =
    [
        new GeneratedField("choice1", "Ask about town", "choice1"), new GeneratedField("response1", "Quiet place.", "response1"),
        new GeneratedField("choice2", "Ask about work", "choice2"), new GeneratedField("response2", "See the captain.", "response2"),
        new GeneratedField("choice3", "Leave", "choice3"), new GeneratedField("response3", "Safe travels.", "response3")
    ]
};
var branch = MvEventTextCompiler.CompileBundle(branchBundle, MvGeneratedEventTextKind.BranchingDialogue);
Check(branch.CanApply, "branching dialogue bundle applicable");
Check(branch.RawCommands.Any(x => Code(x) == 101) && branch.RawCommands.Any(x => Code(x) == 102) && branch.RawCommands.Count(x => Code(x) == 402) == 3, "branching dialogue compiles prompt + three choices");
Check(MvEventTextCompiler.ExtractChoices(branchBundle).Count == 3, "generated choice fields extracted from bundle");

var mapFields = new[]
{
    new TextTargetDescriptor("map", "data/MapInfos.json", "map:1", "/1/name", "name", "Editor Name", "Old Map", false, false, FieldKey:"editorName"),
    new TextTargetDescriptor("map", "data/Map001.json", "map:1", "/displayName", "displayName", "Display Name", "Old Display", false, false, FieldKey:"displayName"),
    new TextTargetDescriptor("map", "data/Map001.json", "map:1", "/note", "note", "Map Note", "", true, true, FieldKey:"note")
};
var mapTarget = new TextRecordTargetDescriptor("map", "data/Map001.json", "map:1", "Map 001 — Old Map", mapFields, "displayName");
var mapBundle = new GeneratedTextBundle
{
    Kind = TextContentKind.PlaceName,
    PrimaryText = "Whispering Vale",
    Fields =
    [
        new GeneratedField("name", "Whispering Vale", "name", true),
        new GeneratedField("displayName", "Whispering Vale", "displayName"),
        new GeneratedField("note", "A quiet forest road.", "note")
    ]
};
var mapPlan = TextImportPlanner.Build(mapTarget, mapBundle, mapFields.Select(x => new TextImportFieldSelection(x.SemanticField, true)).ToArray());
Check(mapPlan.CanApply && mapPlan.Changes.Count == 3, "map bundle plans all three text changes");
Check(mapPlan.Changes.Any(x => x.DataFile.EndsWith("MapInfos.json", StringComparison.OrdinalIgnoreCase) && x.JsonPath == "/1/name"), "map editor name targets MapInfos");
Check(mapPlan.Changes.Count(x => x.DataFile.EndsWith("Map001.json", StringComparison.OrdinalIgnoreCase)) == 2, "map display name/note target Map file");

var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase9Registry());
foreach (TextRecipe recipe in BuiltInPhase9Recipes.All)
{
    GeneratedTextBundle generated = await engine.GenerateAsync(recipe, new GenerationContext("phase9-tests"), SeedFactory.Derive(9900UL, recipe.Id));
    Check(!string.IsNullOrWhiteSpace(generated.PrimaryText), "Phase 9 built-in generates: " + recipe.Id);
    Check(!generated.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), "Phase 9 built-in has no errors: " + recipe.Id);
}
var generatedMap = await engine.GenerateAsync(BuiltInPhase9Recipes.MapText(), new GenerationContext("phase9-tests"), 991122UL);
Check(new[] { "name", "displayName", "note" }.All(name => generatedMap.Fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))), "map recipe emits name/displayName/note");
var generatedBranch = await engine.GenerateAsync(BuiltInPhase9Recipes.BranchingDialogue(), new GenerationContext("phase9-tests"), 991123UL);
Check(MvEventTextCompiler.ExtractChoices(generatedBranch).Count >= 3, "branching recipe emits at least three choices");
Check(MvEventTextCompiler.CompileBundle(generatedBranch, MvGeneratedEventTextKind.BranchingDialogue).CanApply, "generated branching recipe compiles to valid MV block");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 9 tests FAILED:");
    foreach (string failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 9 tests passed.");
return 0;

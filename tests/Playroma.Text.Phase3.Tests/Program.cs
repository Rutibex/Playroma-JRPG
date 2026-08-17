using System.Text.Json;
using Playroma.Text.Authoring;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Stages;
using Playroma.Text.Generators.Tracery;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var registry = GeneratorCatalog.CreatePhase3Registry();
Check(registry.Kinds.Contains(TraceryGrammarStage.StageKind, StringComparer.OrdinalIgnoreCase), "phase3 tracery stage registered");

var classicJson = """
{
  "origin": ["#hello.capitalize#, #place#!"],
  "hello": ["hello", "greetings"],
  "place": ["world", "traveler"]
}
""";
var classic = TraceryGrammarJson.Parse(classicJson, "test.classic", "Classic Test");
Check(classic.Compatibility == TraceryCompatibilityMode.Classic, "classic JSON detected");
Check(classic.Symbols.Count == 3, "classic JSON symbols parsed");
var classicRoundTrip = TraceryGrammarJson.Parse(TraceryGrammarJson.SerializeClassic(classic), "roundtrip", "Roundtrip");
Check(classicRoundTrip.Symbols["hello"].Count == 2, "classic JSON roundtrip");

var grammarEngine = new TraceryEngine();
var budget = new GenerationBudget(new GenerationBudgetOptions(MaxTraceNodes: 20000));
var one = grammarEngine.Expand(classic, new GenerationContext(), new Xoshiro256StarStar(77), budget);
var two = grammarEngine.Expand(classic, new GenerationContext(), new Xoshiro256StarStar(77), new GenerationBudget());
Check(one.Success && one.Text == two.Text, "Tracery deterministic expansion");
Check(one.Text.EndsWith('!'), "classic expansion output");
var rawRule = grammarEngine.ExpandRule(classic, "Raw says #hello# to the #place#.", new GenerationContext(), new Xoshiro256StarStar(77), new GenerationBudget());
Check(rawRule.Success && rawRule.Text.StartsWith("Raw says ", StringComparison.Ordinal) && rawRule.Text.EndsWith(".", StringComparison.Ordinal), "arbitrary raw-rule expansion");

var binding = BuiltInPhase3Grammars.BindingDemo();
var bound = grammarEngine.Expand(binding, new GenerationContext(), new Xoshiro256StarStar(1001), new GenerationBudget());
Check(bound.Success, "scoped binding expansion succeeds");
var firstName = binding.Symbols["name"].Select(x => x.Text).FirstOrDefault(n => bound.Text.StartsWith(n + " ", StringComparison.Ordinal));
Check(firstName is not null && bound.Text.Count(c => c == firstName![0]) >= 1, "binding output formed");
// Strong consistency test: selected hero appears twice.
if (firstName is not null) Check(bound.Text.Split(new[] { firstName }, StringSplitOptions.None).Length - 1 == 2, "scoped action reuses hero consistently");

var popGrammar = TraceryGrammarJson.Parse("""
{
  "format":"playroma-tracery",
  "id":"pop",
  "version":1,
  "name":"POP Test",
  "symbols":{
    "origin": ["[x:temporary]#x#[x:POP] #x#"],
    "x": ["base"]
  }
}
""");
var popped = grammarEngine.Expand(popGrammar, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget());
Check(popped.Success && popped.Text == "temporary base", "push and POP actions");

var nestedActionGrammar = TraceryGrammarJson.Parse("""
{
  "origin":["#[[pet:owl]hero:#name#]story#"],
  "name":["Mira"],
  "story":["#hero# #pet#"]
}
""", "nested-actions", "Nested Actions");
var nestedActionOutput = grammarEngine.Expand(nestedActionGrammar, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget());
Check(nestedActionOutput.Success && nestedActionOutput.Text == "Mira owl", "classic nested sub-actions");

var classicBrackets = TraceryGrammarJson.Parse("""
{"origin":["[x:temporary] #x#"],"x":["base"]}
""", "classic-brackets", "Classic Brackets");
var classicBracketOutput = grammarEngine.Expand(classicBrackets, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget());
Check(classicBracketOutput.Success && classicBracketOutput.Text == "[x:temporary] base", "classic top-level brackets remain literal");

var conditioned = BuiltInPhase3Grammars.NpcGreeting();
var hostile = grammarEngine.Expand(conditioned, new GenerationContext(), new Xoshiro256StarStar(9), new GenerationBudget(),
    new Dictionary<string, object?> { ["mood"] = "hostile" });
Check(hostile.Success && hostile.Metadata.TryGetValue("tone", out var tone) && tone == "hostile", "conditional rules and metadata");

var modifiedGrammar = TraceryGrammarJson.Parse("""
{"origin":["#animal.a.capitalize# / #animal.s# / #verb.ed#"],"animal":["owl"],"verb":["carry"]}
""", "mods", "Modifiers");
var mods = grammarEngine.Expand(modifiedGrammar, new GenerationContext(), new Xoshiro256StarStar(3), new GenerationBudget());
Check(mods.Success && mods.Text.StartsWith("An owl / owls / carried", StringComparison.Ordinal), "classic modifiers");


var parameterSymbol = TraceryGrammarJson.Parse("""
{
  "format":"playroma-tracery",
  "id":"param-symbol",
  "version":1,
  "name":"Parameter Symbol",
  "parameters":{"element":{"type":"choice","defaultValue":"ice","choices":["ice","fire"]}},
  "symbols":{"origin":["A spell of #element#."]}
}
""");
var parameterExpanded = grammarEngine.Expand(parameterSymbol, new GenerationContext(), new Xoshiro256StarStar(2), new GenerationBudget(), new Dictionary<string, object?> { ["element"] = "fire" });
Check(parameterExpanded.Success && parameterExpanded.Text == "A spell of fire.", "typed parameter exposed as symbol");

var wrapperClassic = TraceryGrammarJson.Parse("""
{"symbols":{"origin":"#x#","x":"wrapped"}}
""", "wrapper", "Wrapper");
var wrapperOutput = grammarEngine.Expand(wrapperClassic, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget());
Check(wrapperOutput.Success && wrapperOutput.Text == "wrapped", "classic symbols wrapper and string rules");

var missing = TraceryGrammarJson.Parse("{\"origin\":[\"#missing#\"]}", "missing", "Missing");
var missingResult = grammarEngine.Expand(missing, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget());
Check(!missingResult.Success && missingResult.Diagnostics.Any(x => x.Code == GenerationDiagnosticCodes.TraceryMissingSymbol), "missing symbol diagnostic");

var recursive = TraceryGrammarJson.Parse("{\"origin\":[\"#origin#\"]}", "recursive", "Recursive");
var recurseResult = grammarEngine.Expand(recursive, new GenerationContext(), new Xoshiro256StarStar(1), new GenerationBudget(), options: new TraceryExpansionOptions(MaxDepth: 12, MaxNodes: 100));
Check(!recurseResult.Success && recurseResult.Diagnostics.Any(x => x.Code == GenerationDiagnosticCodes.TraceryRecursionLimit), "recursion guard");
var staticAnalysis = TraceryGrammarAnalyzer.Analyze(recursive);
Check(staticAnalysis.Any(x => x.Code == GenerationDiagnosticCodes.TraceryStaticCycle), "static recursion-cycle diagnostic");

var textEngine = new TextGenerationEngine(registry);
foreach (var recipe in BuiltInPhase3Recipes.All)
{
    Check(TextRecipeValidator.Validate(recipe).All(x => x.Severity != GenerationDiagnosticSeverity.Error), "phase3 recipe validates " + recipe.Id);
    var output = await textEngine.GenerateAsync(recipe, seed: 20260806, budgetOptions: new GenerationBudgetOptions(MaxTraceNodes: 50000, MaxOutputCharacters: 100000));
    Check(!string.IsNullOrWhiteSpace(output.PrimaryText), "phase3 recipe output " + recipe.Id);
    Check(output.Diagnostics.All(x => x.Severity != GenerationDiagnosticSeverity.Error), "phase3 recipe diagnostics " + recipe.Id);
    Check(output.Trace?.Nodes.Any(x => x.Operation.StartsWith("tracery.", StringComparison.Ordinal)) == true, "Tracery trace bridge " + recipe.Id);
}

var friendlyBundle = await textEngine.GenerateAsync(BuiltInPhase3Recipes.FriendlyGreeting(), seed: 31, budgetOptions: new GenerationBudgetOptions(MaxTraceNodes: 50000));
Check(friendlyBundle.Tags.TryGetValue("tone", out var bundleTone) && bundleTone == "friendly", "rule metadata promoted to bundle tags");

var exportRoot = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase3-" + Guid.NewGuid().ToString("N"));
var grammarDir = Path.Combine(exportRoot, "grammars");
await BuiltInTraceryGrammarExporter.ExportPhase3Async(grammarDir);
var library = await TraceryGrammarLibrary.LoadDirectoryAsync(grammarDir);
Check(library.Count >= BuiltInPhase3Grammars.All.Count, "Tracery grammar export/library load");
var resolved = await library.ResolveAsync("builtin.tracery.classic-story");
Check(resolved is not null, "grammar resolver by id");
Directory.Delete(exportRoot, true);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 3 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 3 tests passed.");
return 0;

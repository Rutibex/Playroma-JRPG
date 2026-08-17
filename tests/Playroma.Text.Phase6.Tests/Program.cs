using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Core.Semantics;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.Generators.Semantics;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var registry = GeneratorCatalog.CreatePhase6Registry();
Check(registry.Kinds.Contains(SemanticAnnotateStage.StageKind), "Phase 6 registry includes semantic annotate");
Check(registry.Kinds.Contains(FeatureUnifyStage.StageKind), "Phase 6 registry includes feature unification");
Check(registry.Kinds.Contains(SemanticConstraintStage.StageKind), "Phase 6 registry includes semantic constraint");
Check(registry.Kinds.Contains(AgreementStage.StageKind), "Phase 6 registry includes agreement");
Check(registry.Kinds.Contains(SemanticScoreStage.StageKind), "Phase 6 registry includes semantic scoring");
Check(BuiltInPhase6Recipes.All.Count >= 8, "Phase 6 built-in recipe coverage");
Check(BuiltInRecipeCatalog.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInRecipeCatalog.All.Count, "all built-in recipe IDs remain unique");

using (var featureDoc = JsonDocument.Parse("{\"tone\":\"ominous\",\"level\":12,\"threat\":0.75,\"boss\":true,\"factions\":[\"mages\",\"royal\"]}"))
{
    var f = featureDoc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => SemanticFeatureValue.FromJsonElement(x.Value), StringComparer.OrdinalIgnoreCase);
    Check(f["tone"].Type == SemanticFeatureType.String && f["tone"].DisplayText == "ominous", "semantic string feature");
    Check(f["level"].Type == SemanticFeatureType.Integer && f["level"].TryGetNumber(out var level) && level == 12, "semantic integer feature");
    Check(f["threat"].Type == SemanticFeatureType.Number && f["threat"].TryGetNumber(out var threat) && Math.Abs(threat - .75) < .0001, "semantic number feature");
    Check(f["boss"].Type == SemanticFeatureType.Boolean && (bool?)f["boss"].ToObject() == true, "semantic boolean feature");
    Check(f["factions"].Type == SemanticFeatureType.StringList && f["factions"].EffectiveValues.Count == 2, "semantic string-list feature");
}

Check(SemanticFeatureUnifier.TryUnify(SemanticFeatureValue.FromString("*"), SemanticFeatureValue.FromString("hostile"), out var wildcard) && wildcard.DisplayText == "hostile", "semantic wildcard unification");
Check(SemanticFeatureUnifier.TryUnify(SemanticFeatureValue.FromStrings(new[] { "mages", "royal" }), SemanticFeatureValue.FromStrings(new[] { "royal", "merchants" }), out var listUnified) && listUnified.DisplayText.Contains("royal", StringComparison.OrdinalIgnoreCase), "semantic list intersection unification");
Check(!SemanticFeatureUnifier.TryUnify(SemanticFeatureValue.FromString("friendly"), SemanticFeatureValue.FromString("hostile"), out _), "semantic conflict detection");

var predicateBundle = new GeneratedTextBundle
{
    PrimaryText = "The gate is guarded.",
    Features = new Dictionary<string, SemanticFeatureValue>(StringComparer.OrdinalIgnoreCase)
    {
        ["tone"] = SemanticFeatureValue.FromString("ominous"),
        ["threat"] = SemanticFeatureValue.FromNumber(.8),
        ["boss"] = SemanticFeatureValue.FromBoolean(true)
    },
    Scores = [new GenerationScore("originality", .91)],
    Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["undead"] = "true" }
};
var predicateContext = new GenerationContext("phase6-tests", Values: new Dictionary<string, object?> { ["tone"] = "ominous", ["minimumThreat"] = .5 }, Tags: new HashSet<string>(new[] { "night" }, StringComparer.OrdinalIgnoreCase));
var predicate = SemanticPredicateEvaluator.Evaluate("feature.tone == context.tone && feature.threat >= context.minimumThreat && score.originality >= 0.9 && has(feature.boss)", predicateContext, predicateBundle);
Check(predicate.Success && predicate.Value, "safe semantic predicate evaluator");
Check(SemanticPredicateEvaluator.Evaluate("tag(\"night\") && tag.undead", predicateContext, predicateBundle).Value, "predicate tag lookup");
Check(!SemanticPredicateEvaluator.Evaluate("feature.tone ==", predicateContext, predicateBundle).Success, "invalid predicate diagnostic path");

var annotateSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
{
    ["features"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["tone"] = "warm", ["threat"] = .2, ["safe"] = true }),
    ["fromContext"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["faction"] = "faction" })
};
var semanticRecipe = new TextRecipe("tests.phase6.semantic-stages", 1, "Semantic Stages", TextContentKind.Dialogue,
[
    new TextRecipeStage("base", RandomListStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["items"] = JsonSerializer.SerializeToElement(new[] { "At the gate, {pronoun:subject} {verb:watch} the road and {be} ready." }),
        ["field"] = JsonSerializer.SerializeToElement("text"), ["isPrimary"] = JsonSerializer.SerializeToElement(true)
    }),
    new TextRecipeStage("annotate", SemanticAnnotateStage.StageKind, Settings: annotateSettings),
    new TextRecipeStage("unify", FeatureUnifyStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["requirements"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["grammar.number"] = "$context.number", ["grammar.person"] = "$context.person", ["grammar.gender"] = "$context.gender", ["tone"] = "warm"
        })
    }),
    new TextRecipeStage("agreement", AgreementStage.StageKind, Settings: new Dictionary<string, JsonElement> { ["target"] = JsonSerializer.SerializeToElement("primary") }),
    new TextRecipeStage("constraint", SemanticConstraintStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["predicate"] = JsonSerializer.SerializeToElement("feature.tone == 'warm' && feature.safe == true && feature.grammar.number == context.number")
    }),
    new TextRecipeStage("score", SemanticScoreStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["scorers"] = JsonSerializer.SerializeToElement(new[] { new CandidateScorerDefinition("coverage", CandidateScorerKind.FeatureCoverage, 1, Target: "tone,grammar.number,grammar.gender") }, RecipeJson.Options)
    })
]);
var semanticContext = new GenerationContext("phase6-tests", Values: new Dictionary<string, object?>
{
    ["faction"] = "North Watch", ["number"] = "singular", ["person"] = 3, ["gender"] = "female"
});
var textEngine = new TextGenerationEngine(registry);
var semanticBundle = await textEngine.GenerateAsync(semanticRecipe, semanticContext, 101UL);
Check(semanticBundle.Features.TryGetValue("tone", out var tone) && tone.DisplayText == "warm", "semantic annotate emits features");
Check(semanticBundle.Features.TryGetValue("faction", out var faction) && faction.DisplayText == "North Watch", "semantic annotate imports context");
Check(semanticBundle.Features.ContainsKey("grammar.number"), "feature unification binds missing grammatical feature");
Check(semanticBundle.PrimaryText.Contains("she watches", StringComparison.OrdinalIgnoreCase) && semanticBundle.PrimaryText.Contains("is ready", StringComparison.OrdinalIgnoreCase), "agreement realizes pronouns and verbs");
Check(!semanticBundle.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), "semantic stage recipe passes constraints");
Check(semanticBundle.Scores.Any(x => x.Name.Equals("coverage", StringComparison.OrdinalIgnoreCase)), "semantic score stage appends score");

var conflictRecipe = semanticRecipe with
{
    Id = "tests.phase6.conflict",
    Stages = semanticRecipe.Stages.Take(2).Concat([
        new TextRecipeStage("conflict", FeatureUnifyStage.StageKind, Settings: new Dictionary<string, JsonElement>
        {
            ["requirements"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["tone"] = "hostile" })
        })
    ]).ToArray()
};
var conflictBundle = await textEngine.GenerateAsync(conflictRecipe, semanticContext, 102UL);
Check(conflictBundle.Diagnostics.Any(x => x.Code == GenerationDiagnosticCodes.SemanticUnificationConflict && x.Severity == GenerationDiagnosticSeverity.Error), "feature conflict produces structured error");

var tracerySymbols = new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
{
    ["origin"] =
    [
        new TraceryRuleDefinition("Welcome.", Features: new Dictionary<string, SemanticFeatureValue> { ["mood"] = SemanticFeatureValue.FromString("friendly"), ["urgency"] = SemanticFeatureValue.FromNumber(.2) }),
        new TraceryRuleDefinition("Leave now.", Features: new Dictionary<string, SemanticFeatureValue> { ["mood"] = SemanticFeatureValue.FromString("hostile"), ["urgency"] = SemanticFeatureValue.FromNumber(.9) })
    ]
};
var traceryDoc = new TraceryGrammarDocument("tests.features", 1, "Feature Grammar", tracerySymbols, Compatibility: TraceryCompatibilityMode.PlayromaExtended);
var serializedGrammar = TraceryGrammarJson.SerializePlayroma(traceryDoc, false);
var parsedGrammar = TraceryGrammarJson.Parse(serializedGrammar, "tests.features");
Check(parsedGrammar.Symbols["origin"][0].EffectiveFeatures.ContainsKey("mood"), "Tracery semantic features JSON round trip");
using var grammarElementDoc = JsonDocument.Parse(serializedGrammar);
var traceryRecipe = new TextRecipe("tests.phase6.tracery-features", 1, "Tracery Feature Emission", TextContentKind.Dialogue,
[
    new TextRecipeStage("grammar", TraceryGrammarStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["grammar"] = grammarElementDoc.RootElement.Clone(), ["field"] = JsonSerializer.SerializeToElement("text"), ["isPrimary"] = JsonSerializer.SerializeToElement(true), ["emitSemanticFeatures"] = JsonSerializer.SerializeToElement(true)
    })
]);
var traceryBundle = await textEngine.GenerateAsync(traceryRecipe, new GenerationContext("phase6-tests"), 33UL);
Check(traceryBundle.Features.ContainsKey("mood") && traceryBundle.Features.ContainsKey("urgency"), "Tracery rule features propagate to generated bundle");

var conditionalFeatureGrammar = new TraceryGrammarDocument("tests.feature-condition", 1, "Feature Condition",
    new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
    {
        ["origin"] = [new TraceryRuleDefinition("#setup##response#")],
        ["setup"] = [new TraceryRuleDefinition(string.Empty, Features: new Dictionary<string, SemanticFeatureValue> { ["stance"] = SemanticFeatureValue.FromString("guarded") })],
        ["response"] =
        [
            new TraceryRuleDefinition("Keep your distance.", Condition: "feature.stance=guarded"),
            new TraceryRuleDefinition("Come closer.", Condition: "feature.stance=open")
        ]
    }, Compatibility: TraceryCompatibilityMode.PlayromaExtended);
using var conditionalGrammarElement = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(conditionalFeatureGrammar, false));
var conditionalRecipe = new TextRecipe("tests.feature-condition", 1, "Feature Condition", TextContentKind.Dialogue,
[
    new TextRecipeStage("grammar", TraceryGrammarStage.StageKind, Settings: new Dictionary<string, JsonElement>
    {
        ["grammar"] = conditionalGrammarElement.RootElement.Clone(), ["field"] = JsonSerializer.SerializeToElement("text"), ["isPrimary"] = JsonSerializer.SerializeToElement(true)
    })
]);
var conditionalBundle = await textEngine.GenerateAsync(conditionalRecipe, new GenerationContext("phase6-tests"), 44UL);
Check(conditionalBundle.PrimaryText == "Keep your distance.", "Tracery later rules can condition on features selected earlier in expansion");

var rankedRecipe = BuiltInPhase6Recipes.All.First(x => x.Id == "phase6.semantic.npc-greeting");
var candidateEngine = new CandidateGenerationEngine(textEngine);
var hostileContext = new GenerationContext("phase6-tests", Values: new Dictionary<string, object?> { ["mood"] = "hostile" });
var rankedA = await candidateEngine.GenerateBestAsync(rankedRecipe, hostileContext, 987654UL, new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(10), MaxCandidateAttempts: 20000));
var rankedB = await candidateEngine.GenerateBestAsync(rankedRecipe, hostileContext, 987654UL, new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(10), MaxCandidateAttempts: 20000));
Check(rankedA.PrimaryText == rankedB.PrimaryText, "candidate pipeline deterministic with fixed seed");
Check(rankedA.Features.TryGetValue("mood", out var rankedMood) && rankedMood.DisplayText == "hostile", "candidate semantic filter enforces requested feature");
Check(rankedA.Scores.Any(x => x.Name == "candidate.total") && rankedA.Tags.ContainsKey("candidatePipeline"), "candidate ranking exposes score/provenance tags");
Check(CandidateScoring.TextDiversity("red fox runs", ["red fox runs"]) == 0, "candidate diversity identical text");
Check(CandidateScoring.TextDiversity("red fox runs", ["silver moon sleeps"]) > .9, "candidate diversity unrelated text");

var rankedJson = JsonSerializer.Serialize(rankedRecipe, RecipeJson.Options);
var rankedReload = JsonSerializer.Deserialize<TextRecipe>(rankedJson, RecipeJson.Options);
Check(rankedReload?.CandidatePipeline?.EffectiveCandidateCount == rankedRecipe.CandidatePipeline?.EffectiveCandidateCount, "candidate pipeline recipe JSON round trip");
Check(rankedReload?.CandidatePipeline?.EffectiveScorers.Count == rankedRecipe.CandidatePipeline?.EffectiveScorers.Count, "candidate scorer JSON round trip");

foreach (var recipe in BuiltInPhase6Recipes.All)
{
    var values = recipe.Parameters?.ToDictionary(
        x => x.Key,
        x => x.Value.DefaultValue is { } value ? JsonElementToObject(value) : null,
        StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var context = new GenerationContext("phase6-builtins", Values: values);
    var bundle = await candidateEngine.GenerateBestAsync(recipe, context, SeedFactory.Derive(777UL, recipe.Id),
        new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(12), MaxCandidateAttempts: 30000));
    Check(!string.IsNullOrWhiteSpace(bundle.PrimaryText), "built-in Phase 6 recipe generates: " + recipe.Id);
    Check(!bundle.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), "built-in Phase 6 recipe has no errors: " + recipe.Id);
}

var root = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase6-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "data"));
try
{
    using var studio = new TextStudioSession(new TextStudioOptions(root, "Phase 6 Test"));
    await studio.InitializeAsync();
    Check(studio.Recipes.Any(x => x.Recipe.Id.StartsWith("phase6.semantic.", StringComparison.OrdinalIgnoreCase)), "Text Studio loads Phase 6 recipes");
    var studioRecipe = studio.Recipes.First(x => x.Recipe.Id.Equals("phase6.semantic.npc-greeting", StringComparison.OrdinalIgnoreCase));
    studio.SelectRecipe(studioRecipe);
    studio.SetParameter("mood", "friendly");
    studio.Seed = 123456UL; studio.SeedLocked = true;
    await studio.GenerateAsync(1);
    var result = studio.Results.Single();
    Check(result.Bundle.Features.TryGetValue("mood", out var studioMood) && studioMood.DisplayText == "friendly", "Playground candidate pipeline uses dynamic semantic parameter");
    await studio.ToggleFavoriteAsync(result.Id);
    Check(studio.Favorites.Any(x => x.Features is { Count: > 0 }), "favorites persist semantic features");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 6 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 6 tests passed.");
return 0;

static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.String => element.GetString(),
    JsonValueKind.Number when element.TryGetInt64(out var i) => i,
    JsonValueKind.Number => element.GetDouble(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).Select(x => x?.ToString() ?? string.Empty).ToArray(),
    _ => element.ToString()
};

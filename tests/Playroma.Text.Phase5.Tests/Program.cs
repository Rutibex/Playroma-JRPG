using System.Text.Json;
using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Corpora;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Markov;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

var registry = GeneratorCatalog.CreatePhase5Registry();
Check(registry.Kinds.Contains(MarkovGenerationStage.StageKind), "Phase 5 registry includes Markov stage");
Check(BuiltInPhase5Recipes.All.Count >= 6, "Phase 5 built-in recipes");
Check(BuiltInRecipeCatalog.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInRecipeCatalog.All.Count, "built-in recipe IDs remain unique");

var normalization = new CorpusNormalizationOptions(RemoveDuplicateLines: true, Lowercase: true);
var normalized = CorpusNormalizer.Normalize("  Alpha  beta\r\n\r\nAlpha  beta\nGamma ", normalization);
Check(normalized == "alpha beta\ngamma", "corpus normalization");
Check(CorpusTokenizer.TokenizeSequences("One red fox. Two blue birds!", CorpusTokenMode.Word).Count == 2, "word sequences split by sentence");
Check(CorpusTokenizer.TokenizeSequences("One. Two.\nThree. Four.", CorpusTokenMode.Sentence).Count == 2, "sentence sequences split by paragraph");
Check(CorpusTokenizer.TokenizeSequences("abc\ndef", CorpusTokenMode.Character).Count == 2, "character sequences split by line");

var sourceText = "The old gate opens at sunset. The west road bends toward the marsh. Lanterns burn beside the river. Traders sleep beneath the stone bridge. The night watch counts every bell. Rain gathers on the copper roofs.";
var corpus = new TextCorpus("tests.core", 1, "Core Test", sourceText, [new CorpusSourceMetadata("Phase 5 tests", License: "Test data")], DefaultTraining: new MarkovTrainingOptions(CorpusTokenMode.Word, 1));
var options = new MarkovTrainingOptions(CorpusTokenMode.Word, 1, 1, true, true, true, 5, 16, true, .98, 128).Normalize();
Check(corpus.Fingerprint() == (corpus with { Name = "Renamed Metadata Only" }).Fingerprint(), "corpus model fingerprint ignores metadata-only edits");
Check(corpus.Fingerprint() != (corpus with { Text = corpus.Text + " Extra." }).Fingerprint(), "corpus model fingerprint changes with source text");

var modelA = NGramMarkovModel.Train(corpus, options);
var modelB = NGramMarkovModel.Train(corpus, options);
var a = modelA.Generate(new Xoshiro256StarStar(12345));
var b = modelB.Generate(new Xoshiro256StarStar(12345));
Check(a == b && !string.IsNullOrWhiteSpace(a), "Markov deterministic generation");
Check(modelA.Statistics.StateCount > 0 && modelA.Statistics.TransitionCount > 0, "Markov statistics populated");

var guard = new CorpusSimilarityGuard(corpus);
Check(!guard.IsAcceptable("The old gate opens at sunset.", options, out var exact) && exact.IsExactMatch, "exact corpus match rejected");

var root = Path.Combine(Path.GetTempPath(), "PlayromaTextPhase5-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "data"));
try
{
    var layout = new TextProjectLayout(root); layout.EnsureCreated();
    var library = new TextCorpusLibrary(layout.Corpora);
    var saved = await library.SaveAsync(corpus);
    Check(File.Exists(saved), "corpus JSON saved");
    var reloaded = await library.ResolveAsync(corpus.Id);
    Check(reloaded is not null && reloaded.Text == corpus.Text && reloaded.DefaultTraining?.Mode == CorpusTokenMode.Word, "corpus JSON reload preserves text/training settings");

    var importer = new CorpusImportService();
    var txt = Path.Combine(root, "sample.txt"); await File.WriteAllTextAsync(txt, "alpha\nbeta");
    var importedTxt = await importer.ImportFileAsync(txt, new CorpusImportOptions(License: "CC0"));
    Check(importedTxt.Text.Contains("alpha", StringComparison.Ordinal), "TXT import");
    Check(importedTxt.Sources.First().Sha256?.Length == 64, "import captures SHA-256 provenance");
    var csv = Path.Combine(root, "sample.csv"); await File.WriteAllTextAsync(csv, "name,text\nA,Hello world\nB,Good night");
    var importedCsv = await importer.ImportFileAsync(csv, new CorpusImportOptions(CsvColumnIndex: 1));
    Check(importedCsv.Text.Contains("Hello world", StringComparison.Ordinal) && !importedCsv.Text.Contains("A\n", StringComparison.Ordinal), "CSV import selected column");
    var json = Path.Combine(root, "sample.json"); await File.WriteAllTextAsync(json, "{\"rows\":[{\"dialogue\":\"Welcome\"},{\"dialogue\":\"Farewell\"}],\"ignored\":\"Nope\"}");
    var importedJson = await importer.ImportFileAsync(json, new CorpusImportOptions(JsonPropertyName: "dialogue"));
    Check(importedJson.Text.Contains("Welcome", StringComparison.Ordinal) && !importedJson.Text.Contains("Nope", StringComparison.Ordinal), "JSON property import");

    var cache = new JsonMarkovModelCache(Path.Combine(layout.CacheRoot, "markov"));
    await cache.SaveAsync(modelA);
    var cached = await cache.TryLoadAsync(corpus, options);
    Check(cached is not null && cached.Generate(new Xoshiro256StarStar(12345)) == a, "Markov cache round trip");

    var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
    {
        ["corpusId"] = JsonSerializer.SerializeToElement(corpus.Id),
        ["mode"] = JsonSerializer.SerializeToElement("word"),
        ["order"] = JsonSerializer.SerializeToElement(1),
        ["minimumTokens"] = JsonSerializer.SerializeToElement(5),
        ["maximumTokens"] = JsonSerializer.SerializeToElement(18),
        ["maximumSimilarity"] = JsonSerializer.SerializeToElement(.98),
        ["candidateAttempts"] = JsonSerializer.SerializeToElement(128),
        ["field"] = JsonSerializer.SerializeToElement("text"),
        ["isPrimary"] = JsonSerializer.SerializeToElement(true)
    };
    var recipe = new TextRecipe("tests.markov-stage", 1, "Markov Stage Test", TextContentKind.Rumor, [new TextRecipeStage("markov", MarkovGenerationStage.StageKind, Settings: settings)]);
    var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase5Registry(corpora: library, markovCache: cache));
    var bundle = await engine.GenerateAsync(recipe, new GenerationContext("tests"), 999UL, new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(10), MaxCandidateAttempts: 5000));
    Check(!string.IsNullOrWhiteSpace(bundle.PrimaryText), "project corpus resolves inside recipe stage");
    Check(bundle.Tags.ContainsKey("markovModel"), "Markov stage emits provenance tags");
    Check(bundle.Scores.Any(x => x.Name == "originality"), "Markov stage emits originality score");

    using var studio = new TextStudioSession(new TextStudioOptions(root, "Phase 5 Test"));
    await studio.InitializeAsync();
    Check(studio.Recipes.Any(x => x.Recipe.Id.StartsWith("phase5.markov.", StringComparison.OrdinalIgnoreCase)), "Text Studio loads Phase 5 recipes");
    var markovRecipe = studio.Recipes.First(x => x.Recipe.Id.Equals("phase5.markov.tavern-rumor", StringComparison.OrdinalIgnoreCase));
    studio.SelectRecipe(markovRecipe); studio.Seed = 424242UL; studio.SeedLocked = true; await studio.GenerateAsync(1);
    Check(!string.IsNullOrWhiteSpace(studio.Results.Single().Text), "live playground generates Markov recipe");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

foreach (var recipe in BuiltInPhase5Recipes.All)
{
    var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase5Registry());
    var bundle = await engine.GenerateAsync(recipe, new GenerationContext("phase5-builtins"), SeedFactory.Derive(777UL, recipe.Id),
        new GenerationBudgetOptions(MaxElapsed: TimeSpan.FromSeconds(10), MaxCandidateAttempts: 10000));
    Check(!string.IsNullOrWhiteSpace(bundle.PrimaryText), "built-in recipe generates: " + recipe.Id);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 5 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 5 tests passed.");
return 0;

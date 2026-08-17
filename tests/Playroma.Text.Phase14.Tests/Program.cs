using System.Text.Json;
using Playroma.Text.Authoring;
using Playroma.Text.Authoring.AI;
using Playroma.Text.Authoring.Release;
using Playroma.Text.Core.AI;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators;
using Playroma.Text.Generators.AI;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(TextEngineInfo.EngineVersion == "1.0.0-phase14", "Phase 14 engine/release marker");
var providers = new NeuralTextProviderRegistry();
providers.Register(new FakeProvider());
var registry = GeneratorCatalog.CreatePhase14Registry(neuralProviders: providers);
Check(registry.TryGet(NeuralTextStage.StageKind, out _), "Phase 14 registry exposes optional neural stage");
Check(registry.TryGet(RandomListStage.StageKind, out _), "Phase 14 preserves complete previous generator stack");
Check(BuiltInRecipeCatalog.All.Any(x => x.Id == "phase14.search.quality-diverse-relic"), "Phase 14 quality-diversity built-in recipe registered");

var engine = new TextGenerationEngine(registry);
var candidateEngine = new CandidateGenerationEngine(engine);
var qd = BuiltInPhase14Recipes.QualityDiverseRelicTitle();
var first = await candidateEngine.GenerateRankedAsync(qd, 5, seed: 140014);
var second = await candidateEngine.GenerateRankedAsync(qd, 5, seed: 140014);
Check(first.Count == second.Count && first.Select(x => x.PrimaryText).SequenceEqual(second.Select(x => x.PrimaryText)), "advanced search is deterministic for fixed seed");
Check(first.Any(x => x.Tags.TryGetValue("search.mode", out var mode) && mode == CandidateSearchMode.QualityDiversity.ToString()), "advanced search provenance records search mode");
Check(first.Select(x => x.PrimaryText).Distinct(StringComparer.Ordinal).Count() > 1, "quality-diversity returns multiple distinct results");

var neuralRecipe = new TextRecipe("phase14.test.ai", 1, "AI test", TextContentKind.Dialogue,
[
    Stage("draft", AdLibStage.StageKind, new { template = "hello traveler", slots = new { }, field = "text", isPrimary = true }),
    Stage("ai", NeuralTextStage.StageKind, new { providerId = "fake", prompt = "Polish: {{text}}", includeProjectContext = false, maximumOutputCharacters = 200, field = "text", isPrimary = true })
]);
var neural = await engine.GenerateAsync(neuralRecipe, seed: 44);
Check(neural.PrimaryText == "POLISHED: Polish: hello traveler", "optional provider stage uses configured provider");
Check(neural.Tags.TryGetValue("neural.projectContextIncluded", out var included) && included == "false", "AI project context defaults to excluded");

var offlineEngine = new TextGenerationEngine(GeneratorCatalog.CreatePhase14Registry());
var offline = await offlineEngine.GenerateAsync(neuralRecipe, seed: 44);
Check(offline.Diagnostics.Any(x => x.Code == GenerationDiagnosticCodes.NeuralProviderUnavailable), "missing optional provider fails safely with diagnostic");

string root = Path.Combine(Path.GetTempPath(), "playroma-phase14-" + Guid.NewGuid().ToString("N"));
try
{
    var layout = new TextProjectLayout(root); layout.EnsureCreated();
    var settingsStore = new NeuralProviderSettingsStore(root);
    await settingsStore.SaveAsync(new NeuralProviderSettingsDocument(1, [new NeuralProviderSettings("off", "Disabled", Enabled: false)]));
    var loadedSettings = await settingsStore.LoadAsync();
    Check(loadedSettings.Providers.Count == 1 && !loadedSettings.Providers[0].Enabled, "optional AI settings round-trip disabled by default");

    var recipePath = Path.Combine(layout.Recipes, "compact.playtext.json");
    await File.WriteAllTextAsync(recipePath, JsonSerializer.Serialize(qd, RecipeJson.Options).Replace("\n", "").Replace("  ", ""));
    var migration = await new TextAssetMigrationService().PreviewAsync(root);
    Check(migration.ChangedCount >= 1, "migration preview identifies noncanonical recipe");
    var applied = await new TextAssetMigrationService().ApplyAsync(root);
    Check(applied.ChangedCount >= 1 && File.Exists(recipePath + ".pre-phase14"), "migration creates pre-phase14 backup");

    var duplicate = qd with { Name = "Duplicate" };
    await File.WriteAllTextAsync(Path.Combine(layout.Recipes, "duplicate.playtext.json"), JsonSerializer.Serialize(duplicate, RecipeJson.Options));
    var doctor = await new TextProjectDoctor().AuditAsync(root);
    Check(doctor.Issues.Any(x => x.Severity == TextProjectDoctorSeverity.Error && x.Message.Contains("Duplicate project recipe id", StringComparison.OrdinalIgnoreCase)), "Project Doctor catches duplicate recipe IDs");
}
finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 14 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 14 tests passed.");
return 0;

static TextRecipeStage Stage(string id, string kind, object settings)
{
    var e = JsonSerializer.SerializeToElement(settings, RecipeJson.Options);
    return new TextRecipeStage(id, kind, Settings: e.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase));
}

sealed class FakeProvider : INeuralTextProvider
{
    public string Id => "fake";
    public string DisplayName => "Fake local provider";
    public NeuralProviderCapabilities Capabilities { get; } = new(SupportsSeed: true);
    public ValueTask<NeuralTextResponse> GenerateAsync(NeuralTextRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new NeuralTextResponse("POLISHED: " + request.Prompt, Id, "fake-model"));
}

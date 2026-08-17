using Playroma.Text.Authoring;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.RpgMakerMV;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

// Determinism and fork stability.
var a = new Xoshiro256StarStar(123456789UL);
var b = new Xoshiro256StarStar(123456789UL);
for (var i = 0; i < 100; i++) Check(a.NextUInt64() == b.NextUInt64(), "same seed must produce same stream");
var parent = new Xoshiro256StarStar(99);
var fa = parent.Fork("names");
var fb = new Xoshiro256StarStar(99).Fork("names");
Check(fa.NextUInt64() == fb.NextUInt64(), "forks must be deterministic");

// PRNG algorithms intentionally wrap modulo 2^64.  The repository enables
// checked arithmetic globally, so high seeds and repeated draws must never
// surface OverflowException.
try
{
    var wrapping = new Xoshiro256StarStar(ulong.MaxValue);
    for (var i = 0; i < 10_000; i++) _ = wrapping.NextUInt64();
    _ = wrapping.NextInt(int.MinValue, int.MaxValue);
}
catch (OverflowException)
{
    failures.Add("deterministic random must permit intentional 64-bit wraparound");
}

// Recipe validation/fingerprinting.
var recipe = new TextRecipe("phase0.echo", 1, "Phase 0 Echo", TextContentKind.Custom,
    new[] { new TextRecipeStage("echo", EchoDiagnosticStage.StageKind) });
Check(TextRecipeValidator.Validate(recipe).All(x => x.Severity != GenerationDiagnosticSeverity.Error), "valid recipe rejected");
Check(recipe.Fingerprint().Length == 64, "fingerprint should be sha256 hex");

// Orchestration/provenance/trace.
var registry = new GenerationStageRegistry();
registry.Register(new EchoDiagnosticStage());
var engine = new TextGenerationEngine(registry);
var bundle = await engine.GenerateAsync(recipe, new GenerationContext(Values: new Dictionary<string, object?> { ["text"] = "Hello Playroma" }), 42UL);
Check(bundle.PrimaryText == "Hello Playroma", "echo stage output");
Check(bundle.Provenance?.Seed == 42UL, "seed provenance");
Check(bundle.Trace?.Nodes.Count >= 2, "trace nodes");

// Authoring layout keeps sources separate from generated caches.
var layout = new TextProjectLayout(Path.Combine(Path.GetTempPath(), "PlayromaTextPhase0"));
Check(layout.Recipes.Contains(Path.Combine("data", "PlayromaText", "recipes")), "recipe layout");
Check(layout.CacheRoot.Contains(Path.Combine(".playroma", "cache", "text")), "cache layout");

// MV note replacement protection.
var target = new TextTargetDescriptor("enemy", "Enemies.json", "3", "/note", "note", "Enemy Note", "<YEP Tag>\n</YEP Tag>", true, true);
var plan = MvTextSafety.BuildSingleFieldPlan(target, "Generated prose", TextImportMode.Replace);
Check(!plan.CanApply, "plugin-tag replacement should be blocked");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 0 tests FAILED:");
    foreach (var failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 0 tests passed.");
return 0;

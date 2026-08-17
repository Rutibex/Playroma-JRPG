using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Dialogue;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Dialogue;
using Playroma.Text.Generators.Stages;
using Playroma.Text.RpgMakerMV;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Phase 10 engine/version marker remains available on current engine");
var registry = GeneratorCatalog.CreatePhase10Registry();
Check(registry.TryGet(DialogueVoiceStage.StageKind, out _), "Phase 10 registry exposes dialogue voice stage");
Check(registry.TryGet(RandomListStage.StageKind, out _), "Phase 10 registry preserves earlier generators");
Check(BuiltInDialogueVoiceProfiles.All.Count >= 9, "built-in voice profile coverage");
Check(BuiltInDialogueVoiceProfiles.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInDialogueVoiceProfiles.All.Count, "built-in voice profile IDs unique");
Check(BuiltInPhase10Recipes.All.Count >= 12, "Phase 10 voice-aware recipe coverage");
Check(BuiltInPhase10Recipes.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInPhase10Recipes.All.Count, "Phase 10 recipe IDs unique");

var voiceEngine = new DialogueVoiceEngine();
var source = new GeneratedTextBundle
{
    Kind = TextContentKind.Dialogue,
    PrimaryText = "I do not want to leave. The road is dangerous after dark.",
    Fields = [new GeneratedField("text", "I do not want to leave. The road is dangerous after dark.", "text", true)]
};
DialogueVoiceProfile gruff = BuiltInDialogueVoiceProfiles.All.Single(x => x.Id == "gruff-veteran");
var voicedA = voiceEngine.Apply(source, gruff, new DialogueVoiceRealizationOptions(DialogueAct.Warning, DialogueEmotion.Tired, SpeakerName:"Rusk"), new Xoshiro256StarStar(101UL));
var voicedB = voiceEngine.Apply(source, gruff, new DialogueVoiceRealizationOptions(DialogueAct.Warning, DialogueEmotion.Tired, SpeakerName:"Rusk"), new Xoshiro256StarStar(101UL));
Check(voicedA.PrimaryText == voicedB.PrimaryText, "voice realization is deterministic for a fixed seed");
Check(voicedA.Tags.TryGetValue("voiceProfile", out var profileTag) && profileTag == "gruff-veteran", "voice provenance tag emitted");
Check(voicedA.Features.TryGetValue("speaker.name", out var speakerFeature) && speakerFeature.DisplayText == "Rusk", "speaker semantic feature emitted");
Check(voicedA.Features.TryGetValue("dialogue.act", out var actFeature) && actFeature.DisplayText == "Warning", "dialogue act semantic feature emitted");

var tabooProfile = new DialogueVoiceProfile("taboo",1,"Taboo",TabooWords:["dragon"],CatchphrasePreference:0);
string taboo = voiceEngine.Realize("The dragon is coming.", tabooProfile, new DialogueVoiceRealizationOptions(AddActFraming:false,AddEmotionFraming:false,AddCatchphrase:false), new Xoshiro256StarStar(2));
Check(!taboo.Contains("dragon", StringComparison.OrdinalIgnoreCase), "taboo words are removed from realized dialogue");

var formal = BuiltInDialogueVoiceProfiles.All.Single(x => x.Id == "courtly-noble");
string formalized = voiceEngine.Realize("I don't want trouble. Can you help?", formal, new DialogueVoiceRealizationOptions(AddActFraming:false,AddEmotionFraming:false,AddCatchphrase:false), new Xoshiro256StarStar(3));
Check(formalized.Contains("do not", StringComparison.OrdinalIgnoreCase), "formal profile expands contractions");

var rogue = BuiltInDialogueVoiceProfiles.All.Single(x => x.Id == "street-rogue");
string rogueLine = voiceEngine.Realize("I am going to leave because I want to avoid the guards.", rogue, new DialogueVoiceRealizationOptions(AddActFraming:false,AddEmotionFraming:false,AddCatchphrase:false), new Xoshiro256StarStar(4));
Check(rogueLine.Contains("gonna", StringComparison.OrdinalIgnoreCase) || rogueLine.Contains("wanna", StringComparison.OrdinalIgnoreCase) || rogueLine.Contains("watch", StringComparison.OrdinalIgnoreCase), "dialect/vocabulary transforms apply");


var recipeDefaultEngine = new TextGenerationEngine(GeneratorCatalog.CreatePhase10Registry());
GeneratedTextBundle recipeDefaultWarning = await recipeDefaultEngine.GenerateAsync(
    BuiltInPhase10Recipes.All.Single(x=>x.Id=="phase10.dialogue.warning"),
    new GenerationContext("phase10-tests", Values:new Dictionary<string,object?>{{"voice.profileId","common"}}), 8080UL);
Check(recipeDefaultWarning.Tags.TryGetValue("dialogueAct", out var defaultActTag) && defaultActTag == "Warning", "recipe dialogue intent is preserved when no explicit act override exists");
Check(DialogueVoiceProfileValidator.Validate(BuiltInDialogueVoiceProfiles.Default).Count == 0, "built-in default voice profile validates");

var engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase10Registry());
var context = new GenerationContext("phase10-tests", Values:new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase)
{
    ["voice.profileId"]="courtly-noble",
    ["speaker.name"]="Lady Meriel",
    ["dialogue.act"]="Warning",
    ["voice.emotion"]="Solemn",
    ["relationship.affinity"]=.25,
    ["relationship.trust"]=.7
});
foreach (TextRecipe recipe in BuiltInPhase10Recipes.All)
{
    GeneratedTextBundle generated = await engine.GenerateAsync(recipe, context, SeedFactory.Derive(10100UL, recipe.Id));
    Check(!string.IsNullOrWhiteSpace(generated.PrimaryText), "Phase 10 built-in generates: " + recipe.Id);
    Check(!generated.Diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error), "Phase 10 built-in has no errors: " + recipe.Id);
    Check(generated.Tags.TryGetValue("voiceProfile", out var voice) && voice == "courtly-noble", "Phase 10 recipe uses active voice: " + recipe.Id);
}

GeneratedTextBundle branch = await engine.GenerateAsync(BuiltInPhase10Recipes.All.Single(x=>x.Id=="phase10.dialogue.branching"), context, 101234UL);
Check(MvEventTextCompiler.ExtractChoices(branch).Count == 3, "voice-aware branching recipe retains player choice labels");
Check(MvEventTextCompiler.CompileBundle(branch, MvGeneratedEventTextKind.BranchingDialogue).CanApply, "voice-aware branching recipe compiles to MV event commands");
Check(branch.Fields.Where(x=>x.Name.StartsWith("response",StringComparison.OrdinalIgnoreCase)).All(x=>!string.IsNullOrWhiteSpace(x.Text)), "all branching responses remain populated after voice realization");

string tempRoot = Path.Combine(Path.GetTempPath(), "playroma-phase10-" + Guid.NewGuid().ToString("N"));
try
{
    var layout = new TextProjectLayout(tempRoot); layout.EnsureCreated();
    var store = new JsonDialogueVoiceProfileStore(layout.Voices);
    var custom = new DialogueVoiceProfile("custom-test",1,"Custom Test",Formality:.77,Vocabulary:new Dictionary<string,string>{{"hello","greetings"}});
    string saved = await store.SaveAsync(custom);
    Check(File.Exists(saved), "project voice profile saved");
    DialogueVoiceProfile? loaded = await store.ResolveAsync("custom-test");
    Check(loaded is not null && Math.Abs(loaded.Formality-.77)<.0001 && loaded.EffectiveVocabulary.ContainsKey("hello"), "project voice profile round-trip");

    bool invalidRejected=false;
    try { await store.SaveAsync(new DialogueVoiceProfile("bad",1,"Bad",Formality:2)); } catch(InvalidOperationException) { invalidRejected=true; }
    Check(invalidRejected, "invalid voice profiles are rejected before persistence");

    var assignments = new DialogueVoiceAssignmentStore(layout.VoiceAssignments);
    await assignments.UpsertAsync(new DialogueVoiceAssignment("map:1:event:2", "custom-test", "Gate Guard"));
    DialogueVoiceAssignment? assignment = await assignments.FindAsync("map:1:event:2");
    Check(assignment?.ProfileId == "custom-test" && assignment.SpeakerName == "Gate Guard", "event voice assignment persists");

    var projectEngine = new TextGenerationEngine(GeneratorCatalog.CreatePhase10Registry(voices:store));
    GeneratedTextBundle projectVoiced = await projectEngine.GenerateAsync(BuiltInPhase10Recipes.All.Single(x=>x.Id=="phase10.dialogue.greeting"),
        new GenerationContext(tempRoot, Values:new Dictionary<string,object?>{{"voice.profileId","custom-test"},{"dialogue.act","Greeting"}}), 55UL);
    Check(projectVoiced.Tags.TryGetValue("voiceProfile", out var projectVoice) && projectVoice == "custom-test", "project voice overrides/fills voice registry");
}
finally { try { if(Directory.Exists(tempRoot)) Directory.Delete(tempRoot,true); } catch { } }

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 10 tests FAILED:");
    foreach (string failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 10 tests passed.");
return 0;

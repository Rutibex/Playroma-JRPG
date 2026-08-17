using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Narrative;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.Generators.Narrative;
using Playroma.Text.Generators.Stages;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Phase 11 engine/version marker remains available on current engine");
var registry = GeneratorCatalog.CreatePhase11Registry();
Check(registry.TryGet(DialogueVoiceStage.StageKind, out _), "Phase 11 registry preserves Phase 10 voice stage");
Check(registry.TryGet(RandomListStage.StageKind, out _), "Phase 11 registry preserves Phase 1 generators");
Check(BuiltInStorylets.All.Count >= 12, "built-in storylet coverage");
Check(BuiltInStorylets.All.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == BuiltInStorylets.All.Count, "built-in storylet IDs unique");
Check(BuiltInStorylets.All.All(x => StoryletDefinitionValidator.Validate(x).Count == 0), "built-in storylets validate");

var engine = new StoryletEngine();
var state = new StoryletRuntimeState();
var simple = new StoryletDefinition("test.simple",1,"Simple",Weight:2,Salience:3,OnceOnly:true,CooldownTurns:2,
    Effects:[new StoryletEffectDefinition(StoryletEffectKind.Set,"flag",true),new StoryletEffectDefinition(StoryletEffectKind.Increment,"count",2),new StoryletEffectDefinition(StoryletEffectKind.AddTag,"seen")],
    Realization:new StoryletRealizationDefinition("phase10.dialogue.small-talk"));
var context = new GenerationContext("phase11-tests", Values:new Dictionary<string,object?>());
var eval = engine.Evaluate([simple], context, state, 123UL).Single();
Check(eval.Eligible && Math.Abs(eval.Score-6)<.0001, "eligible storylet score uses weight × salience");
var selectionA = engine.Select([simple],context,state,123UL);
var selectionB = engine.Select([simple],context,state,123UL);
Check(selectionA?.Storylet.Id == selectionB?.Storylet.Id, "storylet selection deterministic for a fixed seed");
Check(selectionA is not null, "simple storylet selects");
if(selectionA is not null) engine.ApplyEffects(selectionA,state);
Check(state.Values.TryGetValue("flag",out var flag) && flag is bool b && b, "set effect applies");
Check(Convert.ToInt64(state.Values["count"])==2, "increment effect applies");
Check(state.Tags.Contains("seen"), "add-tag effect applies");
Check(state.Turn==1 && state.SeenCounts["test.simple"]==1, "storylet state advances seen/turn counters");
Check(!engine.Evaluate([simple],context,state,123UL).Single().Eligible, "once-only storylet becomes ineligible after selection");

var cooldown = new StoryletDefinition("test.cooldown",1,"Cooldown",CooldownTurns:3,Realization:new StoryletRealizationDefinition("phase10.dialogue.small-talk"));
var cooldownState = new StoryletRuntimeState { Turn=10 }; cooldownState.LastSelectedTurns["test.cooldown"]=8;
Check(!engine.Evaluate([cooldown],context,cooldownState,5).Single().Eligible,"cooldown prevents premature selection");
cooldownState.Turn=11;
Check(engine.Evaluate([cooldown],context,cooldownState,5).Single().Eligible,"cooldown expires on correct turn");

var gated = new StoryletDefinition("test.gated",1,"Gated",Prerequisites:["reputation >= 5 && tag(\"hero\")"],Realization:new StoryletRealizationDefinition("phase10.dialogue.small-talk"));
var gatedState = new StoryletRuntimeState(); gatedState.Values["reputation"]=6; gatedState.Tags.Add("hero");
Check(engine.Evaluate([gated],context,gatedState,1).Single().Eligible,"storylet prerequisites use semantic predicate language and runtime state");
gatedState.Values["reputation"]=1;
Check(!engine.Evaluate([gated],context,gatedState,1).Single().Eligible,"failed storylet prerequisite blocks eligibility");

var bound = new StoryletDefinition("test.bound",1,"Bound",
    Bindings:[new StoryletBindingDefinition("speaker",StoryletBindingKind.Character,"storylet.characters"),new StoryletBindingDefinition("place",StoryletBindingKind.Location,"storylet.locations")],
    Realization:new StoryletRealizationDefinition("phase10.dialogue.small-talk"));
var bindContext = new GenerationContext("phase11-tests",Values:new Dictionary<string,object?>
{
    ["storylet.characters"] = new[]{"Mira","Rusk"},
    ["storylet.locations"] = new[]{"Greyhaven","North Gate"}
});
var boundEvalA = engine.Evaluate([bound],bindContext,new StoryletRuntimeState(),777).Single();
var boundEvalB = engine.Evaluate([bound],bindContext,new StoryletRuntimeState(),777).Single();
Check(boundEvalA.Eligible && boundEvalA.EffectiveBindings.Count==2,"character/location bindings resolve");
Check(boundEvalA.EffectiveBindings.Select(x=>x.Candidate.Name).SequenceEqual(boundEvalB.EffectiveBindings.Select(x=>x.Candidate.Name)),"bindings deterministic for fixed seed");
var bindingPredicate = new StoryletDefinition("test.binding-predicate",1,"Binding Predicate",
    Bindings:[new StoryletBindingDefinition("speaker",StoryletBindingKind.Character,"storylet.characters")],
    Prerequisites:["has(binding.speaker.name)"],
    Realization:new StoryletRealizationDefinition("phase10.dialogue.small-talk"));
Check(engine.Evaluate([bindingPredicate],bindContext,new StoryletRuntimeState(),777).Single().Eligible,"prerequisites can inspect resolved binding context");

var textEngine = new TextGenerationEngine(GeneratorCatalog.CreatePhase11Registry());
var candidateEngine = new CandidateGenerationEngine(textEngine);
var narrative = new StoryletNarrativeEngine(candidateEngine, id => BuiltInRecipeCatalog.All.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)));
var realizedStorylet = new StoryletDefinition("test.realize",1,"Realize",Realization:new StoryletRealizationDefinition("phase10.dialogue.warning",DialogueAct:"Warning",Emotion:"Suspicious"));
var realizeSelection = new StoryletSelection(realizedStorylet,1,[],9001,0);
var realized = await narrative.RealizeAsync(realizeSelection,new GenerationContext("phase11-tests",Values:new Dictionary<string,object?>{{"voice.profileId","common"}}),new StoryletRuntimeState());
Check(!string.IsNullOrWhiteSpace(realized.Bundle.PrimaryText),"storylet realizes through existing recipe engine");
Check(realized.Bundle.Tags.TryGetValue("storylet.id",out var storyletTag) && storyletTag=="test.realize","storylet provenance tags emitted");
Check(realized.Context.Data.TryGetValue("dialogue.act",out var act) && Convert.ToString(act)=="Warning","storylet realization injects dialogue act");
var parameterStorylet = realizedStorylet with { Realization = realizedStorylet.Realization! with { Parameters = new Dictionary<string, object?> { ["storyletSpecific"] = "lantern" } } };
var parameterResult = await narrative.RealizeAsync(new StoryletSelection(parameterStorylet,1,[],9002,0),new GenerationContext("phase11-tests",Values:new Dictionary<string,object?>()),new StoryletRuntimeState());
Check(parameterResult.Context.Data.TryGetValue("storyletSpecific",out var parameterValue) && Convert.ToString(parameterValue)=="lantern","storylet realization injects typed recipe parameters");

string tempRoot = Path.Combine(Path.GetTempPath(),"playroma-phase11-"+Guid.NewGuid().ToString("N"));
try
{
    var layout = new TextProjectLayout(tempRoot); layout.EnsureCreated();
    var store = new JsonStoryletStore(layout.Storylets);
    var custom = new StoryletDefinition("custom.story",1,"Custom Story",Weight:4,Realization:new StoryletRealizationDefinition("phase10.dialogue.rumor"));
    string path = await store.SaveAsync(custom);
    Check(File.Exists(path),"project storylet saved");
    StoryletDefinition? loaded = await store.ResolveAsync("custom.story");
    Check(loaded is not null && loaded.Name=="Custom Story" && Math.Abs(loaded.Weight-4)<.001,"project storylet round-trip");
    var composite = new CompositeStoryletResolver(BuiltInStorylets.All,store);
    Check((await composite.LoadAllAsync()).Any(x=>x.Id=="custom.story"),"composite resolver includes project storylets");

    bool invalidRejected=false;
    try { await store.SaveAsync(new StoryletDefinition("",1,"Bad",Realization:new StoryletRealizationDefinition("phase10.dialogue.rumor"))); } catch(InvalidOperationException) { invalidRejected=true; }
    Check(invalidRejected,"invalid storylet rejected before persistence");
}
finally { try { if(Directory.Exists(tempRoot)) Directory.Delete(tempRoot,true); } catch { } }

if (failures.Count > 0)
{
    Console.Error.WriteLine("Playroma Text Phase 11 tests FAILED:");
    foreach (string failure in failures.Distinct()) Console.Error.WriteLine(" - " + failure);
    return 1;
}
Console.WriteLine("Playroma Text Phase 11 tests passed.");
return 0;

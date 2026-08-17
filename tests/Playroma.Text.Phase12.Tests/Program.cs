using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Planning;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Planning;
using Playroma.Text.Generators;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.Generators.Planning;
using Playroma.Text.Generators.Stages;
using Playroma.Text.RpgMakerMV.Planning;

var failures=new List<string>(); void Check(bool c,string n){if(!c)failures.Add(n);}
Check(!string.IsNullOrWhiteSpace(TextEngineInfo.EngineVersion), "Phase 12 engine marker remains available on current engine");
var registry=GeneratorCatalog.CreatePhase12Registry(); Check(registry.TryGet(RandomListStage.StageKind,out _),"Phase 12 preserves generator stack");
Check(BuiltInQuestGrammars.All.Count>=8,"built-in quest grammar coverage");
Check(BuiltInQuestGrammars.All.All(x=>QuestGrammarValidator.Validate(x).Count==0),"built-in quest grammars validate");
Check(BuiltInPhase12Recipes.All.Count>=5,"Phase 12 realization recipes included");

var context=new GenerationContext("phase12-tests",Values:new Dictionary<string,object?>
{
 ["storylet.characters"]=new[]{"Mira","Tomas","Captain Rusk"},
 ["storylet.locations"]=new[]{"Greyhaven","Whispering Mine","North Road"},
 ["quest.items"]=new[]{"Moonstone","Lost Ledger","Sealed Letter"},
 ["quest.monsters"]=new[]{"Marsh Beast","Ruin Guardian"}
});
var planner=new QuestPlanner();
var fetch=BuiltInQuestGrammars.All.Single(x=>x.Id=="phase12.quest.fetch");
QuestPlan p1=planner.Plan(fetch,context,new QuestWorldState(),1234); QuestPlan p2=planner.Plan(fetch,context,new QuestWorldState(),1234);
Check(p1.IsValid && p1.Steps.Count==4,"HTN fetch plan causal validation");
Check(p1.Steps.Select(x=>x.Action.Id).SequenceEqual(p2.Steps.Select(x=>x.Action.Id)),"HTN planning deterministic");
Check(p1.Diagnostics.Mode=="HTN","HTN planner mode reported");
Check(QuestPlanValidator.Validate(p1,context).Count==0,"independent causal validator passes");
var graph=QuestObjectiveGraphBuilder.Build(p1); Check(graph.Nodes.Count==p1.Steps.Count && graph.Edges.Count>=p1.Steps.Count-1,"objective dependency graph derived from causal plan");

var search=BuiltInQuestGrammars.All.Single(x=>x.Id=="phase12.quest.branching-search");
QuestPlan sp=planner.Plan(search,context,new QuestWorldState(),99); Check(sp.IsValid && sp.Diagnostics.Mode=="ForwardSearch","forward search solves goal");
Check(sp.Steps.Any(x=>x.Action.Id=="recover-relic") && sp.Steps.Last().Action.Id=="return-relic","search plan contains causal chain");
Check(sp.TotalCost<=5.0001,"cost-aware planner chooses lower-cost information action");

var bad=new QuestGrammarDefinition("bad",1,"Bad",GoalPredicates:["tag(\"done\")"],Actions:[new("finish","Finish",Preconditions:["tag(\"never\")"],Effects:[new(QuestEffectKind.AddTag,"done")])],Planner:new(MaxPlanSteps:3,MaxSearchNodes:10));
bool noPlan=false;try{planner.Plan(bad,context,new QuestWorldState(),1);}catch(InvalidOperationException){noPlan=true;}Check(noPlan,"unsatisfiable quest fails safely within bounds");

var textEngine=new TextGenerationEngine(GeneratorCatalog.CreatePhase12Registry()); var candidates=new CandidateGenerationEngine(textEngine);
var realize=new QuestRealizationEngine(candidates,id=>BuiltInRecipeCatalog.All.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)));
QuestRealizationResult rr=await realize.RealizeAsync(p1,context); Check(!string.IsNullOrWhiteSpace(rr.Bundle.PrimaryText),"quest plan realizes to text");Check(rr.Steps.Count==p1.Steps.Count,"every plan step realized");Check(rr.Bundle.Fields.Count(x=>x.Name.StartsWith("objective",StringComparison.OrdinalIgnoreCase))==p1.Steps.Count,"objectives exported as fields");

MvQuestExportPlan mv=MvQuestPlanCompiler.CompileSuggestedCommonEvent(p1,rr.Bundle.PrimaryText,rr.Steps.Select(x=>x.Bundle.PrimaryText).ToArray()); Check(mv.CanApply,"valid plan produces MV export suggestion");Check(mv.RawCommands.Any(x=>x.Contains("\"code\":101")),"MV export contains Show Text commands");Check(mv.RawCommands.Last().Contains("\"code\":0"),"MV export terminates Common Event suggestion");

string root=Path.Combine(Path.GetTempPath(),"playroma-phase12-"+Guid.NewGuid().ToString("N"));
try{var layout=new TextProjectLayout(root);layout.EnsureCreated();Check(Directory.Exists(layout.Quests),"quest source directory created");var store=new JsonQuestGrammarStore(layout.Quests);string path=await store.SaveAsync(fetch with{Id="project.fetch",Name="Project Fetch"});Check(File.Exists(path),"quest grammar persisted");var loaded=await store.ResolveAsync("project.fetch");Check(loaded?.Name=="Project Fetch","quest grammar round trip");}
finally{try{if(Directory.Exists(root))Directory.Delete(root,true);}catch{}}

if(failures.Count>0){Console.Error.WriteLine("Playroma Text Phase 12 tests FAILED:");foreach(string f in failures.Distinct())Console.Error.WriteLine(" - "+f);return 1;}Console.WriteLine("Playroma Text Phase 12 tests passed.");return 0;

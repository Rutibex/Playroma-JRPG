using Playroma.Text.Core.Planning;

namespace Playroma.Text.Generators.Planning;

/// <summary>Derives a readable dependency graph from plan effects and later preconditions.</summary>
public static class QuestObjectiveGraphBuilder
{
    public static QuestObjectiveGraph Build(QuestPlan plan)
    {
        var nodes=plan.Steps.Select(x=>new QuestObjectiveNode(x.Index,x.Action.Id,x.Action.Name,x.Action.EffectiveTags)).ToArray();
        var edges=new List<QuestObjectiveEdge>();
        for(int to=0;to<plan.Steps.Count;to++)
        {
            QuestPlanStep target=plan.Steps[to];
            foreach(string predicate in target.Action.EffectivePreconditions)
            {
                for(int from=to-1;from>=0;from--)
                {
                    string? key=plan.Steps[from].Action.EffectiveEffects.Select(x=>x.Key).FirstOrDefault(k=>References(predicate,k));
                    if(key is null)continue;
                    edges.Add(new(from,to,key));break;
                }
            }
            if(to>0 && !edges.Any(x=>x.ToStepIndex==to)) edges.Add(new(to-1,to,"sequence"));
        }
        return new(nodes,edges.Distinct().ToArray());
    }
    private static bool References(string predicate,string key)=>predicate.Contains(key,StringComparison.OrdinalIgnoreCase);
}

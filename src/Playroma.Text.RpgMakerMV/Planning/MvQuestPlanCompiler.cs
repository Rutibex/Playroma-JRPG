using System.Text.Json;
using Playroma.Text.Core.Planning;
using Playroma.Text.RpgMakerMV;

namespace Playroma.Text.RpgMakerMV.Planning;

public sealed record MvQuestExportPlan(IReadOnlyList<string> RawCommands,string PreviewText,IReadOnlyList<string> StateSuggestions,IReadOnlyList<TextImportValidationIssue> Issues)
{
    public bool CanApply => Issues.All(x=>!x.IsError);
}

/// <summary>
/// Produces a non-destructive RPG Maker MV Common Event suggestion from a causal QuestPlan.
/// It intentionally does not allocate switch/variable IDs; authors review those mappings first.
/// </summary>
public static class MvQuestPlanCompiler
{
    public static MvQuestExportPlan CompileSuggestedCommonEvent(QuestPlan plan, string? summary=null, IReadOnlyList<string>? objectives=null)
    {
        ArgumentNullException.ThrowIfNull(plan); var issues=new List<TextImportValidationIssue>(); if(!plan.IsValid)issues.Add(new("PTXTMV1201","Quest plan failed causal validation and cannot be exported.",true));
        objectives ??= plan.Steps.Select(x=>x.Action.Name).ToArray();
        string intro=string.IsNullOrWhiteSpace(summary)?(plan.Grammar.Description ?? plan.Grammar.Name):summary!;
        var commands=new List<string>();
        commands.Add(Command(108,0,new object?[]{"[Playroma Quest] "+plan.Grammar.Name+" ("+plan.Grammar.Id+")"}));
        commands.AddRange(MvEventTextCompiler.CompileShowText(intro,0).RawCommands);
        for(int i=0;i<plan.Steps.Count;i++)
        {
            string objective=i<objectives.Count?objectives[i]:plan.Steps[i].Action.Name;
            commands.Add(Command(108,0,new object?[]{$"[Quest Step {i+1}/{plan.Steps.Count}] {plan.Steps[i].Action.Id}"}));
            commands.AddRange(MvEventTextCompiler.CompileShowText(objective,0).RawCommands);
        }
        commands.Add(Command(108,0,new object?[]{"[Quest Complete] Map planner state to project switches/variables after review."}));
        commands.Add(Command(0,0,Array.Empty<object?>()));
        var suggestions=BuildStateSuggestions(plan);
        string preview=$"COMMON EVENT SUGGESTION — {plan.Grammar.Name}\n\n{intro}\n\n"+string.Join("\n",plan.Steps.Select((x,i)=>$"{i+1}. {(i<objectives.Count?objectives[i]:x.Action.Name)}"));
        return new(commands,preview,suggestions,issues);
    }

    private static IReadOnlyList<string> BuildStateSuggestions(QuestPlan plan)
    {
        var result=new List<string>{"Playroma does not auto-allocate MV switch/variable IDs during quest-plan compilation."};
        var tags=plan.Steps.SelectMany(x=>x.Action.EffectiveEffects).Where(x=>x.Kind is QuestEffectKind.AddTag or QuestEffectKind.RemoveTag).Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach(string tag in tags) result.Add($"Switch suggestion: quest.{plan.Grammar.Id}.{tag}");
        var values=plan.Steps.SelectMany(x=>x.Action.EffectiveEffects).Where(x=>x.Kind is QuestEffectKind.Set or QuestEffectKind.Increment).Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach(string key in values) result.Add($"Variable/switch suggestion: quest.{plan.Grammar.Id}.{key}");
        return result;
    }
    private static string Command(int code,int indent,object?[] parameters)=>JsonSerializer.Serialize(new{code,indent,parameters});
}

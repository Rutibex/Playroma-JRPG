using System.Globalization;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Planning;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Candidates;

namespace Playroma.Text.Generators.Planning;

public sealed record QuestStepRealization(QuestPlanStep Step, GeneratedTextBundle Bundle, GenerationContext Context);
public sealed record QuestRealizationResult(QuestPlan Plan, GeneratedTextBundle Bundle, IReadOnlyList<QuestStepRealization> Steps, GenerationContext Context);

/// <summary>Turns a causally validated QuestPlan into titles, objective text, journal text, and per-step bundles using ordinary Playroma recipes.</summary>
public sealed class QuestRealizationEngine
{
    private readonly CandidateGenerationEngine _generation;
    private readonly Func<string, TextRecipe?> _recipeResolver;
    public QuestRealizationEngine(CandidateGenerationEngine generation, Func<string, TextRecipe?> recipeResolver) { _generation = generation; _recipeResolver = recipeResolver; }

    public async ValueTask<QuestRealizationResult> RealizeAsync(QuestPlan plan, GenerationContext baseContext, CancellationToken cancellationToken = default)
    {
        if (!plan.IsValid) throw new InvalidOperationException("Quest plan is not causally valid: " + string.Join(" ", plan.EffectiveValidationIssues));
        GenerationContext questContext = BuildPlanContext(plan, baseContext);
        var steps = new List<QuestStepRealization>();
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            QuestPlanStep step = plan.Steps[i];
            string objective = ObjectiveText(step, plan);
            var values = questContext.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            values["objective.index"] = i + 1; values["objective.text"] = objective; values["action.id"] = step.Action.Id; values["action.name"] = step.Action.Name;
            foreach (var pair in step.Action.EffectiveRealizationParameters) values[pair.Key] = pair.Value;
            foreach (StoryletBinding binding in step.Bindings)
            {
                values["binding." + binding.Id + ".id"] = binding.Candidate.Id; values["binding." + binding.Id + ".name"] = binding.Candidate.Name; values[binding.Id] = binding.Candidate.Name;
            }
            var context = new GenerationContext(baseContext.ProjectId, "quest-step", plan.Grammar.Id + ":" + (i + 1).ToString(CultureInfo.InvariantCulture), values, questContext.EffectiveTags);
            string recipeId = string.IsNullOrWhiteSpace(step.Action.RealizationRecipeId) ? "phase12.quest.objective" : step.Action.RealizationRecipeId!;
            TextRecipe recipe = _recipeResolver(recipeId) ?? _recipeResolver("phase12.quest.objective") ?? throw new InvalidOperationException("The objective realization recipe is unavailable.");
            ulong stepSeed = SeedFactory.Derive(plan.Seed, "quest-step-realization:" + i.ToString(CultureInfo.InvariantCulture));
            GeneratedTextBundle bundle = await _generation.GenerateBestAsync(recipe, context, stepSeed, cancellationToken:cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bundle.PrimaryText)) bundle = bundle with { PrimaryText = objective };
            steps.Add(new(step, bundle, context));
        }

        string summary = string.Join(" ", steps.Select((x,i) => $"{i + 1}. {x.Bundle.PrimaryText}"));
        var summaryValues = questContext.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        summaryValues["quest.summary"] = summary;
        summaryValues["quest.objectives"] = steps.Select(x => x.Bundle.PrimaryText).ToArray();
        var summaryContext = new GenerationContext(baseContext.ProjectId, "quest-plan", plan.Grammar.Id, summaryValues, questContext.EffectiveTags);
        TextRecipe? summaryRecipe = _recipeResolver(plan.Grammar.SummaryRecipeId ?? "phase12.quest.summary");
        GeneratedTextBundle final;
        if (summaryRecipe is not null)
        {
            final = await _generation.GenerateBestAsync(summaryRecipe, summaryContext, SeedFactory.Derive(plan.Seed,"quest-summary"), cancellationToken:cancellationToken).ConfigureAwait(false);
        }
        else final = new GeneratedTextBundle { Kind = TextContentKind.Quest, PrimaryText = summary };

        var fields = final.Fields.ToList();
        fields.Add(new("questId", plan.Grammar.Id, "quest.id", false));
        fields.Add(new("questName", plan.Grammar.Name, "quest.name", false));
        for (int i = 0; i < steps.Count; i++) fields.Add(new($"objective{i + 1}", steps[i].Bundle.PrimaryText, "quest.objective", i == 0));
        var tags = final.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        tags["quest.id"] = plan.Grammar.Id; tags["quest.mode"] = plan.Diagnostics.Mode; tags["quest.steps"] = plan.Steps.Count.ToString(CultureInfo.InvariantCulture); tags["quest.cost"] = plan.TotalCost.ToString("0.###",CultureInfo.InvariantCulture);
        final = final with { Fields = fields, Tags = tags };
        return new(plan, final, steps, summaryContext);
    }

    public static GenerationContext BuildPlanContext(QuestPlan plan, GenerationContext baseContext)
    {
        var values = baseContext.Data.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        values["quest.id"] = plan.Grammar.Id; values["quest.name"] = plan.Grammar.Name; values["quest.stepCount"] = plan.Steps.Count; values["quest.totalCost"] = plan.TotalCost; values["quest.plannerMode"] = plan.Diagnostics.Mode;
        foreach (StoryletBinding binding in plan.Bindings)
        {
            values["binding." + binding.Id + ".id"] = binding.Candidate.Id; values["binding." + binding.Id + ".name"] = binding.Candidate.Name; values["quest." + binding.Id] = binding.Candidate.Name; values[binding.Id] = binding.Candidate.Name;
        }
        foreach (var pair in plan.Grammar.EffectiveRealizationParameters) values[pair.Key] = pair.Value;
        var tags = new HashSet<string>(baseContext.EffectiveTags, StringComparer.OrdinalIgnoreCase); tags.UnionWith(plan.Grammar.EffectiveTags);
        return new(baseContext.ProjectId,"quest-plan",plan.Grammar.Id,values,tags);
    }

    private static string ObjectiveText(QuestPlanStep step, QuestPlan plan)
    {
        string text = step.Action.Name.Trim();
        foreach (StoryletBinding binding in plan.Bindings.Concat(step.Bindings)) text = text.Replace("{" + binding.Id + "}", binding.Candidate.Name, StringComparison.OrdinalIgnoreCase);
        return text.EndsWith('.') ? text : text + ".";
    }
}

using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Selection;

namespace Playroma.Text.Generators.Stages;

public sealed class ShuffleBagStage : ITextGenerationStage
{
    public const string StageKind = "phase1.shuffle-bag";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = StageSettingsReader.GetStringArray(context.Stage, "items");
        if (items.Count == 0) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "shuffle-bag requires a non-empty 'items' array."));
        var count = Math.Max(1, StageSettingsReader.GetInt(context.Stage, "count", 1));
        var separator = StageSettingsReader.GetString(context.Stage, "separator", " ");
        var bag = new ShuffleBag<string>(items, context.Random);
        var selected = bag.DrawMany(count);
        foreach (var value in selected)
        {
            context.Budget.ConsumeCandidateAttempt();
            context.Trace.Add(context.Stage.Id, "shuffle-bag.draw", output: value);
        }
        return ValueTask.FromResult(GenerationStageResult.Ok(StageUtilities.Write(context, string.Join(separator, selected), "selection", "shuffle-selection")));
    }
}

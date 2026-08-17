using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators.Stages;

public sealed class RandomListStage : ITextGenerationStage
{
    public const string StageKind = "phase1.random-list";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = StageSettingsReader.GetStringArray(context.Stage, "items");
        if (items.Count == 0) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "random-list requires a non-empty 'items' array."));
        var count = Math.Max(1, StageSettingsReader.GetInt(context.Stage, "count", 1));
        var unique = StageSettingsReader.GetBool(context.Stage, "unique", false);
        var separator = StageSettingsReader.GetString(context.Stage, "separator", " ");
        var selected = new List<string>(count);
        var pool = items.ToList();
        for (var i = 0; i < count; i++)
        {
            context.Budget.ConsumeCandidateAttempt();
            if (pool.Count == 0) break;
            var index = context.Random.NextInt(pool.Count);
            selected.Add(pool[index]);
            context.Trace.Add(context.Stage.Id, "random-list.pick", output: pool[index], metadata: new Dictionary<string, string> { ["index"] = index.ToString() });
            if (unique) pool.RemoveAt(index);
        }
        var text = string.Join(separator, selected);
        var bundle = StageUtilities.Write(context, text, "selection", "selection");
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
    }
}

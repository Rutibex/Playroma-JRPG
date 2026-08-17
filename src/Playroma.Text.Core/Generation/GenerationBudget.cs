using System.Diagnostics;

namespace Playroma.Text.Core.Generation;

public sealed record GenerationBudgetOptions(
    int MaxStages = 64,
    int MaxTraceNodes = 10_000,
    int MaxOutputCharacters = 100_000,
    int MaxCandidateAttempts = 10_000,
    TimeSpan? MaxElapsed = null)
{
    public TimeSpan EffectiveMaxElapsed => MaxElapsed ?? TimeSpan.FromSeconds(5);
}

public sealed class GenerationBudget
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _stages;
    private int _traceNodes;
    private int _candidateAttempts;

    public GenerationBudgetOptions Options { get; }
    public GenerationBudget(GenerationBudgetOptions? options = null) => Options = options ?? new GenerationBudgetOptions();

    public void ConsumeStage() { if (++_stages > Options.MaxStages) throw new GenerationBudgetExceededException("Stage budget exceeded."); CheckTime(); }
    public void ConsumeTraceNode() { if (++_traceNodes > Options.MaxTraceNodes) throw new GenerationBudgetExceededException("Trace-node budget exceeded."); CheckTime(); }
    public void ConsumeCandidateAttempt() { if (++_candidateAttempts > Options.MaxCandidateAttempts) throw new GenerationBudgetExceededException("Candidate-attempt budget exceeded."); CheckTime(); }
    public void CheckOutputLength(int characters) { if (characters > Options.MaxOutputCharacters) throw new GenerationBudgetExceededException("Output-character budget exceeded."); CheckTime(); }
    public void CheckTime() { if (_stopwatch.Elapsed > Options.EffectiveMaxElapsed) throw new GenerationBudgetExceededException("Generation time budget exceeded."); }
}

public sealed class GenerationBudgetExceededException : Exception
{
    public GenerationBudgetExceededException(string message) : base(message) { }
}

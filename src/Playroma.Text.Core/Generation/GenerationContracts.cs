namespace Playroma.Text.Core.Generation;

public interface ITextGenerationStage
{
    string Kind { get; }
    ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken);
}

public interface IGenerationStageRegistry
{
    bool TryGet(string kind, out ITextGenerationStage? stage);
    IReadOnlyCollection<string> Kinds { get; }
}

public sealed class GenerationStageRegistry : IGenerationStageRegistry
{
    private readonly Dictionary<string, ITextGenerationStage> _stages = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Kinds => _stages.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(ITextGenerationStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (string.IsNullOrWhiteSpace(stage.Kind)) throw new ArgumentException("Stage kind cannot be empty.", nameof(stage));
        _stages[stage.Kind] = stage;
    }

    public bool TryGet(string kind, out ITextGenerationStage? stage) => _stages.TryGetValue(kind, out stage);

    /// <summary>
    /// Returns the registered stage for <paramref name="kind"/>, or <c>null</c> when no stage is registered.
    /// This convenience API is retained for compatibility with the phase test suites and editor tooling.
    /// </summary>
    public ITextGenerationStage? Find(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        return _stages.TryGetValue(kind, out ITextGenerationStage? stage) ? stage : null;
    }
}

public sealed record GenerationStageContext(
    TextRecipe Recipe,
    TextRecipeStage Stage,
    GenerationContext Context,
    GeneratedTextBundle WorkingBundle,
    Playroma.Text.Core.Random.IDeterministicRandom Random,
    GenerationBudget Budget,
    GenerationTraceBuilder Trace);

public sealed record GenerationStageResult(
    bool Success,
    GeneratedTextBundle Bundle,
    IReadOnlyList<GenerationDiagnostic> Diagnostics)
{
    public static GenerationStageResult Ok(GeneratedTextBundle bundle) => new(true, bundle, Array.Empty<GenerationDiagnostic>());
    public static GenerationStageResult Fail(GeneratedTextBundle bundle, params GenerationDiagnostic[] diagnostics) => new(false, bundle, diagnostics);
}

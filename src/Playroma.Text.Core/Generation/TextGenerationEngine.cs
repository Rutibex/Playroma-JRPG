namespace Playroma.Text.Core.Generation;

public sealed class TextGenerationEngine
{
    private readonly IGenerationStageRegistry _registry;
    public TextGenerationEngine(IGenerationStageRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async ValueTask<GeneratedTextBundle> GenerateAsync(
        TextRecipe recipe,
        GenerationContext? context = null,
        ulong? seed = null,
        GenerationBudgetOptions? budgetOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var diagnostics = TextRecipeValidator.Validate(recipe).ToList();
        if (diagnostics.Any(d => d.Severity == GenerationDiagnosticSeverity.Error))
            return new GeneratedTextBundle { Kind = recipe.Kind, Diagnostics = diagnostics };

        var actualSeed = seed ?? Playroma.Text.Core.Random.SeedFactory.CreateSeed();
        var random = new Playroma.Text.Core.Random.Xoshiro256StarStar(actualSeed);
        var budget = new GenerationBudget(budgetOptions);
        var trace = new GenerationTraceBuilder(budget);
        var bundle = new GeneratedTextBundle { Kind = recipe.Kind };

        try
        {
            foreach (var stageDef in recipe.Stages.Where(s => s.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.ConsumeStage();
                if (!_registry.TryGet(stageDef.Kind, out var stage) || stage is null)
                {
                    diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownStage, GenerationDiagnosticSeverity.Error, $"Unknown generation stage '{stageDef.Kind}'.", stageDef.Id));
                    break;
                }

                var stageRandom = random.Fork(stageDef.Id);
                trace.Add(stageDef.Id, "stage.begin");
                var result = await stage.ExecuteAsync(new GenerationStageContext(recipe, stageDef, context ?? new GenerationContext(), bundle, stageRandom, budget, trace), cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(result.Diagnostics);
                bundle = result.Bundle;
                budget.CheckOutputLength(bundle.PrimaryText?.Length ?? 0);
                trace.Add(stageDef.Id, result.Success ? "stage.end" : "stage.failed", output: bundle.PrimaryText);
                if (!result.Success) break;
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.Cancelled, GenerationDiagnosticSeverity.Warning, "Generation was cancelled."));
        }
        catch (GenerationBudgetExceededException ex)
        {
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.BudgetExceeded, GenerationDiagnosticSeverity.Error, ex.Message));
        }
        catch (Exception ex)
        {
            diagnostics.Add(new GenerationDiagnostic(GenerationDiagnosticCodes.StageFailed, GenerationDiagnosticSeverity.Error, "A generation stage failed.", Detail: ex.ToString()));
        }

        return bundle with
        {
            Provenance = new GenerationProvenance(recipe.Id, recipe.Version, actualSeed, recipe.Fingerprint(), DateTimeOffset.UtcNow),
            Trace = trace.Build(),
            Diagnostics = diagnostics
        };
    }
}

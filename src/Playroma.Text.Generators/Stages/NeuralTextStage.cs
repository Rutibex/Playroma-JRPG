using System.Text.RegularExpressions;
using Playroma.Text.Core.AI;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators.Stages;

public sealed class NeuralTextStage : ITextGenerationStage
{
    public const string StageKind = "phase14.neural-text";
    private readonly INeuralTextProviderResolver _providers;
    public NeuralTextStage(INeuralTextProviderResolver? providers = null) => _providers = providers ?? EmptyNeuralTextProviderResolver.Instance;
    public string Kind => StageKind;

    public async ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        var providerId = StageSettingsReader.GetString(context.Stage, "providerId");
        if (string.IsNullOrWhiteSpace(providerId) || !_providers.TryResolve(providerId, out var provider))
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.NeuralProviderUnavailable, GenerationDiagnosticSeverity.Error, $"Optional AI provider '{providerId}' is not configured or enabled.", context.Stage.Id));

        var promptTemplate = StageSettingsReader.GetString(context.Stage, "prompt", "{{text}}");
        var prompt = Expand(promptTemplate, context);
        var system = StageSettingsReader.GetString(context.Stage, "systemPrompt", string.Empty);
        var includeContext = StageSettingsReader.GetBool(context.Stage, "includeProjectContext", false);
        var maximumOutput = Math.Clamp(StageSettingsReader.GetInt(context.Stage, "maximumOutputCharacters", 2000), 1, 8000);
        IReadOnlyDictionary<string, object?>? requestContext = includeContext ? context.Context.Data : null;
        try
        {
            var response = await provider.GenerateAsync(new NeuralTextRequest(prompt, string.IsNullOrWhiteSpace(system) ? null : system, context.Random.Seed, maximumOutput, requestContext), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response.Text))
                return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.NeuralOutputRejected, GenerationDiagnosticSeverity.Error, "Optional AI provider returned empty text.", context.Stage.Id));
            var field = StageSettingsReader.FieldName(context.Stage, "text");
            var primary = StageSettingsReader.IsPrimary(context.Stage, true);
            var bundle = context.WorkingBundle.WithField(field, response.Text, "neural", primary);
            var tags = bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            tags["neural.provider"] = response.ProviderId;
            if (!string.IsNullOrWhiteSpace(response.Model)) tags["neural.model"] = response.Model!;
            tags["neural.projectContextIncluded"] = includeContext ? "true" : "false";
            bundle = bundle with { Tags = tags };
            context.Trace.Add(context.Stage.Id, "neural.generate", input: prompt, output: response.Text, metadata: new Dictionary<string, string> { ["provider"] = response.ProviderId, ["projectContext"] = includeContext.ToString() });
            return GenerationStageResult.Ok(bundle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.NeuralProviderFailed, GenerationDiagnosticSeverity.Error, "Optional AI provider failed.", context.Stage.Id, ex.Message));
        }
    }

    private static string Expand(string template, GenerationStageContext context) => Regex.Replace(template, @"\{\{(?<key>[A-Za-z0-9_.-]+)\}\}", m =>
    {
        var key = m.Groups["key"].Value;
        if (key.Equals("text", StringComparison.OrdinalIgnoreCase)) return context.WorkingBundle.PrimaryText ?? string.Empty;
        var field = context.WorkingBundle.Fields.LastOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (field is not null) return field.Text;
        return context.Context.Data.TryGetValue(key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    });
}

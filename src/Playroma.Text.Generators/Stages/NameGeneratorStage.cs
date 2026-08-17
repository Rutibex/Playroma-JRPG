using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Names;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Generators.Stages;

public sealed class NameGeneratorStage : ITextGenerationStage
{
    public const string StageKind = "phase2.name-generate";
    private readonly NameGenerationEngine _engine;
    public string Kind => StageKind;

    public NameGeneratorStage(NameGenerationEngine engine) => _engine = engine;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuredProfile = StageSettingsReader.GetString(context.Stage, "profile", "common");
        var profile = ReadContextString(context.Context, "culture") ?? ReadContextString(context.Context, "languageProfile") ?? configuredProfile;
        if (!TryEnum(StageSettingsReader.GetString(context.Stage, "domain", "custom"), out NameDomain domain))
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, "name-generate has an invalid 'domain'."));
        if (!TryEnum(StageSettingsReader.GetString(context.Stage, "strategy", "hybrid"), out NameGenerationStrategy strategy))
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, "name-generate has an invalid 'strategy'."));

        var existing = ReadExisting(context);
        var request = new NameGenerationRequest(
            profile, domain, strategy,
            CandidateCount: Math.Clamp(StageSettingsReader.GetInt(context.Stage, "candidateCount", 32), 1, 512),
            MinLength: NullablePositiveInt(context.Stage, "minLength"),
            MaxLength: NullablePositiveInt(context.Stage, "maxLength"),
            MinSyllables: NullablePositiveInt(context.Stage, "minSyllables"),
            MaxSyllables: NullablePositiveInt(context.Stage, "maxSyllables"),
            MarkovOrder: Math.Clamp(StageSettingsReader.GetInt(context.Stage, "markovOrder", 3), 1, 8),
            MarkovBlend: NullableDouble(context.Stage, "markovBlend"),
            MaxExistingSimilarity: StageSettingsReader.GetDouble(context.Stage, "maxExistingSimilarity", .88),
            MaxCorpusSimilarity: StageSettingsReader.GetDouble(context.Stage, "maxCorpusSimilarity", .965),
            ExistingNames: existing,
            RequiredFragment: StageSettingsReader.GetString(context.Stage, "requiredFragment", string.Empty) is { Length: > 0 } fragment ? fragment : null);

        NameGenerationResult result;
        try { result = _engine.Generate(request, context.Random); }
        catch (KeyNotFoundException ex)
        {
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle,
                new GenerationDiagnostic(GenerationDiagnosticCodes.UnknownLanguageProfile, GenerationDiagnosticSeverity.Error, ex.Message, context.Stage.Id)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle,
                new GenerationDiagnostic(GenerationDiagnosticCodes.InvalidNameProfile, GenerationDiagnosticSeverity.Error, ex.Message, context.Stage.Id)));
        }

        foreach (var _ in result.Candidates) context.Budget.ConsumeCandidateAttempt();
        foreach (var _ in result.Rejected) context.Budget.ConsumeCandidateAttempt();
        context.Trace.Add(context.Stage.Id, "name.select", output: result.Text, metadata: new Dictionary<string, string>
        {
            ["profile"] = result.ProfileId,
            ["domain"] = result.Domain.ToString(),
            ["strategy"] = result.Strategy.ToString(),
            ["acceptedCandidates"] = result.Candidates.Count.ToString(),
            ["rejectedCandidates"] = result.Rejected.Count.ToString()
        });

        var tags = context.WorkingBundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        tags["culture"] = result.ProfileId;
        tags["nameDomain"] = result.Domain.ToString();
        tags["nameStrategy"] = result.Strategy.ToString();

        var scores = context.WorkingBundle.Scores.Concat(new[] { new GenerationScore("nameQuality", result.Score, "Name quality/novelty score") }).ToArray();
        var bundle = StageUtilities.Write(context, result.Text, "name", "generated-name") with { Tags = tags, Scores = scores };
        if (!result.Success)
            return ValueTask.FromResult(GenerationStageResult.Fail(bundle, new GenerationDiagnostic(
                GenerationDiagnosticCodes.NameGenerationFailed, GenerationDiagnosticSeverity.Error,
                $"Could not find a name satisfying the configured constraints after {result.Rejected.Count} candidates.", context.Stage.Id)));
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
    }

    private static bool TryEnum<T>(string text, out T value) where T : struct, Enum => Enum.TryParse(text, true, out value);
    private static string? ReadContextString(GenerationContext context, string key) => context.Data.TryGetValue(key, out var value) ? value as string : null;
    private static int? NullablePositiveInt(TextRecipeStage stage, string key) => StageSettingsReader.TryGet(stage, key, out var value) && value.TryGetInt32(out var parsed) && parsed > 0 ? parsed : null;
    private static double? NullableDouble(TextRecipeStage stage, string key) => StageSettingsReader.TryGet(stage, key, out var value) && value.TryGetDouble(out var parsed) ? parsed : null;

    internal static IReadOnlyCollection<string> ReadExisting(GenerationStageContext context)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (StageSettingsReader.TryGet(context.Stage, "existingNames", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var item in array.EnumerateArray()) if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text) result.Add(text);
        var key = StageSettingsReader.GetString(context.Stage, "existingNamesContextKey", "existingNames");
        if (context.Context.Data.TryGetValue(key, out var value))
        {
            if (value is IEnumerable<string> strings)
            {
                foreach (var existingName in strings)
                    if (!string.IsNullOrWhiteSpace(existingName))
                        result.Add(existingName);
            }
            else if (value is string singleExistingName && !string.IsNullOrWhiteSpace(singleExistingName))
            {
                result.Add(singleExistingName);
            }
        }
        return result;
    }
}

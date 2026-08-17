using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Names;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Generators.Stages;

public sealed class NameSetStage : ITextGenerationStage
{
    public const string StageKind = "phase2.name-set";
    private readonly NameGenerationEngine _engine;
    public string Kind => StageKind;
    public NameSetStage(NameGenerationEngine engine) => _engine = engine;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StageSettingsReader.TryGet(context.Stage, "entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, "name-set requires an 'entries' array."));
        var profile = context.Context.Data.TryGetValue("culture", out var culture) && culture is string cultureId ? cultureId
            : context.Context.Data.TryGetValue("languageProfile", out var language) && language is string languageId ? languageId
            : StageSettingsReader.GetString(context.Stage, "profile", "common");
        var seen = new HashSet<string>(NameGeneratorStage.ReadExisting(context), StringComparer.OrdinalIgnoreCase);
        var bundle = context.WorkingBundle;
        var index = 0;
        var generatedAny = false;

        foreach (var entry in entries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var field = Get(entry, "field", $"name{index + 1}");
            if (!Enum.TryParse<NameDomain>(Get(entry, "domain", "custom"), true, out var domain))
                return ValueTask.FromResult(StageSettingsReader.Invalid(context, $"name-set entry '{field}' has an invalid domain."));
            Enum.TryParse<NameGenerationStrategy>(Get(entry, "strategy", "hybrid"), true, out var strategy);
            var count = Math.Clamp(GetInt(entry, "count", 1), 1, 100);
            var separator = Get(entry, "separator", "\n");
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var request = new NameGenerationRequest(profile, domain, strategy, CandidateCount: 32, ExistingNames: seen);
                NameGenerationResult result;
                try { result = _engine.Generate(request, context.Random.Fork($"entry:{index}:name:{i}")); }
                catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
                {
                    return ValueTask.FromResult(GenerationStageResult.Fail(bundle, new GenerationDiagnostic(
                        ex is KeyNotFoundException ? GenerationDiagnosticCodes.UnknownLanguageProfile : GenerationDiagnosticCodes.InvalidNameProfile,
                        GenerationDiagnosticSeverity.Error, ex.Message, context.Stage.Id)));
                }
                foreach (var _ in result.Candidates) context.Budget.ConsumeCandidateAttempt();
                foreach (var _ in result.Rejected) context.Budget.ConsumeCandidateAttempt();
                if (!result.Success) return ValueTask.FromResult(GenerationStageResult.Fail(bundle, new GenerationDiagnostic(
                    GenerationDiagnosticCodes.NameGenerationFailed, GenerationDiagnosticSeverity.Error, $"Could not generate name-set entry '{field}'.", context.Stage.Id)));
                names.Add(result.Text); seen.Add(result.Text); generatedAny = true;
            }
            var text = string.Join(separator, names);
            var isPrimary = entry.TryGetProperty("isPrimary", out var primary) && primary.ValueKind is JsonValueKind.True or JsonValueKind.False && primary.GetBoolean();
            bundle = bundle.WithField(field, text, "generated-name-set", isPrimary);
            context.Trace.Add(context.Stage.Id, "name-set.entry", output: text, metadata: new Dictionary<string, string> { ["field"] = field, ["domain"] = domain.ToString(), ["count"] = count.ToString(), ["culture"] = profile });
            index++;
        }
        if (!generatedAny) return ValueTask.FromResult(StageSettingsReader.Invalid(context, "name-set did not contain any usable entries."));
        var tags = bundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        tags["culture"] = profile;
        bundle = bundle with { Tags = tags };
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
    }

    private static string Get(JsonElement element, string key, string fallback) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static int GetInt(JsonElement element, string key, int fallback) => element.TryGetProperty(key, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;
}

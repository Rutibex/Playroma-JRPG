using System.Text.Json;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Markov;

namespace Playroma.Text.Generators.Stages;

public sealed class MarkovGenerationStage : ITextGenerationStage
{
    public const string StageKind = "phase5.markov";
    private readonly ICorpusResolver? _resolver;
    private readonly IMarkovModelCache? _cache;
    public string Kind => StageKind;

    public MarkovGenerationStage(ICorpusResolver? resolver = null, IMarkovModelCache? cache = null)
    {
        _resolver = resolver;
        _cache = cache;
    }

    public async ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var corpus = await ResolveCorpusAsync(context, cancellationToken).ConfigureAwait(false);
        if (corpus is null)
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.MissingCorpus,
                GenerationDiagnosticSeverity.Error, "Markov stage requires 'corpusId', 'corpusText', or an inline 'corpus' object.", context.Stage.Id));

        var options = ReadOptions(context.Stage);
        NGramMarkovModel model;
        var cacheHit = false;
        GenerationDiagnostic? cacheWarning = null;
        try
        {
            var cached = _cache is null ? null : await _cache.TryLoadAsync(corpus, options, cancellationToken).ConfigureAwait(false);
            if (cached is not null) { model = cached; cacheHit = true; }
            else
            {
                model = NGramMarkovModel.Train(corpus, options);
                if (_cache is not null)
                {
                    try { await _cache.SaveAsync(model, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        cacheWarning = new GenerationDiagnostic(GenerationDiagnosticCodes.MarkovCacheError, GenerationDiagnosticSeverity.Warning,
                            "The Markov model was generated but its disposable cache could not be saved.", context.Stage.Id, ex.Message);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.MarkovTrainingFailed,
                GenerationDiagnosticSeverity.Error, ex.Message, context.Stage.Id));
        }

        var guard = new CorpusSimilarityGuard(corpus);
        string? accepted = null;
        CorpusSimilarityReport? acceptedReport = null;
        CorpusSimilarityReport? closestRejected = null;
        var attempts = Math.Clamp(options.CandidateAttempts, 1, 512);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Budget.ConsumeCandidateAttempt();
            context.Budget.CheckTime();
            var candidate = model.Generate(context.Random.Fork("markov:" + attempt));
            context.Budget.CheckOutputLength(candidate.Length);
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (guard.IsAcceptable(candidate, options, out var report))
            {
                accepted = candidate;
                acceptedReport = report;
                break;
            }
            if (closestRejected is null || report.CombinedSimilarity < closestRejected.CombinedSimilarity) closestRejected = report;
        }

        if (accepted is null)
        {
            var detail = closestRejected is null ? null : $"Closest candidate similarity: {closestRejected.CombinedSimilarity:0.000}.";
            return GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.MarkovGenerationFailed,
                GenerationDiagnosticSeverity.Error, $"Could not generate sufficiently original text after {attempts} candidates.", context.Stage.Id, detail));
        }

        var similarity = acceptedReport?.CombinedSimilarity ?? 0;
        context.Trace.Add(context.Stage.Id, "markov.generate", input: corpus.Id, output: accepted, metadata: new Dictionary<string, string>
        {
            ["mode"] = options.Mode.ToString(),
            ["order"] = options.Order.ToString(),
            ["backoff"] = options.VariableOrderBackoff.ToString(),
            ["cache"] = cacheHit ? "hit" : "trained",
            ["similarity"] = similarity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            ["states"] = model.Statistics.StateCount.ToString(),
            ["transitions"] = model.Statistics.TransitionCount.ToString()
        });

        var tags = context.WorkingBundle.Tags.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        tags["markovCorpus"] = corpus.Id;
        tags["markovMode"] = options.Mode.ToString();
        tags["markovOrder"] = options.Order.ToString();
        tags["markovModel"] = model.Statistics.ModelFingerprint;
        var scores = context.WorkingBundle.Scores.Concat(new[]
        {
            new GenerationScore("originality", 1 - similarity, "1 minus closest corpus similarity"),
            new GenerationScore("corpusSimilarity", similarity, "Closest source-corpus similarity")
        }).ToArray();
        var bundle = StageUtilities.Write(context, accepted, "text", "markov-generated") with { Tags = tags, Scores = scores };
        return cacheWarning is null ? GenerationStageResult.Ok(bundle) : new GenerationStageResult(true, bundle, [cacheWarning]);
    }

    private async ValueTask<TextCorpus?> ResolveCorpusAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        if (StageSettingsReader.TryGet(context.Stage, "corpus", out var inline) && inline.ValueKind == JsonValueKind.Object)
            return inline.Deserialize<TextCorpus>(CorpusJson.Options);
        var corpusText = StageSettingsReader.GetString(context.Stage, "corpusText");
        if (!string.IsNullOrWhiteSpace(corpusText))
            return new TextCorpus(context.Recipe.Id + ".inline", 1, StageSettingsReader.GetString(context.Stage, "corpusName", context.Recipe.Name + " Corpus"), corpusText,
                [new CorpusSourceMetadata("Inline recipe corpus", License: "Project-authored")]);
        var id = StageSettingsReader.GetString(context.Stage, "corpusId");
        return !string.IsNullOrWhiteSpace(id) && _resolver is not null ? await _resolver.ResolveAsync(id, cancellationToken).ConfigureAwait(false) : null;
    }

    internal static MarkovTrainingOptions ReadOptions(TextRecipeStage stage)
    {
        var mode = Enum.TryParse<CorpusTokenMode>(StageSettingsReader.GetString(stage, "mode", "character"), true, out var parsed) ? parsed : CorpusTokenMode.Character;
        return new MarkovTrainingOptions(
            mode,
            Math.Clamp(StageSettingsReader.GetInt(stage, "order", mode == CorpusTokenMode.Character ? 4 : 2), 1, 8),
            Math.Clamp(StageSettingsReader.GetInt(stage, "minimumOrder", 1), 1, 8),
            StageSettingsReader.GetBool(stage, "backoff", true),
            StageSettingsReader.GetBool(stage, "preserveCase", true),
            StageSettingsReader.GetBool(stage, "includePunctuation", true),
            Math.Clamp(StageSettingsReader.GetInt(stage, "minimumTokens", mode == CorpusTokenMode.Character ? 12 : 5), 1, 10_000),
            Math.Clamp(StageSettingsReader.GetInt(stage, "maximumTokens", mode == CorpusTokenMode.Character ? 120 : 40), 1, 10_000),
            StageSettingsReader.GetBool(stage, "preventExactMatches", true),
            StageSettingsReader.GetDouble(stage, "maximumSimilarity", .86),
            Math.Clamp(StageSettingsReader.GetInt(stage, "candidateAttempts", 64), 1, 512)).Normalize();
    }
}

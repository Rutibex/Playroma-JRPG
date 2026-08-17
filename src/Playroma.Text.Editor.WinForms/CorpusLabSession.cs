using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Corpora;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Markov;

namespace Playroma.Text.Editor.WinForms;

public sealed class CorpusLabSession
{
    private readonly TextProjectLayout _layout;
    private readonly TextCorpusLibrary _library;
    private readonly JsonMarkovModelCache _cache;
    private readonly CorpusImportService _importer = new();

    public CorpusLabSession(string projectRoot)
    {
        _layout = new TextProjectLayout(projectRoot);
        _layout.EnsureCreated();
        _library = new TextCorpusLibrary(_layout.Corpora);
        _cache = new JsonMarkovModelCache(_layout.MarkovCache);
    }

    public IReadOnlyList<TextCorpusLibraryEntry> Corpora { get; private set; } = Array.Empty<TextCorpusLibraryEntry>();
    public NGramMarkovModel? CurrentModel { get; private set; }
    public MarkovModelStatistics? Statistics => CurrentModel?.Statistics;
    public bool LastCacheWriteSucceeded { get; private set; } = true;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
        => Corpora = await _library.LoadAllAsync(cancellationToken).ConfigureAwait(false);

    public async Task<string> SaveAsync(TextCorpus corpus, string? existingPath, CancellationToken cancellationToken = default)
    {
        var path = await _library.SaveAsync(corpus with { UpdatedAtUtc = DateTimeOffset.UtcNow }, existingPath, cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }

    public Task<TextCorpus> ImportAsync(string path, CorpusImportOptions options, CancellationToken cancellationToken = default)
        => _importer.ImportFileAsync(path, options, cancellationToken);

    public TextCorpus FromPastedText(string text, string name, CorpusImportOptions? options = null)
        => _importer.FromPastedText(text, name, options);

    public async Task<NGramMarkovModel> TrainAsync(TextCorpus corpus, MarkovTrainingOptions options, bool forceRetrain = false, CancellationToken cancellationToken = default)
    {
        options = options.Normalize();
        var model = forceRetrain ? null : await _cache.TryLoadAsync(corpus, options, cancellationToken).ConfigureAwait(false);
        LastCacheWriteSucceeded = true;
        if (model is null)
        {
            model = NGramMarkovModel.Train(corpus, options);
            try { await _cache.SaveAsync(model, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { LastCacheWriteSucceeded = false; }
        }
        CurrentModel = model;
        return model;
    }

    public IReadOnlyList<(string Text, CorpusSimilarityReport Similarity)> GenerateSamples(TextCorpus corpus, MarkovTrainingOptions options, ulong seed, int count = 10)
    {
        if (CurrentModel is null || CurrentModel.Statistics.ModelFingerprint != NGramMarkovModel.ComputeModelFingerprint(corpus.Fingerprint(), options))
            CurrentModel = NGramMarkovModel.Train(corpus, options);
        var guard = new CorpusSimilarityGuard(corpus);
        var result = new List<(string, CorpusSimilarityReport)>();
        var random = new Xoshiro256StarStar(seed);
        var attempts = 0;
        while (result.Count < Math.Clamp(count, 1, 100) && attempts < Math.Max(64, count * options.CandidateAttempts))
        {
            attempts++;
            var candidate = CurrentModel.Generate(random.Fork("sample:" + attempts));
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (guard.IsAcceptable(candidate, options, out var report) && !result.Any(x => x.Item1.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                result.Add((candidate, report));
        }
        return result;
    }

    public ICorpusResolver Resolver => _library;
    public IMarkovModelCache Cache => _cache;
}

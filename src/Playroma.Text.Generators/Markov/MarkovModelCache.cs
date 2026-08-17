using System.Text.Json;
using Playroma.Text.Core.Corpora;

namespace Playroma.Text.Generators.Markov;

public interface IMarkovModelCache
{
    ValueTask<NGramMarkovModel?> TryLoadAsync(TextCorpus corpus, MarkovTrainingOptions options, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(NGramMarkovModel model, CancellationToken cancellationToken = default);
    string? GetCachePath(TextCorpus corpus, MarkovTrainingOptions options);
}

public sealed class JsonMarkovModelCache : IMarkovModelCache
{
    private readonly string _root;
    public JsonMarkovModelCache(string root) => _root = root;

    public string? GetCachePath(TextCorpus corpus, MarkovTrainingOptions options)
    {
        var fingerprint = NGramMarkovModel.ComputeModelFingerprint(corpus.Fingerprint(), options);
        var safeId = string.Concat(corpus.Id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'));
        return Path.Combine(_root, safeId, fingerprint + ".markov.json");
    }

    public async ValueTask<NGramMarkovModel?> TryLoadAsync(TextCorpus corpus, MarkovTrainingOptions options, CancellationToken cancellationToken = default)
    {
        var path = GetCachePath(corpus, options)!;
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<MarkovModelSnapshot>(stream, CorpusJson.Options, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || !string.Equals(snapshot.CorpusFingerprint, corpus.Fingerprint(), StringComparison.OrdinalIgnoreCase)) return null;
            return NGramMarkovModel.FromSnapshot(snapshot);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async ValueTask SaveAsync(NGramMarkovModel model, CancellationToken cancellationToken = default)
    {
        var snapshot = model.ToSnapshot();
        var safeId = string.Concat(snapshot.CorpusId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'));
        var path = Path.Combine(_root, safeId, snapshot.Statistics.ModelFingerprint + ".markov.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, snapshot, CorpusJson.Options, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }
}

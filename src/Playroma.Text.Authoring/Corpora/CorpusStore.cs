using System.Text.Json;
using Playroma.Text.Core.Corpora;

namespace Playroma.Text.Authoring.Corpora;

public interface ITextCorpusStore
{
    Task<TextCorpus> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string path, TextCorpus corpus, CancellationToken cancellationToken = default);
}

public sealed class JsonTextCorpusStore : ITextCorpusStore
{
    public async Task<TextCorpus> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TextCorpus>(stream, CorpusJson.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Corpus file is empty or invalid.");
    }

    public async Task SaveAsync(string path, TextCorpus corpus, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, corpus, CorpusJson.Options, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, true);
    }
}

public sealed record TextCorpusLibraryEntry(TextCorpus Corpus, string Path);

public sealed class TextCorpusLibrary : ICorpusResolver
{
    private readonly string _directory;
    private readonly ITextCorpusStore _store;
    private Dictionary<string, TextCorpusLibraryEntry>? _cache;

    public TextCorpusLibrary(string directory, ITextCorpusStore? store = null)
    {
        _directory = directory;
        _store = store ?? new JsonTextCorpusStore();
    }

    public async Task<IReadOnlyList<TextCorpusLibraryEntry>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var result = new List<TextCorpusLibraryEntry>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.playcorpus.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { result.Add(new TextCorpusLibraryEntry(await _store.LoadAsync(path, cancellationToken).ConfigureAwait(false), path)); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { /* malformed files remain visible on disk but do not break the studio */ }
        }
        _cache = result.GroupBy(x => x.Corpus.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last())
            .ToDictionary(x => x.Corpus.Id, StringComparer.OrdinalIgnoreCase);
        return result.OrderBy(x => x.Corpus.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async ValueTask<TextCorpus?> ResolveAsync(string corpusId, CancellationToken cancellationToken = default)
    {
        await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return _cache!.TryGetValue(corpusId, out var entry) ? entry.Corpus : null;
    }

    public async Task<string> SaveAsync(TextCorpus corpus, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = existingPath;
        if (string.IsNullOrWhiteSpace(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(_directory), StringComparison.OrdinalIgnoreCase))
        {
            var safe = string.Concat(corpus.Id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')).Trim('-');
            if (string.IsNullOrWhiteSpace(safe)) safe = "corpus";
            path = Path.Combine(_directory, safe + ".playcorpus.json");
        }
        await _store.SaveAsync(path, corpus, cancellationToken).ConfigureAwait(false);
        _cache = null;
        return path;
    }
}

using Playroma.Text.Core.Tracery;

namespace Playroma.Text.Authoring;

public interface ITraceryGrammarStore
{
    Task<TraceryGrammarDocument> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SavePlayromaAsync(string path, TraceryGrammarDocument grammar, CancellationToken cancellationToken = default);
    Task SaveClassicAsync(string path, TraceryGrammarDocument grammar, CancellationToken cancellationToken = default);
}

public sealed class JsonTraceryGrammarStore : ITraceryGrammarStore
{
    public async Task<TraceryGrammarDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
        return TraceryGrammarJson.Parse(json, id, id);
    }

    public Task SavePlayromaAsync(string path, TraceryGrammarDocument grammar, CancellationToken cancellationToken = default)
        => SaveAsync(path, TraceryGrammarJson.SerializePlayroma(grammar), cancellationToken);

    public Task SaveClassicAsync(string path, TraceryGrammarDocument grammar, CancellationToken cancellationToken = default)
        => SaveAsync(path, TraceryGrammarJson.SerializeClassic(grammar), cancellationToken);

    private static async Task SaveAsync(string path, string json, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class TraceryGrammarLibrary : ITraceryGrammarResolver
{
    private readonly Dictionary<string, TraceryGrammarDocument> _grammars = new(StringComparer.OrdinalIgnoreCase);
    public int Count => _grammars.Count;
    public IReadOnlyCollection<string> Ids => _grammars.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Add(TraceryGrammarDocument grammar) { ArgumentNullException.ThrowIfNull(grammar); _grammars[grammar.Id] = grammar; }
    public ValueTask<TraceryGrammarDocument?> ResolveAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_grammars.TryGetValue(id, out var grammar) ? grammar : null);
    }

    public static async Task<TraceryGrammarLibrary> LoadDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var library = new TraceryGrammarLibrary();
        if (!Directory.Exists(directory)) return library;
        var store = new JsonTraceryGrammarStore();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { library.Add(await store.LoadAsync(path, cancellationToken).ConfigureAwait(false)); }
            catch (InvalidDataException) { /* Other JSON assets may coexist in the directory. */ }
            catch (System.Text.Json.JsonException) { }
        }
        return library;
    }
}

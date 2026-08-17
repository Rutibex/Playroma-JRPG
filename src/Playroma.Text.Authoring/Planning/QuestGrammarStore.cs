using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Planning;

namespace Playroma.Text.Authoring.Planning;

public sealed class JsonQuestGrammarStore : IQuestGrammarResolver
{
    private readonly string _directory;
    public JsonQuestGrammarStore(string directory) { _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory))); Directory.CreateDirectory(_directory); }
    public string DirectoryPath => _directory;
    public string PathForId(string id) => Path.Combine(_directory, SafeFileName(id) + ".playquest.json");
    public bool Contains(string id) => !string.IsNullOrWhiteSpace(id) && File.Exists(PathForId(id));

    public async ValueTask<IReadOnlyList<QuestGrammarDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory); var result = new List<QuestGrammarDefinition>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.playquest.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                QuestGrammarDefinition? grammar = JsonSerializer.Deserialize<QuestGrammarDefinition>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), RecipeJson.Options);
                if (grammar is not null && QuestGrammarValidator.Validate(grammar).Count == 0) result.Add(grammar);
            }
            catch (JsonException) { }
        }
        return result;
    }

    public async ValueTask<QuestGrammarDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default) =>
        (await LoadAllAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<string> SaveAsync(QuestGrammarDefinition grammar, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        var errors = QuestGrammarValidator.Validate(grammar); if (errors.Count != 0) throw new InvalidOperationException("Quest grammar is invalid: " + string.Join(" ", errors));
        Directory.CreateDirectory(_directory); string path = string.IsNullOrWhiteSpace(existingPath) ? PathForId(grammar.Id) : Path.GetFullPath(existingPath);
        if (!IsUnderDirectory(path, _directory)) throw new InvalidOperationException("Quest grammars must be saved inside PlayromaText/quests.");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(grammar, RecipeJson.Options), cancellationToken).ConfigureAwait(false); return path;
    }

    public void Delete(string path)
    {
        string full = Path.GetFullPath(path); if (!IsUnderDirectory(full, _directory)) throw new InvalidOperationException("Quest grammars can only be deleted from PlayromaText/quests."); if (File.Exists(full)) File.Delete(full);
    }

    private static string SafeFileName(string value)
    {
        string safe = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "quest" : safe;
    }
    private static bool IsUnderDirectory(string path, string directory) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

public sealed class CompositeQuestGrammarResolver : IQuestGrammarResolver
{
    private readonly IReadOnlyList<QuestGrammarDefinition> _builtIns; private readonly IQuestGrammarResolver? _project;
    public CompositeQuestGrammarResolver(IEnumerable<QuestGrammarDefinition>? builtIns = null, IQuestGrammarResolver? project = null) { _builtIns = (builtIns ?? []).ToArray(); _project = project; }
    public async ValueTask<IReadOnlyList<QuestGrammarDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuestGrammarDefinition> project = _project is null ? [] : await _project.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return _builtIns.Concat(project).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async ValueTask<QuestGrammarDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default) => (await LoadAllAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

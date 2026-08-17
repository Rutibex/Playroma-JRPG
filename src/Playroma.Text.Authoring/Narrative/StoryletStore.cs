using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;

namespace Playroma.Text.Authoring.Narrative;

public sealed class JsonStoryletStore : IStoryletResolver
{
    private readonly string _directory;

    public JsonStoryletStore(string directory)
    {
        _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public string PathForId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Storylet ID is required.", nameof(id));
        return Path.Combine(_directory, SafeFileName(id) + ".playstorylet.json");
    }

    public bool Contains(string id) => !string.IsNullOrWhiteSpace(id) && File.Exists(PathForId(id));

    public async ValueTask<IReadOnlyList<StoryletDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var result = new List<StoryletDefinition>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.playstorylet.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                StoryletDefinition? storylet = JsonSerializer.Deserialize<StoryletDefinition>(json, RecipeJson.Options);
                if (storylet is not null && StoryletDefinitionValidator.Validate(storylet).Count == 0) result.Add(storylet);
            }
            catch (JsonException) { }
        }
        return result;
    }

    public async ValueTask<StoryletDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> SaveAsync(StoryletDefinition storylet, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storylet);
        var errors = StoryletDefinitionValidator.Validate(storylet);
        if (errors.Count != 0) throw new InvalidOperationException("Storylet is invalid: " + string.Join(" ", errors));
        Directory.CreateDirectory(_directory);
        string path = string.IsNullOrWhiteSpace(existingPath)
            ? PathForId(storylet.Id)
            : Path.GetFullPath(existingPath);
        if (!IsUnderDirectory(path, _directory)) throw new InvalidOperationException("Storylets must be saved inside the project's PlayromaText/storylets directory.");
        string json = JsonSerializer.Serialize(storylet, RecipeJson.Options);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public void Delete(string path)
    {
        string full = Path.GetFullPath(path);
        if (!IsUnderDirectory(full, _directory)) throw new InvalidOperationException("Storylets can only be deleted from the project's PlayromaText/storylets directory.");
        if (File.Exists(full)) File.Delete(full);
    }

    private static string SafeFileName(string value)
    {
        string safe = string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "storylet" : safe;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CompositeStoryletResolver : IStoryletResolver
{
    private readonly IReadOnlyList<StoryletDefinition> _builtIns;
    private readonly IStoryletResolver? _project;

    public CompositeStoryletResolver(IEnumerable<StoryletDefinition>? builtIns = null, IStoryletResolver? project = null)
    {
        _builtIns = (builtIns ?? Array.Empty<StoryletDefinition>()).ToArray();
        _project = project;
    }

    public async ValueTask<IReadOnlyList<StoryletDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoryletDefinition> project = _project is null ? Array.Empty<StoryletDefinition>() : await _project.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return _builtIns.Concat(project)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async ValueTask<StoryletDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default) =>
        (await LoadAllAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

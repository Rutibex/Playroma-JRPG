using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring;

public sealed record RecipeLibraryEntry(string Path, TextRecipe Recipe);

public sealed class TextRecipeLibrary
{
    private readonly ITextRecipeStore _store;
    public TextRecipeLibrary(ITextRecipeStore? store = null) => _store = store ?? new JsonTextRecipeStore();

    public async Task<IReadOnlyList<RecipeLibraryEntry>> LoadDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return Array.Empty<RecipeLibraryEntry>();
        var result = new List<RecipeLibraryEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.playtext.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new RecipeLibraryEntry(path, await _store.LoadAsync(path, cancellationToken).ConfigureAwait(false)));
        }
        return result;
    }
}

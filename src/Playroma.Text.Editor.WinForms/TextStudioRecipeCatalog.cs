using Playroma.Text.Authoring;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators;

namespace Playroma.Text.Editor.WinForms;

public sealed class TextStudioRecipeCatalog
{
    private readonly string _projectRoot;
    private readonly JsonTextRecipeStore _store = new();
    private readonly TextRecipeLibrary _library;

    public TextStudioRecipeCatalog(string projectRoot)
    {
        _projectRoot = projectRoot;
        _library = new TextRecipeLibrary(_store);
    }

    public async Task<IReadOnlyList<TextStudioRecipeEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = BuiltInRecipeCatalog.All
            .Select(x => new TextStudioRecipeEntry(x, BuiltInRecipeCatalog.CategoryFor(x), "Built-in", null, true))
            .ToList();

        var layout = new TextProjectLayout(_projectRoot);
        layout.EnsureCreated();
        foreach (var entry in await _library.LoadDirectoryAsync(layout.Recipes, cancellationToken).ConfigureAwait(false))
        {
            result.RemoveAll(x => string.Equals(x.Recipe.Id, entry.Recipe.Id, StringComparison.OrdinalIgnoreCase));
            result.Add(new TextStudioRecipeEntry(entry.Recipe, BuiltInRecipeCatalog.CategoryFor(entry.Recipe), "Project", entry.Path, false));
        }
        return result.OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Recipe.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<string> SaveProjectRecipeAsync(TextRecipe recipe, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        var layout = new TextProjectLayout(_projectRoot);
        layout.EnsureCreated();
        var path = existingPath;
        if (string.IsNullOrWhiteSpace(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(layout.Recipes), StringComparison.OrdinalIgnoreCase))
        {
            var safe = string.Concat(recipe.Id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')).Trim('-');
            if (string.IsNullOrWhiteSpace(safe)) safe = "custom-recipe";
            path = Path.Combine(layout.Recipes, safe + ".playtext.json");
        }
        await _store.SaveAsync(path, recipe, cancellationToken).ConfigureAwait(false);
        return path;
    }
}

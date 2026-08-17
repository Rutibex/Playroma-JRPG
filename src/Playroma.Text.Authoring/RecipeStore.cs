using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring;

public interface ITextRecipeStore
{
    Task<TextRecipe> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string path, TextRecipe recipe, CancellationToken cancellationToken = default);
}

public sealed class JsonTextRecipeStore : ITextRecipeStore
{
    public async Task<TextRecipe> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var recipe = await JsonSerializer.DeserializeAsync<TextRecipe>(stream, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        return recipe ?? throw new InvalidDataException($"'{path}' did not contain a valid Playroma text recipe.");
    }

    public async Task SaveAsync(string path, TextRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
    }
}

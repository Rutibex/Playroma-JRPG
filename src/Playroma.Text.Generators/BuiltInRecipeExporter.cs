using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators;

public static class BuiltInRecipeExporter
{
    public static async Task ExportPhase1Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase1Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playtext.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ExportPhase2Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase2Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playtext.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ExportPhase3Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase3Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playtext.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }
    public static async Task ExportPhase5Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase5Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playtext.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ExportPhase6Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase6Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playtext.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, recipe, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }


    public static async Task ExportPhase8Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase8Recipes.All)
            await File.WriteAllTextAsync(Path.Combine(directory, recipe.Id.Replace('.', '-') + ".playtext.json"), JsonSerializer.Serialize(recipe, RecipeJson.Options), cancellationToken).ConfigureAwait(false);
    }

    public static async Task ExportPhase12Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase12Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            await File.WriteAllTextAsync(Path.Combine(directory, safe + ".playtext.json"), JsonSerializer.Serialize(recipe, RecipeJson.Options), cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ExportPhase14Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var recipe in BuiltInPhase14Recipes.All)
        {
            var safe = string.Concat(recipe.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            await File.WriteAllTextAsync(Path.Combine(directory, safe + ".playtext.json"), JsonSerializer.Serialize(recipe, RecipeJson.Options), cancellationToken).ConfigureAwait(false);
        }
    }

}

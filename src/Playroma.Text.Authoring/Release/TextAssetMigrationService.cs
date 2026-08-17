using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring.Release;

public sealed record TextAssetMigrationItem(string Path, bool Changed, string Message);
public sealed record TextAssetMigrationReport(IReadOnlyList<TextAssetMigrationItem> Items)
{
    public int ChangedCount => Items.Count(x => x.Changed);
}

/// <summary>
/// Conservative release migration. Phase 14 never invents content: it parses recipes using the
/// current model and writes canonical JSON only when explicitly asked, creating .pre-phase14 backups.
/// </summary>
public sealed class TextAssetMigrationService
{
    public async Task<TextAssetMigrationReport> PreviewAsync(string projectRoot, CancellationToken cancellationToken = default)
        => await RunAsync(projectRoot, apply: false, cancellationToken).ConfigureAwait(false);

    public async Task<TextAssetMigrationReport> ApplyAsync(string projectRoot, CancellationToken cancellationToken = default)
        => await RunAsync(projectRoot, apply: true, cancellationToken).ConfigureAwait(false);

    private static async Task<TextAssetMigrationReport> RunAsync(string projectRoot, bool apply, CancellationToken cancellationToken)
    {
        var layout = new TextProjectLayout(projectRoot);
        layout.EnsureCreated();
        var items = new List<TextAssetMigrationItem>();
        foreach (var path in Directory.EnumerateFiles(layout.Recipes, "*.playtext.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var original = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var recipe = JsonSerializer.Deserialize<TextRecipe>(original, RecipeJson.Options) ?? throw new InvalidDataException("Recipe JSON returned null.");
                var canonical = JsonSerializer.Serialize(recipe, RecipeJson.Options) + Environment.NewLine;
                var changed = !Normalize(original).Equals(Normalize(canonical), StringComparison.Ordinal);
                if (changed && apply)
                {
                    var backup = path + ".pre-phase14";
                    if (!File.Exists(backup)) await File.WriteAllTextAsync(backup, original, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllTextAsync(path, canonical, cancellationToken).ConfigureAwait(false);
                }
                items.Add(new TextAssetMigrationItem(path, changed, changed ? (apply ? "Canonical JSON written; backup preserved." : "Canonical JSON rewrite available.") : "Already current."));
            }
            catch (Exception ex) { items.Add(new TextAssetMigrationItem(path, false, "Migration blocked: " + ex.Message)); }
        }
        return new TextAssetMigrationReport(items);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n").Trim();
}

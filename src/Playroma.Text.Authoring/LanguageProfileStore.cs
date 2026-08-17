using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Names;

namespace Playroma.Text.Authoring;

public sealed class LanguageProfileStore
{
    public async Task<LanguageProfile?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LanguageProfile>(stream, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string path, LanguageProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LanguageProfile>> LoadDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return Array.Empty<LanguageProfile>();
        var result = new List<LanguageProfile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.playlang.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
            if (profile is not null) result.Add(profile);
        }
        return result;
    }
}

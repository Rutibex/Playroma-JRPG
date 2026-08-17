using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Names;

namespace Playroma.Text.Generators;

public static class BuiltInLanguageProfileExporter
{
    public static async Task ExportPhase2Async(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var profile in BuiltInLanguageProfiles.All)
        {
            var safe = string.Concat(profile.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            var path = Path.Combine(directory, safe + ".playlang.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, profile, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }
}

using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms;

public sealed class TextStudioStateStore
{
    private readonly string _path;
    public TextStudioStateStore(string projectRoot)
    {
        var dir = Path.Combine(projectRoot, "data", "PlayromaText", "studio");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "playground-state.json");
    }

    public async Task<TextStudioProjectState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return TextStudioProjectState.Empty;
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<TextStudioProjectState>(stream, RecipeJson.Options, cancellationToken).ConfigureAwait(false)
                ?? TextStudioProjectState.Empty;
        }
        catch (JsonException)
        {
            return TextStudioProjectState.Empty;
        }
    }

    public async Task SaveAsync(TextStudioProjectState state, CancellationToken cancellationToken = default)
    {
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, state, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
        File.Move(temp, _path, true);
    }
}

using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring.AI;

public sealed record NeuralProviderSettings(
    string Id,
    string DisplayName,
    string Kind = "process",
    bool Enabled = false,
    string? ExecutablePath = null,
    string? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutSeconds = 60);

public sealed record NeuralProviderSettingsDocument(
    int Version,
    IReadOnlyList<NeuralProviderSettings> Providers)
{
    public static NeuralProviderSettingsDocument Empty { get; } = new(1, Array.Empty<NeuralProviderSettings>());
}

public sealed class NeuralProviderSettingsStore
{
    private readonly string _path;
    public NeuralProviderSettingsStore(string projectRoot) => _path = Path.Combine(new TextProjectLayout(projectRoot).Studio, "ai-providers.json");
    public string PathOnDisk => _path;

    public async Task<NeuralProviderSettingsDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return NeuralProviderSettingsDocument.Empty;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<NeuralProviderSettingsDocument>(stream, RecipeJson.Options, cancellationToken).ConfigureAwait(false) ?? NeuralProviderSettingsDocument.Empty;
    }

    public async Task SaveAsync(NeuralProviderSettingsDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, RecipeJson.Options), cancellationToken).ConfigureAwait(false);
        File.Move(temp, _path, true);
    }
}

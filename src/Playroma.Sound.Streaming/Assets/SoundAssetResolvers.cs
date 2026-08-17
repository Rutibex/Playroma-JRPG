using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Streaming.Assets;

public interface ISoundAssetResolver
{
    SoundGraphDefinition Resolve(string soundId);
}

public sealed class InMemorySoundAssetResolver : ISoundAssetResolver
{
    private readonly Dictionary<string, SoundGraphDefinition> _assets;

    public InMemorySoundAssetResolver(IEnumerable<SoundGraphDefinition> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
    }

    public InMemorySoundAssetResolver(IReadOnlyDictionary<string, SoundGraphDefinition> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = new Dictionary<string, SoundGraphDefinition>(assets, StringComparer.Ordinal);
    }

    public SoundGraphDefinition Resolve(string soundId) =>
        _assets.TryGetValue(soundId, out SoundGraphDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Sound graph '{soundId}' was not found.");
}

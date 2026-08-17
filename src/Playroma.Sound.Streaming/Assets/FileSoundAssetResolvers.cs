using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Presets;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Serialization;

namespace Playroma.Sound.Streaming.Assets;

/// <summary>
/// Resolves graph IDs from a directory containing files named &lt;soundId&gt;.playsound.
/// Resolution and caching occur during scene compilation, never on the audio thread.
/// </summary>
public sealed class DirectorySoundAssetResolver : ISoundAssetResolver
{
    private readonly string _rootDirectory;
    private readonly SoundGraphSerializer _serializer;
    private readonly Dictionary<string, SoundGraphDefinition> _cache = new(StringComparer.Ordinal);

    public DirectorySoundAssetResolver(string rootDirectory, SoundGraphSerializer? serializer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _serializer = serializer ?? new SoundGraphSerializer();
    }

    public SoundGraphDefinition Resolve(string soundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundId);
        if (_cache.TryGetValue(soundId, out SoundGraphDefinition? cached)) return cached;
        string path = SoundAssetPath.GetSafeAssetPath(_rootDirectory, soundId, ".playsound");
        if (!File.Exists(path)) throw new FileNotFoundException($"Sound graph '{soundId}' was not found in '{_rootDirectory}'.", path);
        SoundGraphDefinition definition = _serializer.LoadFile(path);
        if (!StringComparer.Ordinal.Equals(definition.Id, soundId))
            throw new InvalidDataException($"Sound file '{path}' declares ID '{definition.Id}', but '{soundId}' was requested.");
        _cache.Add(soundId, definition);
        return definition;
    }
}

/// <summary>
/// Resolves semantic .playgen requests and regenerates their graphs during scene preparation.
/// </summary>
public sealed class GeneratorRequestAssetResolver : ISoundAssetResolver
{
    private readonly string _rootDirectory;
    private readonly SoundGeneratorRequestSerializer _serializer;
    private readonly SoundGeneratorCatalog _catalog;
    private readonly Dictionary<string, SoundGraphDefinition> _cache = new(StringComparer.Ordinal);

    public GeneratorRequestAssetResolver(
        string rootDirectory,
        SoundGeneratorCatalog? catalog = null,
        SoundGeneratorRequestSerializer? serializer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _catalog = catalog ?? SoundGeneratorCatalog.CreateDefault();
        _serializer = serializer ?? new SoundGeneratorRequestSerializer();
    }

    public SoundGraphDefinition Resolve(string soundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundId);
        if (_cache.TryGetValue(soundId, out SoundGraphDefinition? cached)) return cached;
        string path = SoundAssetPath.GetSafeAssetPath(_rootDirectory, soundId, ".playgen");
        if (!File.Exists(path)) throw new FileNotFoundException($"Generator request '{soundId}' was not found in '{_rootDirectory}'.", path);
        SoundGraphDefinition definition = _serializer.Generate(File.ReadAllText(path), _catalog);
        if (!StringComparer.Ordinal.Equals(definition.Id, soundId))
            throw new InvalidDataException($"Generator request '{path}' produced ID '{definition.Id}', but '{soundId}' was requested.");
        _cache.Add(soundId, definition);
        return definition;
    }
}

/// <summary>Tries a fixed resolver chain in order.</summary>
public sealed class CompositeSoundAssetResolver : ISoundAssetResolver
{
    private readonly ISoundAssetResolver[] _resolvers;

    public CompositeSoundAssetResolver(params ISoundAssetResolver[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        if (resolvers.Length == 0) throw new ArgumentException("At least one resolver is required.", nameof(resolvers));
        if (resolvers.Any(item => item is null)) throw new ArgumentException("Resolver entries cannot be null.", nameof(resolvers));
        _resolvers = [.. resolvers];
    }

    public SoundGraphDefinition Resolve(string soundId)
    {
        List<Exception>? failures = null;
        foreach (ISoundAssetResolver resolver in _resolvers)
        {
            try
            {
                return resolver.Resolve(soundId);
            }
            catch (FileNotFoundException exception)
            {
                (failures ??= []).Add(exception);
            }
            catch (KeyNotFoundException exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        throw new AggregateException($"No sound asset resolver could resolve '{soundId}'.", failures ?? []);
    }
}

internal static class SoundAssetPath
{
    public static string GetSafeAssetPath(string rootDirectory, string soundId, string extension)
    {
        if (soundId.Contains(Path.DirectorySeparatorChar) || soundId.Contains(Path.AltDirectorySeparatorChar) || soundId is "." or "..")
            throw new ArgumentException("Sound IDs cannot contain path separators.", nameof(soundId));
        string root = Path.GetFullPath(rootDirectory);
        string path = Path.GetFullPath(Path.Combine(root, soundId + extension));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved sound asset path escaped its root directory.");
        return path;
    }
}

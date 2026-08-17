using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Streaming.Assets;

namespace Playroma.Sound.Authoring.Projects;

public sealed class ProjectSoundAssetResolver : ISoundAssetResolver
{
    private readonly AuthoringProjectDefinition _project;
    private readonly string _rootDirectory;
    private readonly SoundGraphSerializer _graphSerializer;
    private readonly EditableGeneratorRequestSerializer _generatorSerializer;
    private readonly SoundGeneratorCatalog _generatorCatalog;
    private readonly Dictionary<string, SoundGraphDefinition> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ProjectSoundAssetResolver(
        AuthoringProjectDefinition project,
        string rootDirectory,
        SoundGeneratorCatalog? generatorCatalog = null,
        SoundGraphSerializer? graphSerializer = null,
        EditableGeneratorRequestSerializer? generatorSerializer = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _generatorCatalog = generatorCatalog ?? SoundGeneratorCatalog.CreateDefault();
        _graphSerializer = graphSerializer ?? new SoundGraphSerializer();
        _generatorSerializer = generatorSerializer ?? new EditableGeneratorRequestSerializer();
    }

    public SoundGraphDefinition Resolve(string soundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundId);
        lock (_gate)
        {
            if (_cache.TryGetValue(soundId, out SoundGraphDefinition? cached)) return Clone(cached);
            ProjectAssetEntry asset = _project.Assets.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, soundId))
                ?? throw new KeyNotFoundException($"Project asset '{soundId}' was not found.");
            string path = ProjectPath.Resolve(_rootDirectory, asset.RelativePath);
            SoundGraphDefinition graph = asset.Kind switch
            {
                AuthoringAssetKind.SoundGraph => _graphSerializer.LoadFile(path),
                AuthoringAssetKind.GeneratorRequest => _generatorSerializer.Generate(_generatorSerializer.Deserialize(File.ReadAllText(path)), _generatorCatalog),
                _ => throw new InvalidDataException($"Asset '{soundId}' is not a sound graph or generator request.")
            };
            if (!StringComparer.Ordinal.Equals(graph.Id, soundId)) throw new InvalidDataException($"Asset '{asset.RelativePath}' declares or generates ID '{graph.Id}', but project ID '{soundId}' was requested.");
            _cache[soundId] = graph;
            return Clone(graph);
        }
    }

    public void Invalidate(string? soundId = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(soundId)) _cache.Clear();
            else _cache.Remove(soundId);
        }
    }

    private SoundGraphDefinition Clone(SoundGraphDefinition graph) => _graphSerializer.Load(_graphSerializer.Serialize(graph));
}

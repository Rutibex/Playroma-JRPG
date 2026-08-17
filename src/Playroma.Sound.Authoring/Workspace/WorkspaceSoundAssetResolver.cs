using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Streaming.Assets;

namespace Playroma.Sound.Authoring.Workspace;

/// <summary>
/// Resolves the current in-memory version of open .playsound and .playgen documents,
/// then falls back to the project files on disk. Compilation therefore previews the
/// editor's current state without requiring a save.
/// </summary>
public sealed class WorkspaceSoundAssetResolver : ISoundAssetResolver
{
    private readonly Func<IReadOnlyCollection<IAuthoringDocument>> _openDocuments;
    private readonly ProjectSoundAssetResolver _diskResolver;
    private readonly SoundGraphSerializer _graphSerializer = new();
    private readonly EditableGeneratorRequestSerializer _generatorSerializer = new();
    private readonly SoundGeneratorCatalog _generatorCatalog;

    public WorkspaceSoundAssetResolver(
        Func<IReadOnlyCollection<IAuthoringDocument>> openDocuments,
        ProjectSoundAssetResolver diskResolver,
        SoundGeneratorCatalog? generatorCatalog = null)
    {
        _openDocuments = openDocuments ?? throw new ArgumentNullException(nameof(openDocuments));
        _diskResolver = diskResolver ?? throw new ArgumentNullException(nameof(diskResolver));
        _generatorCatalog = generatorCatalog ?? SoundGeneratorCatalog.CreateDefault();
    }

    public SoundGraphDefinition Resolve(string soundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundId);
        IAuthoringDocument? open = _openDocuments().FirstOrDefault(document =>
            StringComparer.Ordinal.Equals(document.DocumentId, soundId) &&
            document.Kind is AuthoringDocumentKind.SoundGraph or AuthoringDocumentKind.GeneratorRequest);

        return open switch
        {
            SoundGraphDocument graph => _graphSerializer.Load(graph.SerializeContent()),
            GeneratorRequestDocument generator => _generatorSerializer.Generate(
                _generatorSerializer.Deserialize(generator.SerializeContent()),
                _generatorCatalog),
            _ => _diskResolver.Resolve(soundId)
        };
    }
}

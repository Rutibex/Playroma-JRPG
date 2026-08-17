using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Export;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Streaming.Assets;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.Authoring.Workspace;

public sealed class AuthoringWorkspace : IDisposable
{
    private readonly AuthoringProjectSerializer _projectSerializer;
    private readonly Dictionary<string, IAuthoringDocument> _documents = new(StringComparer.Ordinal);
    private string _savedProjectContent;
    private bool _disposed;

    private AuthoringWorkspace(string projectFilePath, AuthoringProjectDefinition project, AuthoringProjectSerializer serializer)
    {
        ProjectFilePath = Path.GetFullPath(projectFilePath);
        ProjectRoot = Path.GetDirectoryName(ProjectFilePath) ?? throw new InvalidOperationException("Project file has no directory.");
        Project = project;
        _projectSerializer = serializer;
        _savedProjectContent = serializer.Serialize(project);
        DiskSoundAssets = new ProjectSoundAssetResolver(Project, ProjectRoot);
        SoundAssets = new WorkspaceSoundAssetResolver(() => _documents.Values.ToArray(), DiskSoundAssets);
    }

    public string ProjectFilePath { get; private set; }
    public string ProjectRoot { get; private set; }
    public AuthoringProjectDefinition Project { get; }
    public AuthoringProjectMetadata Metadata => new(Project.Id, Project.DisplayName, ProjectFilePath, ProjectRoot, Project.Assets.Count, Project.FormatVersion);
    public IReadOnlyCollection<IAuthoringDocument> OpenDocuments => _documents.Values.ToArray();
    public bool IsProjectDirty => !StringComparer.Ordinal.Equals(_savedProjectContent, _projectSerializer.Serialize(Project));
    public bool HasUnsavedChanges => IsProjectDirty || _documents.Values.Any(document => document.IsDirty);
    public ProjectSoundAssetResolver DiskSoundAssets { get; private set; }
    public ISoundAssetResolver SoundAssets { get; private set; }

    public static AuthoringWorkspace Create(string projectFilePath, string projectId, string displayName)
    {
        var serializer = new AuthoringProjectSerializer();
        var project = new AuthoringProjectDefinition { Id = projectId, DisplayName = displayName };
        serializer.Save(projectFilePath, project);
        return new AuthoringWorkspace(projectFilePath, project, serializer);
    }

    public static AuthoringWorkspace Open(string projectFilePath)
    {
        var serializer = new AuthoringProjectSerializer();
        return new AuthoringWorkspace(projectFilePath, serializer.Load(projectFilePath), serializer);
    }

    public ValidationSummary ValidateProject() => _projectSerializer.Validate(Project, ProjectFilePath);

    public ValidationSummary ValidateWorkspace()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidationSummary summary = ValidateProject();
        var dependencyTracker = new Playroma.Sound.Authoring.Services.ProjectDependencyTracker();
        foreach (ProjectAssetEntry asset in Project.Assets.Where(IsEditableAsset).ToArray())
        {
            bool wasOpen = _documents.ContainsKey(asset.Id);
            try
            {
                IAuthoringDocument document = OpenDocument(asset.Id);
                summary = summary.Merge(document.Validate());
                IReadOnlyList<Playroma.Sound.Authoring.Services.DocumentDependency> dependencies = dependencyTracker.Analyze(Project, ProjectRoot, document);
                summary = summary.Merge(new ValidationSummary(dependencies.Where(item => !item.Exists).Select(item => new AuthoringDiagnostic(
                    "DEPENDENCY001",
                    AuthoringDiagnosticSeverity.Error,
                    $"Referenced asset '{item.AssetId}' is missing.",
                    document.DocumentId,
                    item.AssetId,
                    SourcePath: item.RelativePath))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or KeyNotFoundException or ArgumentException or InvalidOperationException)
            {
                summary = summary.Merge(new ValidationSummary([new AuthoringDiagnostic(
                    "DOCUMENT_LOAD",
                    AuthoringDiagnosticSeverity.Error,
                    exception.Message,
                    asset.Id,
                    SourcePath: asset.RelativePath)]));
            }
            finally
            {
                if (!wasOpen) CloseDocument(asset.Id, true);
            }
        }
        var exportSerializer = new AuthoringExportProfileSerializer();
        foreach (string relativePath in Project.ExportProfiles.ToArray())
        {
            try
            {
                AuthoringExportProfile profile = exportSerializer.Load(ProjectPath.Resolve(ProjectRoot, relativePath));
                summary = summary.Merge(exportSerializer.Validate(profile, Project));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                summary = summary.Merge(new ValidationSummary([new AuthoringDiagnostic(
                    "EXPORT_LOAD",
                    AuthoringDiagnosticSeverity.Error,
                    exception.Message,
                    Project.Id,
                    SourcePath: relativePath)]));
            }
        }
        return summary;
    }

    public AuthoringCompilationContext CreateCompilationContext(AudioFormat format) => new(format, SoundAssets);

    public AuthoringExportProfile LoadExportProfile(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AuthoringExportProfileSerializer().Load(ProjectPath.Resolve(ProjectRoot, relativePath));
    }

    public AuthoringExportPlan CreateExportPlan(AuthoringExportProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AuthoringExportPlanner().CreatePlan(Project, ProjectRoot, profile, OpenDocuments);
    }

    public void MutateProject(Action<AuthoringProjectDefinition> mutation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mutation);
        mutation(Project);
        DiskSoundAssets.Invalidate();
    }

    public IAuthoringDocument OpenDocument(string assetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (_documents.TryGetValue(assetId, out IAuthoringDocument? existing)) return existing;
        ProjectAssetEntry asset = GetRequiredAsset(assetId);
        string path = ProjectPath.Resolve(ProjectRoot, asset.RelativePath);
        IAuthoringDocument document = asset.Kind switch
        {
            AuthoringAssetKind.SoundGraph => SoundGraphDocument.Load(path),
            AuthoringAssetKind.GeneratorRequest => GeneratorRequestDocument.Load(path),
            AuthoringAssetKind.StreamingScene => StreamingSceneDocument.Load(path),
            _ => throw new NotSupportedException($"Asset kind {asset.Kind} is not an editable Playroma document.")
        };
        if (!StringComparer.Ordinal.Equals(document.DocumentId, asset.Id)) throw new InvalidDataException($"Asset '{asset.RelativePath}' declares ID '{document.DocumentId}', but project entry ID is '{asset.Id}'.");
        document.Changed += OnDocumentChanged;
        _documents.Add(assetId, document);
        return document;
    }

    public SoundGraphDocument CreateGraph(string id, string displayName, string relativePath)
    {
        var model = new SoundGraphDefinition { Id = id, DisplayName = displayName };
        return AddNewDocument(new ProjectAssetEntry { Id = id, DisplayName = displayName, Kind = AuthoringAssetKind.SoundGraph, RelativePath = relativePath }, new SoundGraphDocument(ProjectPath.Resolve(ProjectRoot, relativePath), model));
    }

    public GeneratorRequestDocument CreateGeneratorRequest(string id, string displayName, string generatorId, string relativePath)
    {
        var model = new EditableGeneratorRequest { Id = id, DisplayName = displayName, GeneratorId = generatorId };
        return AddNewDocument(new ProjectAssetEntry { Id = id, DisplayName = displayName, Kind = AuthoringAssetKind.GeneratorRequest, RelativePath = relativePath }, new GeneratorRequestDocument(ProjectPath.Resolve(ProjectRoot, relativePath), model));
    }

    public StreamingSceneDocument CreateScene(string id, string displayName, string relativePath)
    {
        var model = new SoundSceneDefinition { Id = id, DisplayName = displayName };
        return AddNewDocument(new ProjectAssetEntry { Id = id, DisplayName = displayName, Kind = AuthoringAssetKind.StreamingScene, RelativePath = relativePath }, new StreamingSceneDocument(ProjectPath.Resolve(ProjectRoot, relativePath), model));
    }

    public ProjectAssetEntry AddExistingAsset(string id, string displayName, AuthoringAssetKind kind, string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Project.Assets.Any(item => StringComparer.Ordinal.Equals(item.Id, id))) throw new InvalidOperationException($"Project already contains asset ID '{id}'.");
        string normalized = ProjectPath.NormalizeRelative(relativePath);
        string path = ProjectPath.Resolve(ProjectRoot, normalized);
        if (!File.Exists(path)) throw new FileNotFoundException("The asset file does not exist.", path);
        var asset = new ProjectAssetEntry { Id = id, DisplayName = displayName, Kind = kind, RelativePath = normalized };
        Project.Assets.Add(asset);
        DiskSoundAssets.Invalidate(id);
        return asset;
    }

    public bool CloseDocument(string assetId, bool discardUnsavedChanges = false)
    {
        if (!_documents.TryGetValue(assetId, out IAuthoringDocument? document)) return false;
        if (document.IsDirty && !discardUnsavedChanges) return false;
        document.Changed -= OnDocumentChanged;
        _documents.Remove(assetId);
        return true;
    }

    public void RenameAsset(string oldId, string newId, bool updateSceneReferences = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);
        if (StringComparer.Ordinal.Equals(oldId, newId)) return;
        if (Project.Assets.Any(item => StringComparer.Ordinal.Equals(item.Id, newId))) throw new InvalidOperationException($"Project already contains asset ID '{newId}'.");

        bool wasOpen = _documents.ContainsKey(oldId);
        IAuthoringDocument document = OpenDocument(oldId);
        switch (document)
        {
            case SoundGraphDocument graph:
                graph.Mutate("Rename asset", model => model.Id = newId);
                break;
            case GeneratorRequestDocument generator:
                generator.Mutate("Rename asset", model => model.Id = newId);
                break;
            case StreamingSceneDocument scene:
                scene.Mutate("Rename asset", model => model.Id = newId);
                break;
            default:
                throw new NotSupportedException($"Asset kind {document.Kind} cannot be renamed.");
        }
        document.Save();

        if (updateSceneReferences && document.Kind is AuthoringDocumentKind.SoundGraph or AuthoringDocumentKind.GeneratorRequest)
        {
            foreach (ProjectAssetEntry sceneAsset in Project.Assets.Where(item => item.Kind == AuthoringAssetKind.StreamingScene).ToArray())
            {
                bool sceneWasOpen = _documents.ContainsKey(sceneAsset.Id);
                var sceneDocument = (StreamingSceneDocument)OpenDocument(sceneAsset.Id);
                sceneDocument.Mutate("Update renamed sound references", model => ReplaceSoundReferences(model, oldId, newId));
                if (sceneDocument.IsDirty) sceneDocument.Save();
                if (!sceneWasOpen) CloseDocument(sceneAsset.Id, true);
            }
        }

        if (!wasOpen) CloseDocument(newId, true);
        SaveProject();
    }

    public void SaveProject()
    {
        _projectSerializer.Save(ProjectFilePath, Project);
        _savedProjectContent = _projectSerializer.Serialize(Project);
    }

    public void SaveAll()
    {
        foreach (IAuthoringDocument document in _documents.Values.Where(item => item.IsDirty).ToArray()) document.Save();
        SaveProject();
    }

    public bool RemoveAsset(string assetId, bool deleteFile = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (_documents.TryGetValue(assetId, out IAuthoringDocument? document) && document.IsDirty) throw new InvalidOperationException("Cannot remove an asset with unsaved changes.");
        ProjectAssetEntry? asset = Project.Assets.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, assetId));
        if (asset is null) return false;
        CloseDocument(assetId, true);
        Project.Assets.Remove(asset);
        DiskSoundAssets.Invalidate(assetId);
        if (deleteFile)
        {
            string path = ProjectPath.Resolve(ProjectRoot, asset.RelativePath);
            if (File.Exists(path)) File.Delete(path);
        }
        return true;
    }

    public void SaveProjectAs(string newProjectFilePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(newProjectFilePath);
        string fullPath = Path.GetFullPath(newProjectFilePath);
        string newRoot = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Project file has no directory.");
        string oldRoot = ProjectRoot;

        foreach (ProjectAssetEntry asset in Project.Assets)
        {
            string destination = ProjectPath.Resolve(newRoot, asset.RelativePath);
            if (_documents.TryGetValue(asset.Id, out IAuthoringDocument? openDocument))
            {
                openDocument.SaveAs(destination);
                continue;
            }

            string source = ProjectPath.Resolve(oldRoot, asset.RelativePath);
            if (!File.Exists(source) || PathsEqual(source, destination)) continue;
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Copy(source, destination, true);
        }

        _projectSerializer.Save(fullPath, Project);
        ProjectFilePath = fullPath;
        ProjectRoot = newRoot;
        _savedProjectContent = _projectSerializer.Serialize(Project);
        DiskSoundAssets = new ProjectSoundAssetResolver(Project, ProjectRoot, SoundGeneratorCatalog.CreateDefault());
        SoundAssets = new WorkspaceSoundAssetResolver(() => _documents.Values.ToArray(), DiskSoundAssets);
    }

    public ProjectAssetEntry GetRequiredAsset(string id) => Project.Assets.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, id)) ?? throw new KeyNotFoundException($"Project asset '{id}' was not found.");

    private TDocument AddNewDocument<TDocument>(ProjectAssetEntry asset, TDocument document) where TDocument : IAuthoringDocument
    {
        if (Project.Assets.Any(item => StringComparer.Ordinal.Equals(item.Id, asset.Id))) throw new InvalidOperationException($"Project already contains asset ID '{asset.Id}'.");
        asset.RelativePath = ProjectPath.NormalizeRelative(asset.RelativePath);
        Project.Assets.Add(asset);
        document.Changed += OnDocumentChanged;
        _documents.Add(asset.Id, document);
        document.Save();
        DiskSoundAssets.Invalidate(asset.Id);
        SaveProject();
        return document;
    }

    private static bool IsEditableAsset(ProjectAssetEntry asset) => asset.Kind is AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene;

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void ReplaceSoundReferences(SoundSceneDefinition scene, string oldId, string newId)
    {
        foreach (SoundReferenceDefinition sound in scene.Sounds)
            if (StringComparer.Ordinal.Equals(sound.SoundId, oldId)) sound.SoundId = newId;
        foreach (SoundLayerDefinition layer in scene.Layers)
            if (StringComparer.Ordinal.Equals(layer.SoundId, oldId)) layer.SoundId = newId;
        foreach (SoundEventEmitterDefinition emitter in scene.Emitters)
            for (int i = 0; i < emitter.SoundIds.Count; i++)
                if (StringComparer.Ordinal.Equals(emitter.SoundIds[i], oldId)) emitter.SoundIds[i] = newId;
        foreach (SoundEventClusterDefinition cluster in scene.EventClusters)
            foreach (SoundClusterEventDefinition item in cluster.Events)
                if (StringComparer.Ordinal.Equals(item.SoundId, oldId)) item.SoundId = newId;
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (sender is not IAuthoringDocument document) return;
        KeyValuePair<string, IAuthoringDocument> entry = _documents.FirstOrDefault(item => ReferenceEquals(item.Value, document));
        if (string.IsNullOrEmpty(entry.Key)) return;
        string oldId = entry.Key;
        string newId = document.DocumentId;
        ProjectAssetEntry asset = GetRequiredAsset(oldId);
        if (!StringComparer.Ordinal.Equals(oldId, newId))
        {
            if (string.IsNullOrWhiteSpace(newId)) return;
            if (_documents.ContainsKey(newId) || Project.Assets.Any(item => !ReferenceEquals(item, asset) && StringComparer.Ordinal.Equals(item.Id, newId)))
                throw new InvalidOperationException($"Cannot rename asset '{oldId}' to duplicate ID '{newId}'.");
            _documents.Remove(oldId);
            _documents.Add(newId, document);
            asset.Id = newId;
            DiskSoundAssets.Invalidate(oldId);
        }
        asset.DisplayName = document.DisplayName;
        DiskSoundAssets.Invalidate(newId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (IAuthoringDocument document in _documents.Values) document.Changed -= OnDocumentChanged;
        _documents.Clear();
    }
}

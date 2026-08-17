using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.Authoring.Export;

public enum AuthoringExportMode
{
    RenderedAudio,
    RuntimePackage
}


internal static class AuthoringExportAssetKinds
{
    public static bool IsRenderable(AuthoringAssetKind kind) => kind is
        AuthoringAssetKind.SoundGraph or
        AuthoringAssetKind.GeneratorRequest or
        AuthoringAssetKind.StreamingScene;
}

public enum AuthoringWaveEncoding
{
    Pcm16,
    Pcm24,
    Pcm32,
    Float32
}

public sealed class AuthoringExportProfile
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Id { get; set; } = "default.export";
    public string DisplayName { get; set; } = "Default Export";
    public AuthoringExportMode Mode { get; set; } = AuthoringExportMode.RenderedAudio;
    public string OutputDirectory { get; set; } = "Exports/Rendered";
    public List<string> AssetIds { get; set; } = [];
    public int SampleRate { get; set; } = 48_000;
    public int Channels { get; set; } = 2;
    public int BlockSize { get; set; } = 256;
    public AuthoringWaveEncoding WaveEncoding { get; set; } = AuthoringWaveEncoding.Pcm16;
    public double SceneDurationSeconds { get; set; } = 60;
    public int VariationCount { get; set; } = 1;
    public bool RenderStems { get; set; }
    public bool IncludeSourceDefinitions { get; set; } = true;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AuthoringExportProfileSerializer
{
    private readonly JsonSerializerOptions _options;

    public AuthoringExportProfileSerializer(bool indented = true)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public string Serialize(AuthoringExportProfile profile) => JsonSerializer.Serialize(profile ?? throw new ArgumentNullException(nameof(profile)), _options);

    public AuthoringExportProfile Deserialize(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        AuthoringExportProfile profile = JsonSerializer.Deserialize<AuthoringExportProfile>(content, _options)
            ?? throw new InvalidDataException("The .playexport document contained no export profile.");
        profile.Id ??= "default.export";
        profile.DisplayName ??= "Default Export";
        profile.OutputDirectory ??= "Exports/Rendered";
        profile.AssetIds ??= [];
        profile.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        return profile;
    }

    public AuthoringExportProfile Load(string path) => Deserialize(File.ReadAllText(path));

    public void Save(string path, AuthoringExportProfile profile) => AtomicTextFile.Write(path, Serialize(profile));

    public ValidationSummary Validate(AuthoringExportProfile profile, AuthoringProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new List<AuthoringDiagnostic>();
        if (profile.FormatVersion != AuthoringExportProfile.CurrentFormatVersion) Error("EXPORT001", $"Export format {profile.FormatVersion} is not supported.");
        if (string.IsNullOrWhiteSpace(profile.Id)) Error("EXPORT002", "Export profile ID is required.");
        if (string.IsNullOrWhiteSpace(profile.DisplayName)) Error("EXPORT003", "Export profile display name is required.");
        try { ProjectPath.NormalizeRelative(profile.OutputDirectory); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { Error("EXPORT004", exception.Message, "outputDirectory"); }
        if (profile.SampleRate is < 8_000 or > 384_000) Error("EXPORT005", "Sample rate must be between 8,000 and 384,000 Hz.", "sampleRate");
        if (profile.Channels is < 1 or > 8) Error("EXPORT006", "Channel count must be between 1 and 8.", "channels");
        if (profile.BlockSize is < 16 or > 16_384) Error("EXPORT007", "Block size must be between 16 and 16,384 frames.", "blockSize");
        if (profile.SceneDurationSeconds <= 0 || !double.IsFinite(profile.SceneDurationSeconds)) Error("EXPORT008", "Scene duration must be finite and greater than zero.", "sceneDurationSeconds");
        if (profile.VariationCount is < 1 or > 10_000) Error("EXPORT009", "Variation count must be between 1 and 10,000.", "variationCount");

        var projectIds = project.Assets.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string assetId in (profile.AssetIds ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
        {
            if (!projectIds.Contains(assetId))
            {
                Error("EXPORT010", $"Export asset '{assetId}' is not in the project.", "assetIds", assetId);
                continue;
            }

            ProjectAssetEntry selectedAsset = project.Assets.First(item => StringComparer.Ordinal.Equals(item.Id, assetId));
            if (profile.Mode == AuthoringExportMode.RenderedAudio && !AuthoringExportAssetKinds.IsRenderable(selectedAsset.Kind))
                Error("EXPORT011", $"Asset '{assetId}' is not a procedural sound, generator request, or streaming scene and cannot be rendered to audio.", "assetIds", assetId);
        }
        return new ValidationSummary(diagnostics);

        void Error(string code, string message, string propertyPath = "", string objectId = "") => diagnostics.Add(new AuthoringDiagnostic(code, AuthoringDiagnosticSeverity.Error, message, profile.Id, objectId, propertyPath));
    }
}

public sealed record AuthoringExportItem(
    string AssetId,
    AuthoringAssetKind Kind,
    string SourceRelativePath,
    string OutputRelativePath,
    IReadOnlyList<string> Dependencies);

public sealed record AuthoringExportManifest(
    int FormatVersion,
    string ProjectId,
    string ProfileId,
    DateTimeOffset CreatedAtUtc,
    AuthoringExportMode Mode,
    int SampleRate,
    int Channels,
    int BlockSize,
    AuthoringWaveEncoding WaveEncoding,
    double SceneDurationSeconds,
    int VariationCount,
    bool RenderStems,
    bool IncludeSourceDefinitions,
    IReadOnlyList<AuthoringExportItem> Items);

public sealed record AuthoringExportPlan(
    AuthoringExportProfile Profile,
    string OutputRoot,
    AuthoringExportManifest Manifest,
    ValidationSummary Validation)
{
    public bool CanExport => !Validation.HasErrors;
}

public sealed class AuthoringExportPlanner
{
    private readonly AuthoringExportProfileSerializer _serializer = new();

    public AuthoringExportPlan CreatePlan(
        AuthoringProjectDefinition project,
        string projectRoot,
        AuthoringExportProfile profile,
        IReadOnlyCollection<IAuthoringDocument>? openDocuments = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(profile);
        ValidationSummary validation = _serializer.Validate(profile, project);
        string normalizedOutput = "Exports/Invalid";
        try { normalizedOutput = ProjectPath.NormalizeRelative(profile.OutputDirectory); }
        catch (Exception) when (validation.HasErrors) { }
        string outputRoot = ProjectPath.Resolve(projectRoot, normalizedOutput);
        var open = (openDocuments ?? []).ToDictionary(item => item.DocumentId, StringComparer.Ordinal);
        IReadOnlyList<string> requestedAssetIds = profile.AssetIds ?? [];
        IReadOnlyList<ProjectAssetEntry> selected = requestedAssetIds.Count == 0
            ? project.Assets.Where(item => profile.Mode == AuthoringExportMode.RuntimePackage || AuthoringExportAssetKinds.IsRenderable(item.Kind)).ToArray()
            : requestedAssetIds.Distinct(StringComparer.Ordinal).Select(id => project.Assets.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, id))).Where(item => item is not null).Cast<ProjectAssetEntry>().ToArray();
        var projectAssets = project.Assets.GroupBy(item => item.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var diagnostics = validation.Diagnostics.ToList();
        var items = new List<AuthoringExportItem>();
        var queued = new HashSet<string>(selected.Select(item => item.Id), StringComparer.Ordinal);
        var queue = new Queue<ProjectAssetEntry>(selected);

        while (queue.Count > 0)
        {
            ProjectAssetEntry asset = queue.Dequeue();
            string sourcePath;
            try { sourcePath = ProjectPath.Resolve(projectRoot, asset.RelativePath); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new AuthoringDiagnostic("EXPORT_PATH", AuthoringDiagnosticSeverity.Error, exception.Message, profile.Id, asset.Id, SourcePath: asset.RelativePath));
                continue;
            }
            bool sourceAvailable = File.Exists(sourcePath) || open.ContainsKey(asset.Id);
            if (!sourceAvailable)
                diagnostics.Add(new AuthoringDiagnostic("EXPORT_SOURCE", AuthoringDiagnosticSeverity.Error, $"Export source '{asset.RelativePath}' does not exist.", profile.Id, asset.Id, SourcePath: asset.RelativePath));

            string[] dependencies = sourceAvailable
                ? ReadDependencies(asset, sourcePath, open).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray()
                : [];
            foreach (string dependencyId in dependencies)
            {
                if (!projectAssets.TryGetValue(dependencyId, out ProjectAssetEntry? dependency))
                {
                    diagnostics.Add(new AuthoringDiagnostic("EXPORT_DEPENDENCY", AuthoringDiagnosticSeverity.Error, $"Export dependency '{dependencyId}' is not in the project.", profile.Id, asset.Id));
                    continue;
                }
                string dependencyPath;
                try { dependencyPath = ProjectPath.Resolve(projectRoot, dependency.RelativePath); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    diagnostics.Add(new AuthoringDiagnostic("EXPORT_DEPENDENCY_PATH", AuthoringDiagnosticSeverity.Error, exception.Message, profile.Id, dependencyId, SourcePath: dependency.RelativePath));
                    continue;
                }
                if (!File.Exists(dependencyPath) && !open.ContainsKey(dependencyId))
                    diagnostics.Add(new AuthoringDiagnostic("EXPORT_DEPENDENCY_SOURCE", AuthoringDiagnosticSeverity.Error, $"Export dependency source '{dependency.RelativePath}' does not exist.", profile.Id, dependencyId, SourcePath: dependency.RelativePath));
                if (profile.Mode == AuthoringExportMode.RuntimePackage && queued.Add(dependencyId)) queue.Enqueue(dependency);
            }

            try
            {
                string outputPath = BuildOutputPath(asset, profile.Mode);
                items.Add(new AuthoringExportItem(asset.Id, asset.Kind, asset.RelativePath, outputPath, dependencies));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new AuthoringDiagnostic("EXPORT_OUTPUT", AuthoringDiagnosticSeverity.Error, exception.Message, profile.Id, asset.Id));
            }
        }

        var manifest = new AuthoringExportManifest(
            1,
            project.Id,
            profile.Id,
            DateTimeOffset.UtcNow,
            profile.Mode,
            profile.SampleRate,
            profile.Channels,
            profile.BlockSize,
            profile.WaveEncoding,
            profile.SceneDurationSeconds,
            profile.VariationCount,
            profile.RenderStems,
            profile.IncludeSourceDefinitions,
            items);
        return new AuthoringExportPlan(profile, outputRoot, manifest, new ValidationSummary(diagnostics));
    }

    public string SerializeManifest(AuthoringExportManifest manifest, bool indented = true)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = indented };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(manifest, options);
    }

    private static IEnumerable<string> ReadDependencies(ProjectAssetEntry asset, string sourcePath, IReadOnlyDictionary<string, IAuthoringDocument> openDocuments)
    {
        if (asset.Kind != AuthoringAssetKind.StreamingScene) return [];
        SoundSceneDefinition scene = openDocuments.TryGetValue(asset.Id, out IAuthoringDocument? open) && open is StreamingSceneDocument sceneDocument
            ? sceneDocument.CreateEditableCopy()
            : new Playroma.Sound.Streaming.Serialization.SoundSceneSerializer().Load(sourcePath);
        return scene.Sounds.Select(item => item.SoundId)
            .Concat(scene.Layers.Select(item => item.SoundId))
            .Concat(scene.Emitters.SelectMany(item => item.SoundIds))
            .Concat(scene.EventClusters.SelectMany(cluster => cluster.Events.Select(item => item.SoundId)))
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static string BuildOutputPath(ProjectAssetEntry asset, AuthoringExportMode mode)
    {
        if (mode == AuthoringExportMode.RuntimePackage) return ProjectPath.NormalizeRelative(asset.RelativePath);
        string safeId = new(asset.Id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray());
        string extension = asset.Kind switch
        {
            AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene => ".wav",
            _ => Path.GetExtension(asset.RelativePath)
        };
        if (string.IsNullOrEmpty(extension)) extension = ".bin";
        return ProjectPath.NormalizeRelative("asset-" + safeId + extension);
    }
}

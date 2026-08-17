using System.Text.RegularExpressions;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Runtime.MV;
using Playroma.JRPG.Specifications;

namespace Playroma.JRPG.RpgMakerMV;

public interface IMvProjectLoader
{
    Task<IReadOnlyList<MvProjectCandidate>> ProbeAsync(
        string selectedPath,
        ProjectSearchOptions? searchOptions = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<MvProjectSnapshot>> LoadAsync(
        string selectedPath,
        ProjectLoadOptions? options = null,
        IProgress<ProjectLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<MvProjectSnapshot>> LoadCandidateAsync(
        MvProjectCandidate candidate,
        ProjectLoadOptions? options = null,
        IProgress<ProjectLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed partial class MvProjectLoader : IMvProjectLoader
{
    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;
    private readonly IProjectLocator _locator;
    private readonly MvSpecificationCatalog _specifications;

    public MvProjectLoader()
        : this(new PhysicalReadOnlyProjectFileSystem(), null, null)
    {
    }

    public MvProjectLoader(
        IReadOnlyProjectFileSystem fileSystem,
        IProjectLocator? locator = null,
        MvSpecificationCatalog? specifications = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fingerprints = new FileFingerprintService(_fileSystem);
        _locator = locator ?? new MvProjectLocator(_fileSystem);
        _specifications = specifications ?? MvSpecificationCatalog.Load();
    }

    public Task<IReadOnlyList<MvProjectCandidate>> ProbeAsync(
        string selectedPath,
        ProjectSearchOptions? searchOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        return _locator.FindCandidatesAsync(
            selectedPath,
            searchOptions ?? new ProjectSearchOptions(),
            cancellationToken);
    }

    public async Task<OperationResult<MvProjectSnapshot>> LoadAsync(
        string selectedPath,
        ProjectLoadOptions? options = null,
        IProgress<ProjectLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        ProjectLoadOptions effectiveOptions = options ?? new ProjectLoadOptions();
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.Locating, 0, 1, selectedPath));

        IReadOnlyList<MvProjectCandidate> candidates;
        try
        {
            candidates = await ProbeAsync(selectedPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException exception)
        {
            return OperationResult<MvProjectSnapshot>.Failure(
            [
                ProjectDiagnostic.Fatal(
                    DiagnosticCodes.NotAnMvProject,
                    "The selected path does not exist or is not accessible.",
                    technicalDetail: exception.Message)
            ]);
        }
        catch (UnauthorizedAccessException exception)
        {
            return OperationResult<MvProjectSnapshot>.Failure(
            [
                ProjectDiagnostic.Fatal(
                    DiagnosticCodes.AccessDenied,
                    "Access to the selected path was denied.",
                    technicalDetail: exception.Message)
            ]);
        }
        catch (IOException exception)
        {
            return OperationResult<MvProjectSnapshot>.Failure(
            [
                ProjectDiagnostic.Fatal(
                    DiagnosticCodes.FileReadFailure,
                    "The selected path could not be inspected.",
                    technicalDetail: exception.Message)
            ]);
        }

        MvProjectCandidate[] supported = candidates
            .Where(candidate => candidate.LayoutKind != MvProjectLayoutKind.UnsupportedMzProject
                                && candidate.Editability != ProjectEditability.Invalid)
            .ToArray();
        if (supported.Length == 0)
        {
            List<ProjectDiagnostic> diagnostics = candidates.SelectMany(candidate => candidate.Diagnostics).ToList();
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(ProjectDiagnostic.Fatal(
                    DiagnosticCodes.NotAnMvProject,
                    "No supported RPG Maker MV project was found at or beneath the selected path."));
            }
            else if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Fatal))
            {
                diagnostics.Add(ProjectDiagnostic.Fatal(
                    DiagnosticCodes.NotAnMvProject,
                    "No supported RPG Maker MV project candidate could be loaded."));
            }

            return OperationResult<MvProjectSnapshot>.Failure(diagnostics);
        }

        MvProjectCandidate selected = supported
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProjectRoot, StringComparer.OrdinalIgnoreCase)
            .First();
        OperationResult<MvProjectSnapshot> result = await LoadCandidateAsync(
            selected,
            effectiveOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (supported.Length > 1 && result.Value is not null)
        {
            ProjectDiagnostic multiple = ProjectDiagnostic.Warning(
                DiagnosticCodes.MultipleProjectCandidates,
                $"{supported.Length} MV project candidates were found. The highest-confidence candidate was inspected: {selected.ProjectRoot}");
            return new OperationResult<MvProjectSnapshot>(
                result.Value with { Diagnostics = result.Value.Diagnostics.Concat([multiple]).ToArray() },
                result.Diagnostics.Concat([multiple]).ToArray());
        }

        return result;
    }

    public async Task<OperationResult<MvProjectSnapshot>> LoadCandidateAsync(
        MvProjectCandidate candidate,
        ProjectLoadOptions? options = null,
        IProgress<ProjectLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ProjectLoadOptions effectiveOptions = options ?? new ProjectLoadOptions();
        if (effectiveOptions.MaximumFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumFiles must be at least one.");
        }

        List<ProjectDiagnostic> diagnostics = candidate.Diagnostics.ToList();
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.Classifying, 1, 1, candidate.ProjectRoot));

        if (candidate.Editability == ProjectEditability.Invalid)
        {
            diagnostics.Add(ProjectDiagnostic.Fatal(
                DiagnosticCodes.NotAnMvProject,
                "The selected candidate is not a supported RPG Maker MV project."));
            return OperationResult<MvProjectSnapshot>.Failure(diagnostics);
        }

        ProjectPathPolicy rootPolicy = new(candidate.ProjectRoot);
        try
        {
            bool unsafeDataRoot = rootPolicy.ContainsReparsePoint(_fileSystem, candidate.DataRoot);
            bool unsafeRuntimeRoot = candidate.RuntimeRoot is not null
                                     && rootPolicy.ContainsReparsePoint(_fileSystem, candidate.RuntimeRoot);
            if (unsafeDataRoot || unsafeRuntimeRoot)
            {
                diagnostics.Add(ProjectDiagnostic.Fatal(
                    DiagnosticCodes.PathOutsideProjectRoot,
                    "The project data or runtime path contains a symbolic link, junction, or reparse point. Playroma refuses to follow it."));
                return OperationResult<MvProjectSnapshot>.Failure(diagnostics);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(ProjectDiagnostic.Fatal(
                exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                "The project path could not be checked for reparse points.",
                technicalDetail: exception.Message));
            return OperationResult<MvProjectSnapshot>.Failure(diagnostics);
        }

        string? markerVersion = await ReadMarkerVersionAsync(candidate.ProjectMarkerPath, cancellationToken).ConfigureAwait(false);
        if (effectiveOptions.Depth == ProjectLoadDepth.ProbeOnly)
        {
            MvProjectSnapshot probeSnapshot = new(
                new MvProjectIdentity(candidate.ProjectRoot, candidate.DataRoot, candidate.RuntimeRoot, null, markerVersion),
                new MvProjectLayout(candidate.LayoutKind, candidate.ProjectRoot, candidate.DataRoot, candidate.RuntimeRoot),
                candidate.Editability,
                new ProjectFileInventory([], 0),
                MvDatabaseInventory.Empty,
                [],
                MvPluginInventory.Empty,
                MvRuntimeSnapshot.Empty,
                AssetInventory.Empty,
                diagnostics);
            return OperationResult<MvProjectSnapshot>.Success(probeSnapshot, diagnostics);
        }

        ProjectFileInventoryBuilder inventoryBuilder = new(_fileSystem, _fingerprints);
        OperationResult<ProjectFileInventory> inventoryResult = await inventoryBuilder.BuildAsync(
            candidate.ProjectRoot,
            new ProjectFileInventoryOptions(
                effectiveOptions.MaximumFiles,
                effectiveOptions.CalculateStrongAssetHashes),
            progress,
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(inventoryResult.Diagnostics);
        if (inventoryResult.Value is null)
        {
            diagnostics.Add(ProjectDiagnostic.Fatal(
                DiagnosticCodes.FileReadFailure,
                "The project file inventory could not be created."));
            return OperationResult<MvProjectSnapshot>.Failure(diagnostics);
        }

        ProjectFileInventory inventory = inventoryResult.Value;
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.ReadingPlugins, 0, 1));
        MvPluginInventory plugins = await new MvPluginManifestParser(_fileSystem, _fingerprints)
            .InspectAsync(candidate.ProjectRoot, candidate.RuntimeRoot, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(plugins.Diagnostics);
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.ReadingPlugins, 1, 1, plugins.ManifestRelativePath));

        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.FingerprintingRuntime, 0, 1));
        MvRuntimeSnapshot runtime = await new MvRuntimeFingerprinter(_fileSystem, _fingerprints)
            .InspectAsync(
                candidate.ProjectRoot,
                candidate.RuntimeRoot,
                inventory,
                plugins.Entries.Where(entry => entry.ScriptRelativePath is not null).Select(entry => entry.ScriptRelativePath!),
                candidate.ProjectMarkerPath,
                cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(runtime.Diagnostics);
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.FingerprintingRuntime, 1, 1));

        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.InventoryingAssets, 0, 1));
        string runtimePrefix = candidate.RuntimeRoot is null
            ? string.Empty
            : Path.GetRelativePath(candidate.ProjectRoot, candidate.RuntimeRoot);
        OperationResult<AssetInventory> assetResult = new MvAssetScanner().Scan(inventory, runtimePrefix);
        diagnostics.AddRange(assetResult.Diagnostics);
        AssetInventory assets = assetResult.Value ?? AssetInventory.Empty;
        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.InventoryingAssets, 1, 1));

        MvDatabaseInventory database = MvDatabaseInventory.Empty;
        IReadOnlyList<MvMapDescriptor> maps = [];
        if (effectiveOptions.Depth >= ProjectLoadDepth.StandardData)
        {
            DatabaseInspectionResult databaseResult = await new MvDatabaseInspector(
                    _fileSystem,
                    _fingerprints,
                    _specifications)
                .InspectAsync(candidate, effectiveOptions, progress, cancellationToken)
                .ConfigureAwait(false);
            database = databaseResult.Database;
            maps = databaseResult.Maps;
            markerVersion ??= databaseResult.ProjectMarkerVersion;
            diagnostics.AddRange(databaseResult.Diagnostics);
        }

        ProjectEditability editability = candidate.Editability;
        if (assets.EncryptedAssetCount > 0
            && candidate.LayoutKind == MvProjectLayoutKind.ReadableDeployment
            && candidate.ProjectMarkerPath is null)
        {
            editability = ProjectEditability.UnsupportedEncryptedDeployment;
        }

        progress?.Report(new ProjectLoadProgress(ProjectLoadStage.Finalizing, 1, 1));
        MvProjectSnapshot snapshot = new(
            new MvProjectIdentity(
                candidate.ProjectRoot,
                candidate.DataRoot,
                candidate.RuntimeRoot,
                database.ProjectTitle,
                markerVersion),
            new MvProjectLayout(candidate.LayoutKind, candidate.ProjectRoot, candidate.DataRoot, candidate.RuntimeRoot),
            editability,
            inventory,
            database,
            maps,
            plugins,
            runtime,
            assets,
            diagnostics.ToArray());
        return OperationResult<MvProjectSnapshot>.Success(snapshot, diagnostics);
    }

    private async Task<string?> ReadMarkerVersionAsync(string? markerPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || !_fileSystem.FileExists(markerPath)) return null;
        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(markerPath, cancellationToken).ConfigureAwait(false);
            if (!Utf8TextDecoder.TryDecode(bytes, out string markerText)) return null;
            Match match = MarkerVersionRegex().Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"RPGMV\s+([0-9]+(?:\.[0-9]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerVersionRegex();
}

using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.RpgMakerMV;

public interface IProjectLocator
{
    Task<IReadOnlyList<MvProjectCandidate>> FindCandidatesAsync(
        string selectedPath,
        ProjectSearchOptions options,
        CancellationToken cancellationToken);
}

public sealed class MvProjectLocator : IProjectLocator
{
    private static readonly string[] StandardDatabaseFiles =
    [
        "Actors.json",
        "Animations.json",
        "Armors.json",
        "Classes.json",
        "CommonEvents.json",
        "Enemies.json",
        "Items.json",
        "MapInfos.json",
        "Skills.json",
        "States.json",
        "System.json",
        "Tilesets.json",
        "Troops.json",
        "Weapons.json"
    ];

    private readonly IReadOnlyProjectFileSystem _fileSystem;

    public MvProjectLocator(IReadOnlyProjectFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public Task<IReadOnlyList<MvProjectCandidate>> FindCandidatesAsync(
        string selectedPath,
        ProjectSearchOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(options), "Maximum depth cannot be negative.");
        if (options.MaximumDirectories < 1) throw new ArgumentOutOfRangeException(nameof(options), "At least one directory must be searchable.");

        string fullSelected = _fileSystem.GetFullPath(selectedPath);
        if (_fileSystem.FileExists(fullSelected))
        {
            fullSelected = Path.GetDirectoryName(fullSelected)
                ?? throw new DirectoryNotFoundException($"The selected file has no parent directory: {selectedPath}");
        }

        if (!_fileSystem.DirectoryExists(fullSelected))
        {
            throw new DirectoryNotFoundException($"The selected project path does not exist: {selectedPath}");
        }

        Dictionary<string, MvProjectCandidate> candidates = new(PathComparer);
        Queue<(string Directory, int Depth)> pending = new();
        pending.Enqueue((fullSelected, 0));
        int visited = 0;

        while (pending.Count > 0 && visited < options.MaximumDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string directory, int depth) = pending.Dequeue();
            visited++;

            foreach (MvProjectCandidate candidate in ProbeDirectory(fullSelected, directory))
            {
                string key = NormalizePath(candidate.DataRoot);
                if (!candidates.TryGetValue(key, out MvProjectCandidate? current)
                    || candidate.Score > current.Score)
                {
                    candidates[key] = candidate;
                }
            }

            if (!options.SearchChildren || depth >= options.MaximumDepth)
            {
                continue;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = _fileSystem.EnumerateDirectories(directory)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in childDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if ((_fileSystem.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    pending.Enqueue((child, depth + 1));
                }
                catch (IOException)
                {
                    // A candidate search is best effort. Loading a selected candidate reports concrete errors.
                }
                catch (UnauthorizedAccessException)
                {
                    // A candidate search is best effort. Loading a selected candidate reports concrete errors.
                }
            }
        }

        bool searchLimitReached = pending.Count > 0;
        IEnumerable<MvProjectCandidate> values = candidates.Values;
        if (searchLimitReached)
        {
            ProjectDiagnostic limitDiagnostic = ProjectDiagnostic.Warning(
                DiagnosticCodes.SearchLimitReached,
                $"Project discovery stopped after inspecting the configured limit of {options.MaximumDirectories} directories.");
            values = values.Select(candidate => candidate with
            {
                Diagnostics = candidate.Diagnostics.Concat([limitDiagnostic]).ToArray()
            });
        }

        IReadOnlyList<MvProjectCandidate> result = values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProjectRoot, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    private IEnumerable<MvProjectCandidate> ProbeDirectory(string selectedRoot, string directory)
    {
        string? mvMarker = FindMarker(directory, "*.rpgproject");
        string? mzMarker = FindMarker(directory, "*.rmmzproject");
        if (mzMarker is not null && mvMarker is null)
        {
            string mzDataRoot = Path.Combine(directory, "data");
            yield return new MvProjectCandidate(
                selectedRoot,
                directory,
                mzDataRoot,
                directory,
                mzMarker,
                MvProjectLayoutKind.UnsupportedMzProject,
                ProjectEditability.Invalid,
                ProjectConfidence.Certain,
                [new ProjectEvidence(ProjectEvidenceKind.MzProjectMarker, Path.GetFileName(mzMarker), "RPG Maker MZ project marker")],
                [ProjectDiagnostic.Error(
                    DiagnosticCodes.UnsupportedMzProject,
                    "This folder is an RPG Maker MZ project. Playroma supports RPG Maker MV project discovery only.",
                    new ProjectLocation(Path.GetFileName(mzMarker))) ]);
            yield break;
        }

        MvProjectCandidate? direct = ProbeDirectProject(selectedRoot, directory);
        if (direct is not null)
        {
            yield return direct;
        }

        string www = Path.Combine(directory, "www");
        string wwwSystem = Path.Combine(www, "data", "System.json");
        if (_fileSystem.FileExists(wwwSystem))
        {
            yield return CreateCandidate(
                selectedRoot,
                projectRoot: directory,
                dataRoot: Path.Combine(www, "data"),
                runtimeRoot: www,
                markerPath: FindMarker(www, "*.rpgproject") ?? FindMarker(directory, "*.rpgproject"),
                markerIsMz: false,
                layoutWithoutMarker: MvProjectLayoutKind.ReadableDeployment,
                layoutWithMarker: MvProjectLayoutKind.WwwSubdirectory);
        }

    }

    private MvProjectCandidate? ProbeDirectProject(string selectedRoot, string directory)
    {
        string dataRoot = Path.Combine(directory, "data");
        string systemPath = Path.Combine(dataRoot, "System.json");
        string? marker = FindMarker(directory, "*.rpgproject");
        bool hasSystem = _fileSystem.FileExists(systemPath);
        bool hasDataDirectory = _fileSystem.DirectoryExists(dataRoot);

        if (!hasSystem && marker is null && !hasDataDirectory)
        {
            return null;
        }

        bool hasIndex = _fileSystem.FileExists(Path.Combine(directory, "index.html"));
        bool hasCore = _fileSystem.FileExists(Path.Combine(directory, "js", "rpg_core.js"));
        MvProjectLayoutKind noMarkerLayout = hasIndex || hasCore
            ? MvProjectLayoutKind.BrowserProject
            : MvProjectLayoutKind.IncompleteProject;

        string? runtimeRoot = hasIndex
                              || hasCore
                              || _fileSystem.DirectoryExists(Path.Combine(directory, "js"))
            ? directory
            : null;

        return CreateCandidate(
            selectedRoot,
            directory,
            dataRoot,
            runtimeRoot,
            marker,
            markerIsMz: false,
            noMarkerLayout,
            MvProjectLayoutKind.SourceRoot);
    }

    private MvProjectCandidate CreateCandidate(
        string selectedRoot,
        string projectRoot,
        string dataRoot,
        string? runtimeRoot,
        string? markerPath,
        bool markerIsMz,
        MvProjectLayoutKind layoutWithoutMarker,
        MvProjectLayoutKind layoutWithMarker)
    {
        List<ProjectEvidence> evidence = [];
        List<ProjectDiagnostic> diagnostics = [];
        ProjectPathPolicy paths = new(projectRoot);

        if (markerPath is not null)
        {
            evidence.Add(new ProjectEvidence(
                markerIsMz ? ProjectEvidenceKind.MzProjectMarker : ProjectEvidenceKind.RpgProjectMarker,
                paths.GetRelativePath(markerPath),
                markerIsMz ? "RPG Maker MZ project marker" : "RPG Maker MV project marker"));
        }

        if (_fileSystem.DirectoryExists(dataRoot))
        {
            evidence.Add(new ProjectEvidence(ProjectEvidenceKind.DataDirectory, paths.GetRelativePath(dataRoot), "MV data directory"));
        }

        string systemPath = Path.Combine(dataRoot, "System.json");
        if (_fileSystem.FileExists(systemPath))
        {
            evidence.Add(new ProjectEvidence(ProjectEvidenceKind.SystemJson, paths.GetRelativePath(systemPath), "MV System.json"));
        }

        string mapInfosPath = Path.Combine(dataRoot, "MapInfos.json");
        if (_fileSystem.FileExists(mapInfosPath))
        {
            evidence.Add(new ProjectEvidence(ProjectEvidenceKind.MapInfosJson, paths.GetRelativePath(mapInfosPath), "MV map hierarchy"));
        }

        int standardCount = StandardDatabaseFiles.Count(file => _fileSystem.FileExists(Path.Combine(dataRoot, file)));
        if (standardCount >= 10)
        {
            evidence.Add(new ProjectEvidence(
                ProjectEvidenceKind.StandardDatabaseSet,
                paths.GetRelativePath(dataRoot),
                $"{standardCount} standard MV database files"));
        }

        if (runtimeRoot is not null)
        {
            AddRuntimeEvidence(runtimeRoot, paths, evidence);
        }

        if (HasMvAssetDirectories(runtimeRoot ?? projectRoot))
        {
            evidence.Add(new ProjectEvidence(
                ProjectEvidenceKind.MvAssetDirectories,
                paths.GetRelativePath(runtimeRoot ?? projectRoot),
                "Standard MV asset directories"));
        }

        bool hasSystem = _fileSystem.FileExists(systemPath);
        MvProjectLayoutKind layout = markerPath is not null ? layoutWithMarker : layoutWithoutMarker;
        ProjectConfidence confidence;
        ProjectEditability editability;

        if (markerIsMz)
        {
            confidence = ProjectConfidence.Certain;
            editability = ProjectEditability.Invalid;
        }
        else if (markerPath is not null && hasSystem)
        {
            confidence = ProjectConfidence.Certain;
            editability = ProjectEditability.FullyEditableSource;
        }
        else if (hasSystem && standardCount >= 10)
        {
            confidence = ProjectConfidence.Strong;
            editability = layout == MvProjectLayoutKind.ReadableDeployment
                ? ProjectEditability.ProbablyEditable
                : ProjectEditability.ProbablyEditable;
        }
        else if (hasSystem)
        {
            confidence = ProjectConfidence.Probable;
            editability = ProjectEditability.ReadOnlyInspection;
        }
        else
        {
            confidence = ProjectConfidence.Weak;
            editability = ProjectEditability.ReadOnlyInspection;
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.StandardDataFileMissing,
                "The folder resembles an MV project, but data/System.json is missing.",
                new ProjectLocation(paths.GetRelativePath(dataRoot))));
        }

        return new MvProjectCandidate(
            selectedRoot,
            projectRoot,
            dataRoot,
            runtimeRoot,
            markerPath,
            layout,
            editability,
            confidence,
            evidence,
            diagnostics);
    }

    private void AddRuntimeEvidence(
        string runtimeRoot,
        ProjectPathPolicy paths,
        ICollection<ProjectEvidence> evidence)
    {
        AddIfExists(ProjectEvidenceKind.RuntimeCoreScript, Path.Combine(runtimeRoot, "js", "rpg_core.js"), "MV CoreScript", paths, evidence);
        AddIfExists(ProjectEvidenceKind.IndexHtml, Path.Combine(runtimeRoot, "index.html"), "HTML runtime entry point", paths, evidence);
        AddIfExists(ProjectEvidenceKind.PackageJson, Path.Combine(runtimeRoot, "package.json"), "NW.js package configuration", paths, evidence);
        AddIfExists(ProjectEvidenceKind.PluginsManifest, Path.Combine(runtimeRoot, "js", "plugins.js"), "MV plugin manifest", paths, evidence);

        try
        {
            if (_fileSystem.EnumerateFiles(paths.Root, "*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                evidence.Add(new ProjectEvidence(ProjectEvidenceKind.NwjsExecutable, ".", "Possible NW.js game executable"));
            }
        }
        catch (IOException)
        {
            // Evidence collection is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Evidence collection is best effort.
        }
    }

    private void AddIfExists(
        ProjectEvidenceKind kind,
        string path,
        string description,
        ProjectPathPolicy paths,
        ICollection<ProjectEvidence> evidence)
    {
        if (_fileSystem.FileExists(path))
        {
            evidence.Add(new ProjectEvidence(kind, paths.GetRelativePath(path), description));
        }
    }

    private bool HasMvAssetDirectories(string root) =>
        _fileSystem.DirectoryExists(Path.Combine(root, "img"))
        || _fileSystem.DirectoryExists(Path.Combine(root, "audio"));

    private string? FindMarker(string directory, string pattern)
    {
        try
        {
            return _fileSystem.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

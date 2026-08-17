using System.Globalization;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Documents;

public sealed record ProjectFileInventoryOptions(
    int MaximumFiles = 250_000,
    bool CalculateStrongAssetHashes = false);

public sealed class ProjectFileInventoryBuilder
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ogg", ".m4a", ".wav", ".mp3", ".flac"
    };

    private static readonly HashSet<string> MovieExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mp4", ".ogv"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".woff", ".woff2"
    };

    private static readonly HashSet<string> EncryptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".rpgmvp", ".rpgmvo", ".rpgmvm"
    };

    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;

    public ProjectFileInventoryBuilder(
        IReadOnlyProjectFileSystem fileSystem,
        FileFingerprintService fingerprints)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
    }

    public async Task<OperationResult<ProjectFileInventory>> BuildAsync(
        string projectRoot,
        ProjectFileInventoryOptions options,
        IProgress<ProjectLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(options);

        ProjectPathPolicy paths = new(projectRoot);
        List<ProjectDiagnostic> diagnostics = [];
        List<ProjectFileEntry> entries = [];
        Queue<string> pending = new();
        pending.Enqueue(paths.Root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Dequeue();

            IEnumerable<string> childDirectories;
            IEnumerable<string> files;
            try
            {
                childDirectories = _fileSystem.EnumerateDirectories(directory).ToArray();
                files = _fileSystem.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.AccessDenied,
                    "Access was denied while inventorying the project.",
                    new ProjectLocation(paths.GetRelativePath(directory)),
                    exception.Message));
                continue;
            }
            catch (IOException exception)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.FileReadFailure,
                    "A directory could not be inventoried.",
                    new ProjectLocation(paths.GetRelativePath(directory)),
                    exception.Message));
                continue;
            }

            foreach (string child in childDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileAttributes attributes = _fileSystem.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(ProjectDiagnostic.Information(
                            DiagnosticCodes.ReparsePointSkipped,
                            "A symbolic link, junction, or reparse-point directory was skipped.",
                            new ProjectLocation(paths.GetRelativePath(child))));
                        continue;
                    }

                    if (paths.IsWithinRoot(child))
                    {
                        pending.Enqueue(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(ProjectDiagnostic.Warning(
                        DiagnosticCodes.FileReadFailure,
                        "A directory entry could not be inspected.",
                        new ProjectLocation(paths.GetRelativePath(child)),
                        exception.Message));
                }
            }

            foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count >= options.MaximumFiles)
                {
                    diagnostics.Add(ProjectDiagnostic.Warning(
                        DiagnosticCodes.FileInventoryLimitReached,
                        string.Create(CultureInfo.InvariantCulture, $"The project file inventory stopped at the configured limit of {options.MaximumFiles:N0} files.")));
                    pending.Clear();
                    break;
                }

                string relativePath = paths.GetRelativePath(file);
                ProjectFileKind kind = Classify(relativePath);
                bool hash = ShouldStrongHash(kind, options.CalculateStrongAssetHashes);

                try
                {
                    if ((_fileSystem.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(ProjectDiagnostic.Information(
                            DiagnosticCodes.ReparsePointSkipped,
                            "A symbolic-link or reparse-point file was skipped.",
                            new ProjectLocation(relativePath)));
                        continue;
                    }

                    ContentFingerprint fingerprint;
                    if (hash)
                    {
                        ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(file, true, cancellationToken).ConfigureAwait(false);
                        fingerprint = snapshot.Fingerprint;
                        ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(snapshot, relativePath);
                        if (changed is not null)
                        {
                            diagnostics.Add(changed);
                        }
                    }
                    else
                    {
                        fingerprint = _fingerprints.CreateQuickFingerprint(file);
                    }

                    entries.Add(new ProjectFileEntry(
                        relativePath,
                        kind,
                        fingerprint.Length,
                        fingerprint.LastWriteTimeUtc,
                        Path.GetExtension(file),
                        fingerprint));
                }
                catch (UnauthorizedAccessException exception)
                {
                    diagnostics.Add(ProjectDiagnostic.Error(
                        DiagnosticCodes.AccessDenied,
                        "Access was denied while reading a project file.",
                        new ProjectLocation(relativePath),
                        exception.Message));
                }
                catch (IOException exception)
                {
                    diagnostics.Add(ProjectDiagnostic.Error(
                        DiagnosticCodes.FileReadFailure,
                        "A project file could not be read.",
                        new ProjectLocation(relativePath),
                        exception.Message));
                }

                progress?.Report(new ProjectLoadProgress(
                    ProjectLoadStage.InventoryingFiles,
                    entries.Count,
                    0,
                    relativePath));
            }
        }

        ProjectFileInventory inventory = new(
            entries.OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            entries.Sum(entry => entry.Size));
        return OperationResult<ProjectFileInventory>.Success(inventory, diagnostics);
    }

    public static ProjectFileKind Classify(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string normalized = relativePath.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        string extension = Path.GetExtension(fileName);

        if (fileName.EndsWith(".rpgproject", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rmmzproject", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectFileKind.ProjectMarker;
        }

        if (EncryptedExtensions.Contains(extension)) return ProjectFileKind.EncryptedAsset;
        if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/data/", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
                   && fileName.Length == 11
                   && extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? ProjectFileKind.Map
                : ProjectFileKind.Database;
        }

        if (normalized.EndsWith("js/plugins.js", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.PluginManifest;
        if (normalized.Contains("js/plugins/", StringComparison.OrdinalIgnoreCase) && extension.Equals(".js", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.PluginScript;
        if (normalized.Contains("js/libs/", StringComparison.OrdinalIgnoreCase) && extension.Equals(".js", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.RuntimeLibrary;
        if (normalized.Contains("js/", StringComparison.OrdinalIgnoreCase) && extension.Equals(".js", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.RuntimeScript;
        if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.RuntimeConfiguration;
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.Html;
        if (ImageExtensions.Contains(extension)) return ProjectFileKind.Image;
        if (AudioExtensions.Contains(extension)) return ProjectFileKind.Audio;
        if (MovieExtensions.Contains(extension)) return ProjectFileKind.Movie;
        if (FontExtensions.Contains(extension)) return ProjectFileKind.Font;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.Executable;
        if (extension.Equals(".rpgsave", StringComparison.OrdinalIgnoreCase)) return ProjectFileKind.SaveData;
        return ProjectFileKind.Unknown;
    }

    private static bool ShouldStrongHash(ProjectFileKind kind, bool hashAssets) => kind switch
    {
        ProjectFileKind.Database or
        ProjectFileKind.Map or
        ProjectFileKind.PluginManifest or
        ProjectFileKind.PluginScript or
        ProjectFileKind.RuntimeScript or
        ProjectFileKind.RuntimeLibrary or
        ProjectFileKind.RuntimeConfiguration or
        ProjectFileKind.Html or
        ProjectFileKind.ProjectMarker => true,
        ProjectFileKind.Image or
        ProjectFileKind.Audio or
        ProjectFileKind.Movie or
        ProjectFileKind.Font or
        ProjectFileKind.EncryptedAsset => hashAssets,
        _ => false
    };
}

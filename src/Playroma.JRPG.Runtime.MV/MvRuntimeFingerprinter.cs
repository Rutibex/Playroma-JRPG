using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Runtime.MV;

public sealed partial class MvRuntimeFingerprinter
{
    private static readonly string[] SignificantRuntimePaths =
    [
        "index.html",
        "package.json",
        "js/rpg_core.js",
        "js/rpg_managers.js",
        "js/rpg_objects.js",
        "js/rpg_scenes.js",
        "js/rpg_sprites.js",
        "js/rpg_windows.js",
        "js/main.js",
        "js/plugins.js"
    ];

    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;

    public MvRuntimeFingerprinter(
        IReadOnlyProjectFileSystem fileSystem,
        FileFingerprintService fingerprints)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
    }

    public async Task<MvRuntimeSnapshot> InspectAsync(
        string projectRoot,
        string? runtimeRoot,
        ProjectFileInventory inventory,
        IEnumerable<string> pluginScriptPaths,
        string? markerPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(pluginScriptPaths);
        if (string.IsNullOrWhiteSpace(runtimeRoot) || !_fileSystem.DirectoryExists(runtimeRoot))
        {
            return MvRuntimeSnapshot.Empty with
            {
                Diagnostics =
                [
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.RuntimeIncomplete,
                        "No JavaScript runtime root was found. The project data can still be inspected.")
                ]
            };
        }

        ProjectPathPolicy projectPaths = new(projectRoot);
        ProjectPathPolicy runtimePaths = new(runtimeRoot);
        List<ProjectDiagnostic> diagnostics = [];
        List<RuntimeFileFingerprint> files = [];

        foreach (string relative in SignificantRuntimePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = runtimePaths.ResolveRelative(relative);
            if (!_fileSystem.FileExists(fullPath))
            {
                continue;
            }

            try
            {
                ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(fullPath, true, cancellationToken).ConfigureAwait(false);
                string projectRelative = projectPaths.GetRelativePath(fullPath);
                files.Add(new RuntimeFileFingerprint(
                    projectRelative,
                    snapshot.Fingerprint.Length,
                    snapshot.Fingerprint.Sha256!));
                ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(snapshot, projectRelative);
                if (changed is not null) diagnostics.Add(changed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                    "A runtime file could not be fingerprinted.",
                    new ProjectLocation(projectPaths.GetRelativePath(fullPath)),
                    exception.Message));
            }
        }

        string wrapperPackagePath = Path.Combine(projectRoot, "package.json");
        string runtimePackagePath = runtimePaths.ResolveRelative("package.json");
        if (!string.Equals(Path.GetFullPath(wrapperPackagePath), Path.GetFullPath(runtimePackagePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && _fileSystem.FileExists(wrapperPackagePath))
        {
            try
            {
                ReadFileSnapshot wrapperPackage = await _fingerprints.ReadAsync(
                    wrapperPackagePath,
                    true,
                    cancellationToken).ConfigureAwait(false);
                string relative = projectPaths.GetRelativePath(wrapperPackagePath);
                files.Add(new RuntimeFileFingerprint(
                    relative,
                    wrapperPackage.Fingerprint.Length,
                    wrapperPackage.Fingerprint.Sha256!));
                ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(wrapperPackage, relative);
                if (changed is not null) diagnostics.Add(changed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                    "The outer NW.js package configuration could not be fingerprinted.",
                    new ProjectLocation(projectPaths.GetRelativePath(wrapperPackagePath)),
                    exception.Message));
            }
        }

        foreach (ProjectFileEntry library in inventory.Files.Where(file =>
                     file.Kind == ProjectFileKind.RuntimeLibrary
                     && IsUnder(runtimeRoot, projectRoot, file.RelativePath)))
        {
            if (library.Fingerprint.Sha256 is null) continue;
            if (files.Any(file => string.Equals(file.RelativePath, library.RelativePath, StringComparison.OrdinalIgnoreCase))) continue;
            files.Add(new RuntimeFileFingerprint(library.RelativePath, library.Size, library.Fingerprint.Sha256));
        }

        bool hasIndexHtml = _fileSystem.FileExists(runtimePaths.ResolveRelative("index.html"));
        bool hasPackageJson = _fileSystem.FileExists(runtimePaths.ResolveRelative("package.json"))
                              || _fileSystem.FileExists(Path.Combine(projectRoot, "package.json"));
        bool hasCore = _fileSystem.FileExists(runtimePaths.ResolveRelative("js/rpg_core.js"));
        bool hasEmbeddedExecutable = inventory.Files.Any(file => file.Kind == ProjectFileKind.Executable);
        bool nodeDependent = await DetectNodeDependenceAsync(
            projectRoot,
            pluginScriptPaths,
            cancellationToken).ConfigureAwait(false);

        string? version = await DetectCoreVersionAsync(markerPath, runtimeRoot, cancellationToken).ConfigureAwait(false);
        if (hasCore && version is null)
        {
            diagnostics.Add(ProjectDiagnostic.Information(
                DiagnosticCodes.RuntimeVersionUnknown,
                "The MV CoreScript runtime was found, but its version could not be identified passively."));
        }

        if (nodeDependent)
        {
            diagnostics.Add(ProjectDiagnostic.Information(
                DiagnosticCodes.RuntimeNodeDependency,
                "One or more plugin scripts appear to use Node.js or NW.js APIs. This is a heuristic result."));
        }

        if (!hasIndexHtml || !hasCore)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.RuntimeIncomplete,
                "The runtime is incomplete; index.html and the MV core script are expected for playtesting."));
        }

        RuntimeLayoutKind layout = ResolveLayout(
            hasIndexHtml,
            hasPackageJson,
            hasEmbeddedExecutable,
            hasCore);
        string? signature = CreateRuntimeSignature(files);
        return new MvRuntimeSnapshot(
            layout,
            version,
            null,
            hasIndexHtml,
            hasPackageJson,
            hasEmbeddedExecutable,
            hasIndexHtml && hasCore && !nodeDependent,
            nodeDependent,
            signature,
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
    }

    private async Task<string?> DetectCoreVersionAsync(
        string? markerPath,
        string runtimeRoot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(markerPath) && _fileSystem.FileExists(markerPath))
        {
            byte[] markerBytes = await _fileSystem.ReadAllBytesAsync(markerPath, cancellationToken).ConfigureAwait(false);
            if (Utf8TextDecoder.TryDecode(markerBytes, out string markerText))
            {
                Match markerMatch = MarkerVersionRegex().Match(markerText);
                if (markerMatch.Success) return markerMatch.Groups[1].Value;
            }
        }

        string corePath = Path.Combine(runtimeRoot, "js", "rpg_core.js");
        if (!_fileSystem.FileExists(corePath)) return null;
        byte[] coreBytes = await _fileSystem.ReadAllBytesAsync(corePath, cancellationToken).ConfigureAwait(false);
        if (!Utf8TextDecoder.TryDecode(coreBytes, out string text)) return null;
        Match versionMatch = CoreVersionRegex().Match(text);
        return versionMatch.Success ? versionMatch.Groups[1].Value : null;
    }

    private async Task<bool> DetectNodeDependenceAsync(
        string projectRoot,
        IEnumerable<string> pluginScriptPaths,
        CancellationToken cancellationToken)
    {
        ProjectPathPolicy paths = new(projectRoot);
        foreach (string relativePath in pluginScriptPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath;
            try
            {
                fullPath = paths.ResolveRelative(relativePath);
            }
            catch (ProjectPathException)
            {
                continue;
            }

            if (!_fileSystem.FileExists(fullPath)) continue;
            try
            {
                byte[] bytes = await _fileSystem.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (Utf8TextDecoder.TryDecode(bytes, out string text) && NodeApiRegex().IsMatch(text)) return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Runtime inspection is best effort. The plugin inventory reports the concrete read error.
            }
        }

        return false;
    }

    private static RuntimeLayoutKind ResolveLayout(
        bool hasIndexHtml,
        bool hasPackageJson,
        bool hasExecutable,
        bool hasCore)
    {
        if (hasExecutable && hasPackageJson && hasIndexHtml) return RuntimeLayoutKind.NwjsDeployment;
        if (hasIndexHtml && hasPackageJson && hasCore) return RuntimeLayoutKind.SourceRuntime;
        if (hasIndexHtml && hasCore) return RuntimeLayoutKind.BrowserRuntime;
        if (hasIndexHtml || hasPackageJson || hasCore) return RuntimeLayoutKind.PartialRuntime;
        return RuntimeLayoutKind.None;
    }

    private static string? CreateRuntimeSignature(IEnumerable<RuntimeFileFingerprint> files)
    {
        RuntimeFileFingerprint[] ordered = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) return null;
        string combined = string.Join(
            "\n",
            ordered.Select(file => $"{file.RelativePath.Replace('\\', '/')}:{file.Size}:{file.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined))).ToLowerInvariant();
    }

    private static bool IsUnder(string runtimeRoot, string projectRoot, string relativePath)
    {
        string runtimeRelative = Path.GetRelativePath(projectRoot, runtimeRoot).Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(runtimeRelative) || runtimeRelative == ".") return true;
        return relativePath.Replace('\\', '/').StartsWith(runtimeRelative + "/", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"RPGMV\s+([0-9]+(?:\.[0-9]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerVersionRegex();

    [GeneratedRegex(@"RPGMAKER_VERSION\s*=\s*[""']([0-9]+(?:\.[0-9]+){1,3})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoreVersionRegex();

    [GeneratedRegex(@"\brequire\s*\(|\bprocess\s*\.|\bnw\s*\.|\bnw\.[A-Za-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NodeApiRegex();
}

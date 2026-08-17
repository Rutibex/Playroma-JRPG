using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvRuntimeInstallRequest(
    string ProjectRoot,
    RpgMakerMvImageProfile Profile,
    string FileName,
    PixelBuffer Pixels,
    bool CreateBackup = true,
    bool AllowOverwrite = true,
    string BackupFolderName = ".playroma-backups");

public sealed record MvRuntimeInstallResult(
    string FullPath,
    string RuntimeRelativePath,
    string? BackupPath,
    IReadOnlyList<MvAssetValidationIssue> ValidationIssues,
    DateTimeOffset InstalledAtUtc);

public static class MvRuntimeInstaller
{
    public static MvRuntimeInstallResult Install(MvRuntimeInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.Pixels);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        string projectRoot = Path.GetFullPath(request.ProjectRoot);
        if (!Directory.Exists(projectRoot)) throw new DirectoryNotFoundException(projectRoot);
        if (Path.IsPathRooted(request.FileName) || request.FileName.Contains('/') || request.FileName.Contains('\\'))
            throw new InvalidDataException("Runtime asset filename may not contain a directory path.");
        string safeFileName = Path.GetFileName(request.FileName);
        if (!safeFileName.Equals(request.FileName, StringComparison.Ordinal) || safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Runtime asset filename contains invalid characters.");
        if (!Path.GetExtension(safeFileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
            safeFileName += ".png";

        string relativeFolder = request.Profile.AssetFolder.Replace('/', Path.DirectorySeparatorChar);
        string destinationFolder = Path.GetFullPath(Path.Combine(projectRoot, relativeFolder));
        EnsureInsideProject(projectRoot, destinationFolder);
        Directory.CreateDirectory(destinationFolder);
        string destination = Path.GetFullPath(Path.Combine(destinationFolder, safeFileName));
        EnsureInsideProject(projectRoot, destination);

        IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(
            request.Profile,
            new MvAssetValidationContext(safeFileName, request.Profile.AssetFolder, request.Pixels.Width, request.Pixels.Height));
        if (issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
            throw new MvAssetValidationException(request.Profile.Id, issues);
        if (File.Exists(destination) && !request.AllowOverwrite)
            throw new IOException($"Runtime asset already exists: {destination}");

        string? backupPath = null;
        if (File.Exists(destination) && request.CreateBackup)
        {
            string relative = Path.GetRelativePath(projectRoot, destination);
            string backupRoot = Path.GetFullPath(Path.Combine(projectRoot, request.BackupFolderName, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
            EnsureInsideProject(projectRoot, backupRoot);
            backupPath = Path.GetFullPath(Path.Combine(backupRoot, relative));
            EnsureInsideProject(projectRoot, backupPath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(destination, backupPath, overwrite: false);
        }

        PngCodec.Save(request.Pixels, destination);
        string runtimeRelative = Path.GetRelativePath(projectRoot, destination).Replace('\\', '/');
        return new MvRuntimeInstallResult(destination, runtimeRelative, backupPath, issues, DateTimeOffset.UtcNow);
    }

    private static void EnsureInsideProject(string projectRoot, string candidate)
    {
        string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!normalizedCandidate.StartsWith(normalizedRoot, comparison) && !normalizedCandidate.Equals(projectRoot, comparison))
            throw new UnauthorizedAccessException("Runtime asset destination escapes the RPG Maker MV project root.");
    }
}

public sealed class MvAssetValidationException : IOException
{
    public MvAssetValidationException(string profileId, IReadOnlyList<MvAssetValidationIssue> issues)
        : base($"Asset does not satisfy RPG Maker MV profile '{profileId}': {string.Join("; ", issues.Where(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)).Select(issue => issue.Message))}")
    {
        ProfileId = profileId;
        Issues = issues;
    }

    public string ProfileId { get; }
    public IReadOnlyList<MvAssetValidationIssue> Issues { get; }
}

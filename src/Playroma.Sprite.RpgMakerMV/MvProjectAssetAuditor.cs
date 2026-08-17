namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvProjectAssetAuditEntry(
    string RuntimeRelativePath,
    string? DetectedProfileId,
    string? DetectedProfileName,
    double Confidence,
    IReadOnlyList<MvAssetValidationIssue> Issues);

public sealed record MvProjectAssetAuditResult(
    string ProjectRoot,
    IReadOnlyList<MvProjectAssetAuditEntry> Assets)
{
    public int ErrorCount => Assets.Sum(asset => asset.Issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)));
    public int WarningCount => Assets.Sum(asset => asset.Issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)));
    public int UnrecognizedCount => Assets.Count(asset => asset.DetectedProfileId is null);
    public bool Success => ErrorCount == 0 && UnrecognizedCount == 0;
}

public sealed class MvProjectAssetAuditor
{
    private readonly RpgMakerMvProfileRegistry profiles;
    private readonly MvAssetProfileDetector detector;

    public MvProjectAssetAuditor(RpgMakerMvProfileRegistry profiles)
    {
        this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        detector = new MvAssetProfileDetector(profiles);
    }

    public MvProjectAssetAuditResult Audit(string projectRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        List<MvProjectAssetAuditEntry> entries = [];
        foreach (string folder in profiles.Profiles.Select(profile => profile.AssetFolder).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullFolder = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullFolder)) continue;
            foreach (string path in Directory.EnumerateFiles(fullFolder, "*.png", SearchOption.TopDirectoryOnly).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PngImageInfo info;
                try { info = PngHeaderReader.Read(path); }
                catch (Exception exception)
                {
                    entries.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), null, null, 0,
                        [new MvAssetValidationIssue("PSPR-MV-900", "error", "PNG header could not be read: " + exception.Message)]));
                    continue;
                }
                string runtimeRelative = Path.GetRelativePath(root, path).Replace('\\', '/');
                MvProfileDetectionCandidate? candidate = detector.DetectBest(new MvProfileDetectionInput(Path.GetFileName(path), folder, info.Width, info.Height), minimumScore: 0);
                if (candidate is null)
                {
                    entries.Add(new(runtimeRelative, null, null, 0,
                        [new MvAssetValidationIssue("PSPR-MV-901", "warning", "No RPG Maker MV image profile could be identified.")]));
                    continue;
                }
                IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(candidate.Profile, new MvAssetValidationContext(Path.GetFileName(path), folder, info.Width, info.Height));
                entries.Add(new(runtimeRelative, candidate.Profile.Id, candidate.Profile.DisplayName, candidate.Confidence, issues));
            }
        }
        return new MvProjectAssetAuditResult(root, entries);
    }
}

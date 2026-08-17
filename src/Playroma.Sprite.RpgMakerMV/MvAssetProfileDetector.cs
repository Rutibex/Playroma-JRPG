namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvProfileDetectionInput(string FileName, string? RelativeFolder, int Width, int Height);

public sealed record MvProfileDetectionCandidate(
    RpgMakerMvImageProfile Profile,
    int Score,
    double Confidence,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<MvAssetValidationIssue> ValidationIssues);

public sealed class MvAssetProfileDetector
{
    private readonly RpgMakerMvProfileRegistry registry;

    public MvAssetProfileDetector(RpgMakerMvProfileRegistry registry) =>
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public IReadOnlyList<MvProfileDetectionCandidate> Detect(MvProfileDetectionInput input, int maximumResults = 8)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Width <= 0 || input.Height <= 0) throw new ArgumentOutOfRangeException(nameof(input), "Dimensions must be positive.");
        if (maximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResults));

        List<(RpgMakerMvImageProfile Profile, int Score, List<string> Reasons, IReadOnlyList<MvAssetValidationIssue> Issues)> ranked = [];
        string folder = NormalizeFolder(input.RelativeFolder);
        string stem = Path.GetFileNameWithoutExtension(input.FileName);

        foreach (RpgMakerMvImageProfile profile in registry.Profiles)
        {
            List<string> reasons = [];
            int score = 0;
            string expectedFolder = NormalizeFolder(profile.AssetFolder);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (folder.Equals(expectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    score += 60;
                    reasons.Add("Runtime folder matches exactly.");
                }
                else if (folder.EndsWith(expectedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    score += 45;
                    reasons.Add("Runtime folder suffix matches.");
                }
                else score -= 40;
            }

            if (profile.Metadata.TryGetValue("expectedFileName", out string? expectedName))
            {
                if (input.FileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    score += 80;
                    reasons.Add("Reserved system filename matches.");
                }
                else score -= 35;
            }

            bool dollar = stem.StartsWith('$') || stem.StartsWith("!$", StringComparison.Ordinal);
            bool bang = stem.StartsWith('!');
            if (profile.FilenameRules.RequireDollarPrefix)
            {
                if (dollar) { score += 30; reasons.Add("Single-character '$' prefix matches."); }
                else score -= 40;
            }
            else if (!profile.FilenameRules.AllowDollarPrefix && dollar) score -= 25;

            if (bang && (profile.FilenameRules.AllowObjectPrefix || profile.FilenameRules.AllowParallaxFloorPrefix))
            {
                score += 15;
                reasons.Add("'!' filename behaviour is supported.");
            }

            IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(
                profile,
                new MvAssetValidationContext(input.FileName, string.IsNullOrWhiteSpace(folder) ? profile.AssetFolder : folder, input.Width, input.Height));
            int errors = issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
            int warnings = issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));
            if (errors == 0)
            {
                score += 55;
                reasons.Add("Dimensions and grid satisfy the profile.");
            }
            else score -= errors * 28;
            score -= warnings * 4;

            if (profile.Dimensions.FixedWidth == input.Width) score += 12;
            if (profile.Dimensions.FixedHeight == input.Height) score += 12;
            if (profile.Dimensions.RecommendedWidth == input.Width) score += 8;
            if (profile.Dimensions.RecommendedHeight == input.Height) score += 8;

            if (profile.Grid is MvGridRule grid)
            {
                if (grid.CellWidth is int cellWidth && input.Width % cellWidth == 0) score += 6;
                if (grid.CellHeight is int cellHeight && input.Height % cellHeight == 0) score += 6;
                if (grid.Columns is int columns && input.Width % columns == 0) score += 5;
                if (grid.Rows is int rows && input.Height % rows == 0) score += 5;
            }

            ranked.Add((profile, score, reasons, issues));
        }

        int best = ranked.Max(item => item.Score);
        int floor = ranked.Min(item => item.Score);
        double span = Math.Max(1, best - floor);
        return ranked
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .Select(item => new MvProfileDetectionCandidate(
                item.Profile,
                item.Score,
                Math.Clamp((item.Score - floor) / span, 0d, 1d),
                item.Reasons,
                item.Issues))
            .ToArray();
    }

    public MvProfileDetectionCandidate? DetectBest(MvProfileDetectionInput input, int minimumScore = 20) =>
        Detect(input, 1).FirstOrDefault(candidate => candidate.Score >= minimumScore);

    private static string NormalizeFolder(string? folder) =>
        (folder ?? string.Empty).Replace('\\', '/').Trim('/');
}

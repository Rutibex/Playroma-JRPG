namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvAssetValidationContext(string FileName, string RelativeFolder, int Width, int Height);
public sealed record MvAssetValidationIssue(string Code, string Severity, string Message);

public static class MvAssetValidator
{
    public static IReadOnlyList<MvAssetValidationIssue> ValidateFile(RpgMakerMvImageProfile profile, string filePath, string relativeFolder)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        PngImageInfo info = PngHeaderReader.Read(filePath);
        return Validate(profile, new MvAssetValidationContext(Path.GetFileName(filePath), relativeFolder, info.Width, info.Height));
    }

    public static IReadOnlyList<MvAssetValidationIssue> Validate(RpgMakerMvImageProfile profile, MvAssetValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        List<MvAssetValidationIssue> issues = [];
        if (!Path.GetExtension(context.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("PSPR-MV-001", "error", "RPG Maker MV runtime images must be PNG files."));
        string folder = NormalizeFolder(context.RelativeFolder);
        string expected = NormalizeFolder(profile.AssetFolder);
        if (!folder.Equals(expected, StringComparison.OrdinalIgnoreCase) && !folder.EndsWith("/" + expected, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("PSPR-MV-002", "error", $"Asset belongs in '{profile.AssetFolder}'."));
        if (context.Width <= 0 || context.Height <= 0)
        {
            issues.Add(new("PSPR-MV-003", "error", "Image dimensions must be positive."));
            return issues;
        }
        ValidateDimensions(profile, context, issues);
        ValidateGrid(profile, context, issues);
        ValidateFileName(profile, context.FileName, issues);
        ValidateProfileSpecificRules(profile, context, issues);
        if (MvSystemAssetCatalog.TryGet(profile.Id, out MvSystemAssetDefinition? systemDefinition))
        {
            foreach (MvSystemAssetDiagnostic diagnostic in MvSystemAssetCatalog.Validate(systemDefinition, context.FileName, context.Width, context.Height))
            {
                if (diagnostic.Code == "PSPR-SYS-001") continue;
                if (!issues.Any(issue => issue.Code == diagnostic.Code)) issues.Add(new(diagnostic.Code, diagnostic.Severity, diagnostic.Message));
            }
        }
        return issues;
    }

    private static void ValidateDimensions(RpgMakerMvImageProfile profile, MvAssetValidationContext context, List<MvAssetValidationIssue> issues)
    {
        MvDimensionRule rule = profile.Dimensions;
        if (rule.FixedWidth is int width && context.Width != width) issues.Add(new("PSPR-MV-010", "error", $"Width must be {width} pixels."));
        if (rule.FixedHeight is int height && context.Height != height) issues.Add(new("PSPR-MV-011", "error", $"Height must be {height} pixels."));
        if (rule.RecommendedWidth is int recommendedWidth && context.Width != recommendedWidth) issues.Add(new("PSPR-MV-012", "warning", $"The stock MV width is {recommendedWidth} pixels."));
        if (rule.RecommendedHeight is int recommendedHeight && context.Height != recommendedHeight) issues.Add(new("PSPR-MV-013", "warning", $"The stock MV height is {recommendedHeight} pixels."));
    }

    private static void ValidateGrid(RpgMakerMvImageProfile profile, MvAssetValidationContext context, List<MvAssetValidationIssue> issues)
    {
        MvGridRule? grid = profile.Grid;
        if (grid is null) return;
        if (grid.Columns is int columns && context.Width % columns != 0) issues.Add(new("PSPR-MV-020", "error", $"Width must divide into exactly {columns} columns."));
        if (grid.Rows is int rows && context.Height % rows != 0) issues.Add(new("PSPR-MV-021", "error", $"Height must divide into exactly {rows} rows."));
        if (grid.CellWidth is int cellWidth && context.Width % cellWidth != 0) issues.Add(new("PSPR-MV-022", "error", $"Width must be a multiple of the {cellWidth}-pixel cell width."));
        if (grid.CellHeight is int cellHeight && context.Height % cellHeight != 0) issues.Add(new("PSPR-MV-023", "error", $"Height must be a multiple of the {cellHeight}-pixel cell height."));
        int? actualColumns = grid.CellWidth is int cw && context.Width % cw == 0 ? context.Width / cw : grid.Columns;
        int? actualRows = grid.CellHeight is int ch && context.Height % ch == 0 ? context.Height / ch : grid.Rows;
        if (grid.Columns is int exactColumns && actualColumns is int ac && ac != exactColumns) issues.Add(new("PSPR-MV-024", "error", $"Sheet must contain exactly {exactColumns} columns."));
        if (grid.Rows is int exactRows && actualRows is int ar && ar != exactRows) issues.Add(new("PSPR-MV-025", "error", $"Sheet must contain exactly {exactRows} rows."));
        if (grid.MinimumRows is int minimum && actualRows is int minRows && minRows < minimum) issues.Add(new("PSPR-MV-026", "error", $"Sheet requires at least {minimum} row(s)."));
        if (grid.MaximumRows is int maximum && actualRows is int maxRows && maxRows > maximum) issues.Add(new("PSPR-MV-027", "error", $"Sheet supports at most {maximum} rows."));
        if (grid.MaximumCells is int maximumCells && actualColumns is int cols && actualRows is int rs && cols * rs > maximumCells) issues.Add(new("PSPR-MV-028", "error", $"Sheet supports at most {maximumCells} cells."));
    }

    private static void ValidateFileName(RpgMakerMvImageProfile profile, string fileName, List<MvAssetValidationIssue> issues)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        bool dollar = stem.StartsWith('$') || stem.StartsWith("!$", StringComparison.Ordinal);
        bool objectPrefix = stem.StartsWith('!');
        if (profile.FilenameRules.RequireDollarPrefix && !dollar) issues.Add(new("PSPR-MV-030", "error", "Single-character sheets require the '$' filename prefix."));
        if (!profile.FilenameRules.AllowDollarPrefix && dollar) issues.Add(new("PSPR-MV-031", "error", "This profile does not permit the '$' filename prefix."));
        if (!profile.FilenameRules.AllowObjectPrefix && !profile.FilenameRules.AllowParallaxFloorPrefix && objectPrefix) issues.Add(new("PSPR-MV-032", "error", "This profile does not permit the '!' filename prefix."));
        if (profile.Metadata.TryGetValue("expectedFileName", out string? expectedFileName) &&
            !fileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("PSPR-MV-033", "error", $"This system profile must use the reserved filename '{expectedFileName}'."));
    }

    private static void ValidateProfileSpecificRules(RpgMakerMvImageProfile profile, MvAssetValidationContext context, List<MvAssetValidationIssue> issues)
    {
        switch (profile.Id.ToLowerInvariant())
        {
            case "mv-animation-sheet":
                if (context.Width != 960) issues.Add(new("PSPR-MV-040", "error", "Animation sheets must be exactly 960 pixels wide (five 192-pixel cells)."));
                if (context.Height % 192 != 0) issues.Add(new("PSPR-MV-041", "error", "Animation sheet height must be a multiple of 192 pixels."));
                break;
            case "mv-character-standard":
                ValidateCharacterCellAspect(context, 12, 8, issues);
                break;
            case "mv-character-single":
                ValidateCharacterCellAspect(context, 3, 4, issues);
                break;
            case "mv-face-sheet":
                if (context.Width != 576 || context.Height != 288)
                    issues.Add(new("PSPR-MV-042", "error", "Face sheets must be 576×288 pixels: four columns by two rows of 144×144 portraits."));
                break;
            case "mv-sv-actor":
                if (context.Width % 9 == 0 && context.Height % 6 == 0 && context.Width / 9 != context.Height / 6)
                    issues.Add(new("PSPR-MV-043", "warning", "Side-view actor cells are normally square; the stock size is 64×64."));
                break;
            case "mv-iconset":
                if (context.Width != 512) issues.Add(new("PSPR-MV-044", "error", "IconSet width must be 512 pixels: sixteen 32-pixel icons."));
                break;
            case "mv-tileset":
                string stem = Path.GetFileNameWithoutExtension(context.FileName);
                ValidateTilesetVariant(stem, context, issues);
                break;
        }
    }

    private static void ValidateCharacterCellAspect(MvAssetValidationContext context, int columns, int rows, List<MvAssetValidationIssue> issues)
    {
        if (context.Width % columns != 0 || context.Height % rows != 0) return;
        int cellWidth = context.Width / columns;
        int cellHeight = context.Height / rows;
        if (cellWidth != cellHeight)
            issues.Add(new("PSPR-MV-045", "warning", $"Character cells are normally square; this sheet uses {cellWidth}×{cellHeight} cells."));
    }

    private static void ValidateTilesetVariant(string stem, MvAssetValidationContext context, List<MvAssetValidationIssue> issues)
    {
        string normalized = stem.ToUpperInvariant();
        string suffix = new[] { "A1", "A2", "A3", "A4", "A5", "B", "C", "D", "E" }
            .FirstOrDefault(candidate => normalized.Equals(candidate, StringComparison.Ordinal) || normalized.EndsWith("_" + candidate, StringComparison.Ordinal))
            ?? string.Empty;
        (int Width, int Height)? fixedSize = suffix switch
        {
            "A1" => (768, 576),
            "A2" => (768, 576),
            "A3" => (768, 384),
            "A4" => (768, 720),
            "A5" => (384, 768),
            _ => null
        };
        if (fixedSize is { } size && (context.Width != size.Width || context.Height != size.Height))
            issues.Add(new("PSPR-MV-046", "error", $"Tileset {suffix} must be {size.Width}×{size.Height} pixels."));
        if (suffix is "B" or "C" or "D" or "E" && (context.Width != 768 || context.Height != 768))
            issues.Add(new("PSPR-MV-047", "error", $"Tileset {suffix} must be 768×768 pixels."));
    }

    private static string NormalizeFolder(string folder) => folder.Replace('\\', '/').Trim('/');
}

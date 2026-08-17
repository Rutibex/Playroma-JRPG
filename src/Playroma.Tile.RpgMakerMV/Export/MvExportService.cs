using System.Text;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Import;
using Playroma.Tile.RpgMakerMV.Validation;

namespace Playroma.Tile.RpgMakerMV.Export;

public sealed class MvExportService
{
    private readonly MvValidator _validator = new();

    public MvExportResult ExportTileSet(
        TileProject project,
        TileSetDefinition tileSet,
        string destinationDirectory,
        MvExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tileSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        options ??= new MvExportOptions();

        var issues = _validator.ValidateTileSet(project, tileSet);
        var errors = issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray();
        if (options.StopOnValidationErrors && errors.Length > 0)
        {
            throw new InvalidOperationException(
                "Export was stopped because validation found errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(error => "• " + error.Message)));
        }

        Directory.CreateDirectory(destinationDirectory);
        var result = new MvExportResult();
        result.Warnings.AddRange(issues
            .Where(issue => issue.Severity == ValidationSeverity.Warning)
            .Select(issue => issue.Message));

        foreach (var sheet in tileSet.Sheets.OrderBy(sheet => sheet.Slot))
        {
            var sourcePath = ProjectPath.Resolve(project, sheet.RelativeImagePath);
            var destinationPath = Path.Combine(destinationDirectory, sheet.FileName);
            CopyFile(sourcePath, destinationPath, options.OverwriteExistingFiles);
            result.ExportedFiles.Add(destinationPath);

            if (options.IncludeCatalogTextFiles)
            {
                var catalogPath = Path.Combine(destinationDirectory, Path.ChangeExtension(sheet.FileName, ".txt"));
                if (File.Exists(catalogPath) && !options.OverwriteExistingFiles)
                {
                    throw new IOException($"The export file already exists: {catalogPath}");
                }

                CatalogTextParser.Write(
                    catalogPath,
                    sheet.Regions.OrderBy(region => region.Index).Select(region =>
                        new CatalogEntry(region.Index + 1, region.Name, region.AlternateName)));
                result.ExportedFiles.Add(catalogPath);
            }
        }

        if (options.IncludeExportReport)
        {
            var reportPath = Path.Combine(destinationDirectory, "PlayromaTile_ExportReport.txt");
            if (File.Exists(reportPath) && !options.OverwriteExistingFiles)
            {
                throw new IOException($"The export file already exists: {reportPath}");
            }

            File.WriteAllText(reportPath, CreateReport(project, tileSet, issues), new UTF8Encoding(true));
            result.ExportedFiles.Add(reportPath);
        }

        return result;
    }

    public MvExportResult ExportAllTileSets(
        TileProject project,
        string destinationRoot,
        MvExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var aggregate = new MvExportResult();
        foreach (var tileSet in project.TileSets)
        {
            var folderName = ProjectPath.SanitizeFileName(tileSet.Name);
            var result = ExportTileSet(project, tileSet, Path.Combine(destinationRoot, folderName), options);
            aggregate.ExportedFiles.AddRange(result.ExportedFiles);
            aggregate.Warnings.AddRange(result.Warnings.Select(warning => $"{tileSet.Name}: {warning}"));
        }

        return aggregate;
    }

    private static void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"The export file already exists: {destinationPath}");
        }

        File.Copy(sourcePath, destinationPath, overwrite);
    }

    private static string CreateReport(
        TileProject project,
        TileSetDefinition tileSet,
        IReadOnlyList<MvValidationIssue> issues)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PLAYROMA TILE — RPG MAKER MV EXPORT REPORT");
        builder.AppendLine(new string('=', 46));
        builder.AppendLine($"Project: {project.Name}");
        builder.AppendLine($"Tileset: {tileSet.Name}");
        builder.AppendLine($"Exported UTC: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();
        builder.AppendLine("Sheets:");
        foreach (var sheet in tileSet.Sheets.OrderBy(sheet => sheet.Slot))
        {
            builder.AppendLine($"  {sheet.Slot}: {sheet.FileName} ({sheet.PixelWidth}x{sheet.PixelHeight})");
            builder.AppendLine($"      Source SHA-256: {sheet.SourceSha256}");
        }

        builder.AppendLine();
        builder.AppendLine("Validation:");
        if (issues.Count == 0)
        {
            builder.AppendLine("  No issues.");
        }
        else
        {
            foreach (var issue in issues)
            {
                builder.AppendLine($"  [{issue.Severity}] {issue.Message}");
            }
        }

        return builder.ToString();
    }
}

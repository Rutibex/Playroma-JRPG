using System.Drawing;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;

namespace Playroma.Tile.RpgMakerMV.Validation;

public sealed class MvValidator
{
    public IReadOnlyList<MvValidationIssue> ValidateProject(TileProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<MvValidationIssue>();

        foreach (var tileSet in project.TileSets)
        {
            issues.AddRange(ValidateTileSet(project, tileSet));
        }

        if (project.TileSets.Count == 0)
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Info,
                "The project contains no imported tilesets.",
                project.Name));
        }

        return issues;
    }

    public IReadOnlyList<MvValidationIssue> ValidateTileSet(TileProject project, TileSetDefinition tileSet)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tileSet);
        var issues = new List<MvValidationIssue>();

        foreach (var duplicateGroup in tileSet.Sheets.GroupBy(sheet => sheet.Slot).Where(group => group.Count() > 1))
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Error,
                $"The tileset contains more than one {duplicateGroup.Key} sheet.",
                tileSet.Name));
        }

        foreach (var sheet in tileSet.Sheets)
        {
            issues.AddRange(ValidateSheet(project, tileSet, sheet));
        }

        var presentSlots = tileSet.Sheets.Select(sheet => sheet.Slot).ToHashSet();
        foreach (var slot in Enum.GetValues<MvSheetSlot>())
        {
            if (!presentSlots.Contains(slot))
            {
                issues.Add(new MvValidationIssue(
                    ValidationSeverity.Info,
                    $"No {slot} sheet is present. Partial RPG Maker MV tilesets are allowed.",
                    tileSet.Name));
            }
        }

        return issues;
    }

    private static IEnumerable<MvValidationIssue> ValidateSheet(
        TileProject project,
        TileSetDefinition tileSet,
        TileSheetDefinition sheet)
    {
        var issues = new List<MvValidationIssue>();
        var specification = MvLayoutCatalog.GetSpecification(sheet.Slot);
        string imagePath;
        try
        {
            imagePath = ProjectPath.Resolve(project, sheet.RelativeImagePath);
        }
        catch (Exception ex)
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Error,
                $"Invalid image path: {ex.Message}",
                tileSet.Name,
                sheet.Id));
            return issues;
        }

        if (!File.Exists(imagePath))
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Error,
                $"Image file is missing: {sheet.RelativeImagePath}",
                tileSet.Name,
                sheet.Id));
            return issues;
        }

        if (!string.Equals(Path.GetExtension(imagePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Error,
                "RPG Maker MV tileset images must use PNG format.",
                tileSet.Name,
                sheet.Id));
        }

        Bitmap? bitmap = null;
        try
        {
            bitmap = BitmapFile.LoadEditable(imagePath);
            if (bitmap.Width != specification.Width || bitmap.Height != specification.Height)
            {
                issues.Add(new MvValidationIssue(
                    ValidationSeverity.Error,
                    $"{sheet.Slot} must be {specification.Width}x{specification.Height}; " +
                    $"this image is {bitmap.Width}x{bitmap.Height}.",
                    tileSet.Name,
                    sheet.Id));
            }

            if (bitmap.Width % MvSheetSpecification.TileSize != 0 ||
                bitmap.Height % MvSheetSpecification.TileSize != 0)
            {
                issues.Add(new MvValidationIssue(
                    ValidationSeverity.Error,
                    "The sheet dimensions are not aligned to the 48x48 MV tile grid.",
                    tileSet.Name,
                    sheet.Id));
            }

            if (specification.RequiresTransparentFirstCell && bitmap.Width >= 48 && bitmap.Height >= 48 &&
                !RegionImageEditor.IsFullyTransparent(bitmap, new Rectangle(0, 0, 48, 48)))
            {
                issues.Add(new MvValidationIssue(
                    ValidationSeverity.Error,
                    "The upper-left 48x48 tile of sheet B must be fully transparent.",
                    tileSet.Name,
                    sheet.Id,
                    0));
            }
        }
        catch (Exception ex)
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Error,
                $"The PNG could not be read: {ex.Message}",
                tileSet.Name,
                sheet.Id));
        }
        finally
        {
            bitmap?.Dispose();
        }

        var expectedRegions = MvLayoutCatalog.GetLogicalRegions(sheet.Slot).Count;
        if (sheet.Regions.Count != expectedRegions)
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Warning,
                $"The sheet has {sheet.Regions.Count} metadata records; {sheet.Slot} expects {expectedRegions}.",
                tileSet.Name,
                sheet.Id));
        }

        var duplicateIndexes = sheet.Regions.GroupBy(region => region.Index).Where(group => group.Count() > 1).ToArray();
        foreach (var duplicate in duplicateIndexes)
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Warning,
                $"Metadata index {duplicate.Key} appears more than once.",
                tileSet.Name,
                sheet.Id,
                duplicate.Key));
        }

        foreach (var region in sheet.Regions.Where(region => string.IsNullOrWhiteSpace(region.Name)))
        {
            issues.Add(new MvValidationIssue(
                ValidationSeverity.Info,
                $"Logical region {region.Index + 1} has no name.",
                tileSet.Name,
                sheet.Id,
                region.Index));
        }

        return issues;
    }
}

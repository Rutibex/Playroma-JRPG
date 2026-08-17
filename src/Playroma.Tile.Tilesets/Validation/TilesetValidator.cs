using System.Drawing;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Services;

namespace Playroma.Tile.Tilesets.Validation;

public sealed class TilesetValidator
{
    public IReadOnlyList<TilesetIssue> Validate(TileProject project, TileSetDefinition tileSet,
        TilesetAssemblyDefinition definition, IReadOnlyDictionary<MvSheetSlot, Bitmap>? sheets = null)
    {
        var issues = new List<TilesetIssue>();
        foreach (var specification in MvLayoutCatalog.AllSpecifications)
        {
            var sheet = tileSet.Sheets.FirstOrDefault(x => x.Slot == specification.Slot);
            if (sheet is null)
            {
                issues.Add(Error("missing-sheet", $"Required sheet {specification.Slot} is missing.", specification.Slot));
                continue;
            }
            if (sheet.PixelWidth != specification.Width || sheet.PixelHeight != specification.Height)
                issues.Add(Error("sheet-size", $"{sheet.Slot} is {sheet.PixelWidth}×{sheet.PixelHeight}; expected {specification.Width}×{specification.Height}.", sheet.Slot));
        }

        var duplicates = definition.Metadata.GroupBy(x => (x.Sheet, x.CellIndex)).Where(x => x.Count() > 1).ToArray();
        foreach (var duplicate in duplicates)
            issues.Add(Error("duplicate-metadata", $"{duplicate.Key.Sheet} cell {duplicate.Key.CellIndex + 1} has duplicate metadata records.", duplicate.Key.Sheet, duplicate.Key.CellIndex));

        foreach (var item in definition.Metadata)
        {
            var max = MvLayoutCatalog.GetLogicalRegions(item.Sheet).Count;
            if (item.CellIndex < 0 || item.CellIndex >= max)
                issues.Add(Error("metadata-range", $"Metadata references invalid {item.Sheet} cell {item.CellIndex}.", item.Sheet, item.CellIndex));
            if (item.Metadata.TerrainTag is < 0 or > 7)
                issues.Add(Error("terrain-tag", $"Terrain tag must be between 0 and 7.", item.Sheet, item.CellIndex));
            if (item.Metadata.Counter && item.Metadata.Passage == PassageMode.Impassable)
                issues.Add(Warning("counter-blocked", "A counter is marked fully impassable; verify the intended interaction.", item.Sheet, item.CellIndex));
            if (item.Metadata.DamageFloor && item.Metadata.Passage == PassageMode.Impassable)
                issues.Add(Warning("damage-blocked", "An impassable damage floor cannot normally be stepped on.", item.Sheet, item.CellIndex));
        }

        var dependency = new TilesetDependencyService();
        foreach (var binding in definition.Assets)
        {
            var status = dependency.GetStatus(project, binding);
            if (status == TilesetAssetStatus.MissingSource)
                issues.Add(Error("missing-source", $"{binding.Name} has a missing source asset.", binding.Sheet, binding.LogicalSlotIndex, binding.Id));
            else if (status == TilesetAssetStatus.SourceChanged)
                issues.Add(Warning("stale-source", $"{binding.Name} is older than its source asset.", binding.Sheet, binding.LogicalSlotIndex, binding.Id));
        }

        foreach (var obj in definition.Objects)
        {
            if (obj.GridWidth < 1 || obj.GridHeight < 1)
                issues.Add(Error("object-size", $"Object {obj.Name} has an invalid grid size.", assetId: obj.Id));
            if (obj.AnchorCellX < 0 || obj.AnchorCellY < 0 || obj.AnchorCellX >= obj.GridWidth || obj.AnchorCellY >= obj.GridHeight)
                issues.Add(Error("object-anchor", $"Object {obj.Name} has an anchor outside its grid.", assetId: obj.Id));
        }

        if (sheets is not null && sheets.TryGetValue(MvSheetSlot.B, out var b) && !IsTransparent(b, 0, 0))
            issues.Add(Error("b-origin", "The upper-left tile of sheet B must be transparent.", MvSheetSlot.B, 0));

        var template = new ConstructionTemplateCatalog().Get(definition.TemplateKind);
        var completeness = new CompletenessAnalyzer().Analyze(definition, template);
        foreach (var missing in completeness.Coverage.Where(x => !x.IsSatisfied && x.Requirement.Level == RequirementLevel.Required))
            issues.Add(Warning("missing-required-category", $"Construction profile is missing: {missing.Requirement.Name}."));
        return issues;
    }

    private static bool IsTransparent(Bitmap bitmap, int cellX, int cellY)
    {
        for (var y = cellY * 48; y < cellY * 48 + 48; y += 4)
        for (var x = cellX * 48; x < cellX * 48 + 48; x += 4)
            if (bitmap.GetPixel(x, y).A != 0) return false;
        return true;
    }

    private static TilesetIssue Error(string code, string message, MvSheetSlot? sheet = null, int? cell = null, Guid? assetId = null)
        => new() { Severity = TilesetIssueSeverity.Error, Code = code, Message = message, Sheet = sheet, CellIndex = cell, AssetId = assetId };
    private static TilesetIssue Warning(string code, string message, MvSheetSlot? sheet = null, int? cell = null, Guid? assetId = null)
        => new() { Severity = TilesetIssueSeverity.Warning, Code = code, Message = message, Sheet = sheet, CellIndex = cell, AssetId = assetId };
}

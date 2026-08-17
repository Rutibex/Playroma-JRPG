using System.Drawing;
using System.Drawing.Imaging;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class TilesetConstructionService
{
    public void EnsureCompleteSheets(TileProject project, TileSetDefinition tileSet)
    {
        var safeName = ProjectPath.SanitizeFileName(tileSet.Name);
        foreach (var specification in MvLayoutCatalog.AllSpecifications)
        {
            if (tileSet.Sheets.Any(x => x.Slot == specification.Slot)) continue;
            var fileName = $"{safeName}_{specification.Slot}.png";
            var relative = ProjectPath.ToPortableRelativePath(Path.Combine("Sheets", tileSet.Id.ToString("N"), fileName));
            var path = ProjectPath.Resolve(project, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var bitmap = new Bitmap(specification.Width, specification.Height, PixelFormat.Format32bppArgb))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Transparent);
                BitmapFile.SavePngAtomic(bitmap, path);
            }
            tileSet.Sheets.Add(new TileSheetDefinition
            {
                Slot = specification.Slot,
                DisplayName = specification.Slot.ToString(),
                FileName = fileName,
                RelativeImagePath = relative,
                PixelWidth = specification.Width,
                PixelHeight = specification.Height,
                Regions = MvLayoutCatalog.GetLogicalRegions(specification.Slot)
                    .Select(x => new TileRegionMetadata
                    {
                        Index = x.Index,
                        Name = string.Empty,
                        AlternateName = string.Empty
                    }).ToList()
            });
        }
        tileSet.Sheets = tileSet.Sheets.OrderBy(x => x.Slot).ToList();
    }

    public TileCellMetadata GetOrCreateMetadata(TilesetAssemblyDefinition definition, MvSheetSlot sheet, int cellIndex)
    {
        var existing = definition.Metadata.FirstOrDefault(x => x.Sheet == sheet && x.CellIndex == cellIndex);
        if (existing is not null) return existing;
        var created = new TileCellMetadata { Sheet = sheet, CellIndex = cellIndex };
        definition.Metadata.Add(created);
        return created;
    }

    public void AssignBinding(TilesetAssemblyDefinition definition, MvSheetSlot sheet, int logicalSlot,
        TilesetAssetSourceKind sourceKind, Guid? sourceId, int sourceRevision, string sourceHash,
        string compiledHash, string name, string category)
    {
        definition.Assets.RemoveAll(x => x.Sheet == sheet && x.LogicalSlotIndex == logicalSlot);
        definition.Assets.Add(new TilesetAssetBinding
        {
            Sheet = sheet,
            LogicalSlotIndex = logicalSlot,
            SourceKind = sourceKind,
            SourceAssetId = sourceId,
            SourceRevision = sourceRevision,
            SourceHash = sourceHash,
            CompiledHash = compiledHash,
            Name = name,
            Category = category,
            CompiledUtc = DateTimeOffset.UtcNow
        });
        definition.Revision++;
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
    }

    public void ClearSlot(TilesetAssemblyDefinition definition, Bitmap sheetBitmap, MvSheetSlot sheet, int logicalSlot)
    {
        var region = MvLayoutCatalog.GetLogicalRegions(sheet)[logicalSlot];
        using var graphics = Graphics.FromImage(sheetBitmap);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using var transparent = new SolidBrush(Color.Transparent);
        graphics.FillRectangle(transparent, region.Bounds);
        definition.Assets.RemoveAll(x => x.Sheet == sheet && x.LogicalSlotIndex == logicalSlot);
        definition.Metadata.RemoveAll(x => x.Sheet == sheet && x.CellIndex == logicalSlot);
        definition.Revision++;
    }

    public static bool IsRegionEmpty(Bitmap sheet, MvSheetSlot slot, int logicalSlot)
    {
        var bounds = MvLayoutCatalog.GetLogicalRegions(slot)[logicalSlot].Bounds;
        for (var y = bounds.Top; y < bounds.Bottom; y += Math.Max(1, bounds.Height / 16))
        for (var x = bounds.Left; x < bounds.Right; x += Math.Max(1, bounds.Width / 16))
            if (sheet.GetPixel(x, y).A > 3) return false;
        return true;
    }
}

using System.Drawing;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class TilesetPacker
{
    private static readonly MvSheetSlot[] UpperSheets = { MvSheetSlot.B, MvSheetSlot.C, MvSheetSlot.D, MvSheetSlot.E };

    public PackingPlan CreatePlan(
        TilesetAssemblyDefinition definition,
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheetImages,
        PackingMode? mode = null)
    {
        var plan = new PackingPlan();
        var occupancy = CreateOccupancy(sheetImages);
        var objects = OrderObjects(definition, mode ?? definition.Construction.PackingMode).ToArray();
        foreach (var obj in objects)
        {
            if (obj.IsLocked && TryFindExisting(definition, obj.Id, out var existing))
            {
                plan.Placements.Add(existing);
                continue;
            }

            var placed = false;
            foreach (var sheet in UpperSheets)
            {
                if (!occupancy.TryGetValue(sheet, out var cells)) continue;
                if (TryPlace(cells, 16, 16, obj.GridWidth, obj.GridHeight, sheet == MvSheetSlot.B, out var x, out var y))
                {
                    Mark(cells, 16, obj.GridWidth, obj.GridHeight, x, y, true);
                    plan.Placements.Add(new PackingPlacement
                    {
                        ObjectId = obj.Id,
                        Sheet = sheet,
                        CellX = x,
                        CellY = y,
                        Width = obj.GridWidth,
                        Height = obj.GridHeight
                    });
                    plan.OccupiedCells += obj.GridWidth * obj.GridHeight;
                    placed = true;
                    break;
                }
            }
            if (!placed) plan.UnplacedObjectIds.Add(obj.Id);
        }
        if (plan.UnplacedObjectIds.Count > 0)
            plan.Warnings.Add($"{plan.UnplacedObjectIds.Count} object(s) could not fit in B–E.");
        return plan;
    }

    public void ApplyPlan(
        TilesetAssemblyDefinition definition,
        PackingPlan plan,
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheetImages,
        Func<TileObjectDefinition, Bitmap> objectLoader)
    {
        foreach (var placement in plan.Placements)
        {
            var obj = definition.Objects.FirstOrDefault(x => x.Id == placement.ObjectId);
            if (obj is null || !sheetImages.TryGetValue(placement.Sheet, out var sheet)) continue;
            using var image = objectLoader(obj);
            using var graphics = Graphics.FromImage(sheet);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            var destination = new Rectangle(placement.CellX * 48, placement.CellY * 48,
                placement.Width * 48, placement.Height * 48);
            graphics.DrawImage(image, destination, new Rectangle(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);

            definition.Assets.RemoveAll(x => x.SourceAssetId == obj.Id &&
                x.SourceKind == TilesetAssetSourceKind.ObjectDefinition);
            for (var oy = 0; oy < placement.Height; oy++)
            for (var ox = 0; ox < placement.Width; ox++)
            {
                var cell = (placement.CellY + oy) * 16 + placement.CellX + ox;
                definition.Assets.RemoveAll(x => x.Sheet == placement.Sheet && x.LogicalSlotIndex == cell);
                definition.Assets.Add(new TilesetAssetBinding
                {
                    Sheet = placement.Sheet,
                    LogicalSlotIndex = cell,
                    SourceKind = TilesetAssetSourceKind.ObjectDefinition,
                    SourceAssetId = obj.Id,
                    SourceRevision = 1,
                    Name = placement.Width * placement.Height == 1 ? obj.Name : $"{obj.Name} [{ox + 1},{oy + 1}]",
                    Category = obj.Category.ToString(),
                    IsLocked = obj.IsLocked,
                    CompiledUtc = DateTimeOffset.UtcNow
                });
            }
        }
        definition.Revision++;
        definition.ModifiedUtc = DateTimeOffset.UtcNow;
    }

    private static Dictionary<MvSheetSlot, bool[]> CreateOccupancy(IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets)
    {
        var result = new Dictionary<MvSheetSlot, bool[]>();
        foreach (var slot in UpperSheets)
        {
            var cells = new bool[256];
            if (sheets.TryGetValue(slot, out var bitmap))
            {
                for (var index = 0; index < 256; index++)
                    cells[index] = !IsCellEmpty(bitmap, index % 16, index / 16);
            }
            if (slot == MvSheetSlot.B) cells[0] = true;
            result[slot] = cells;
        }
        return result;
    }

    private static bool IsCellEmpty(Bitmap bitmap, int cellX, int cellY)
    {
        var startX = cellX * 48;
        var startY = cellY * 48;
        for (var y = startY; y < startY + 48; y += 3)
        for (var x = startX; x < startX + 48; x += 3)
            if (bitmap.GetPixel(x, y).A > 4) return false;
        return true;
    }

    private static IEnumerable<TileObjectDefinition> OrderObjects(TilesetAssemblyDefinition definition, PackingMode mode)
    {
        var objects = definition.Objects.Where(x => !string.IsNullOrWhiteSpace(x.RelativeImagePath));
        return mode switch
        {
            PackingMode.Compact => objects.OrderByDescending(x => x.GridWidth * x.GridHeight),
            PackingMode.VariationFamilies => objects.OrderBy(x => x.VariationGroupId).ThenByDescending(x => x.GridHeight).ThenBy(x => x.Name),
            PackingMode.CommercialPresentation => objects.OrderBy(x => x.Category).ThenBy(x => x.Name),
            PackingMode.PreserveExistingLayout => objects.OrderByDescending(x => x.IsLocked).ThenBy(x => x.Category).ThenBy(x => x.Name),
            PackingMode.ManualOnly => Enumerable.Empty<TileObjectDefinition>(),
            _ => objects.OrderBy(x => x.Category).ThenByDescending(x => x.GridHeight).ThenBy(x => x.Name)
        };
    }

    private static bool TryPlace(bool[] cells, int columns, int rows, int width, int height, bool reserveFirst,
        out int resultX, out int resultY)
    {
        for (var y = 0; y <= rows - height; y++)
        for (var x = 0; x <= columns - width; x++)
        {
            if (reserveFirst && x == 0 && y == 0) continue;
            var fits = true;
            for (var oy = 0; oy < height && fits; oy++)
            for (var ox = 0; ox < width; ox++)
                if (cells[(y + oy) * columns + x + ox]) { fits = false; break; }
            if (!fits) continue;
            resultX = x;
            resultY = y;
            return true;
        }
        resultX = resultY = -1;
        return false;
    }

    private static void Mark(bool[] cells, int columns, int width, int height, int x, int y, bool value)
    {
        for (var oy = 0; oy < height; oy++)
        for (var ox = 0; ox < width; ox++) cells[(y + oy) * columns + x + ox] = value;
    }

    private static bool TryFindExisting(TilesetAssemblyDefinition definition, Guid objectId, out PackingPlacement placement)
    {
        var cells = definition.Assets.Where(x => x.SourceAssetId == objectId && x.SourceKind == TilesetAssetSourceKind.ObjectDefinition).ToArray();
        if (cells.Length == 0)
        {
            placement = new PackingPlacement();
            return false;
        }
        var sheet = cells[0].Sheet;
        var xValues = cells.Select(x => x.LogicalSlotIndex % 16).ToArray();
        var yValues = cells.Select(x => x.LogicalSlotIndex / 16).ToArray();
        placement = new PackingPlacement
        {
            ObjectId = objectId,
            Sheet = sheet,
            CellX = xValues.Min(),
            CellY = yValues.Min(),
            Width = xValues.Max() - xValues.Min() + 1,
            Height = yValues.Max() - yValues.Min() + 1
        };
        return true;
    }
}

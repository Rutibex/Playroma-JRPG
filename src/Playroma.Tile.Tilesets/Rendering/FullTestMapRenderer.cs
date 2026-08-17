using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Services;

namespace Playroma.Tile.Tilesets.Rendering;

public sealed class FullTestMapRenderer
{
    public Bitmap Render(
        TestMapDefinition map,
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets,
        TilesetAssemblyDefinition definition,
        bool drawGrid = false,
        bool drawPlayer = true,
        bool showCollision = false)
    {
        var output = new Bitmap(map.Width * 48, map.Height * 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(30, 30, 30));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        foreach (var layer in new[] { TestMapLayer.Lower1, TestMapLayer.Lower2 })
            DrawLayer(graphics, map, layer, sheets, definition, aboveOnly: false);
        DrawLayer(graphics, map, TestMapLayer.Upper1, sheets, definition, aboveOnly: false);
        DrawLayer(graphics, map, TestMapLayer.Upper2, sheets, definition, aboveOnly: false);
        if (drawPlayer) DrawPlayer(graphics, map.PlayerX, map.PlayerY);
        DrawLayer(graphics, map, TestMapLayer.Upper1, sheets, definition, aboveOnly: true);
        DrawLayer(graphics, map, TestMapLayer.Upper2, sheets, definition, aboveOnly: true);
        if (showCollision) DrawCollision(graphics, map, definition);
        if (drawGrid)
        {
            using var pen = new Pen(Color.FromArgb(80, Color.White));
            for (var x = 0; x <= map.Width; x++) graphics.DrawLine(pen, x * 48, 0, x * 48, output.Height);
            for (var y = 0; y <= map.Height; y++) graphics.DrawLine(pen, 0, y * 48, output.Width, y * 48);
        }
        return output;
    }

    public bool TryMovePlayer(TestMapDefinition map, TilesetAssemblyDefinition definition, int dx, int dy)
    {
        var targetX = map.PlayerX + dx;
        var targetY = map.PlayerY + dy;
        if (targetX < 0 || targetY < 0 || targetX >= map.Width || targetY >= map.Height) return false;
        var direction = dx switch
        {
            > 0 => DirectionPassageFlags.Right,
            < 0 => DirectionPassageFlags.Left,
            _ => dy > 0 ? DirectionPassageFlags.Down : DirectionPassageFlags.Up
        };
        var opposite = direction switch
        {
            DirectionPassageFlags.Down => DirectionPassageFlags.Up,
            DirectionPassageFlags.Up => DirectionPassageFlags.Down,
            DirectionPassageFlags.Left => DirectionPassageFlags.Right,
            _ => DirectionPassageFlags.Left
        };
        if (!CanEnter(map, definition, targetX, targetY, opposite)) return false;
        if (!CanLeave(map, definition, map.PlayerX, map.PlayerY, direction)) return false;
        map.PlayerX = targetX;
        map.PlayerY = targetY;
        return true;
    }

    public bool IsDamageFloor(TestMapDefinition map, TilesetAssemblyDefinition definition, int x, int y)
        => EnumerateCellMetadata(map, definition, x, y).Any(x => x.DamageFloor);

    private static void DrawLayer(Graphics graphics, TestMapDefinition map, TestMapLayer layer,
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets, TilesetAssemblyDefinition definition, bool aboveOnly)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var address = map.Get(layer, x, y);
            if (!TileAddressCodec.TryDecode(address, out var sheet, out var index)) continue;
            var metadata = GetMetadata(definition, sheet, index);
            var above = metadata?.Passage == PassageMode.AboveCharacters;
            if (above != aboveOnly) continue;
            if (!sheets.TryGetValue(sheet, out var bitmap)) continue;
            if (sheet is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4)
            {
                using var tile = RenderAutotile(map, layer, x, y, address, sheet, index, bitmap);
                graphics.DrawImageUnscaled(tile, x * 48, y * 48);
            }
            else
            {
                var source = ResolveSourceRectangle(sheet, index, bitmap);
                if (source.Width <= 0 || source.Height <= 0) continue;
                graphics.DrawImage(bitmap, new Rectangle(x * 48, y * 48, 48, 48), source, GraphicsUnit.Pixel);
            }
        }
    }

    private static Rectangle ResolveSourceRectangle(MvSheetSlot sheet, int index, Bitmap bitmap)
    {
        var columns = bitmap.Width / 48;
        if (columns <= 0 || index < 0 || index >= columns * (bitmap.Height / 48)) return Rectangle.Empty;
        return new Rectangle(index % columns * 48, index / columns * 48, 48, 48);
    }

    private static Bitmap RenderAutotile(TestMapDefinition map, TestMapLayer layer, int x, int y, int address,
        MvSheetSlot sheet, int index, Bitmap sheetBitmap)
    {
        var regions = MvLayoutCatalog.GetLogicalRegions(sheet);
        if (index < 0 || index >= regions.Count) return new Bitmap(48, 48, PixelFormat.Format32bppArgb);
        var bounds = regions[index].Bounds;
        using var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var sourceGraphics = Graphics.FromImage(source))
        {
            sourceGraphics.CompositingMode = CompositingMode.SourceCopy;
            sourceGraphics.DrawImage(sheetBitmap, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
        }
        var kind = AutotileKindInfo.InferKind(sheet, index);
        var mask = NeighborMask.None;
        if (map.Get(layer, x - 1, y - 1) == address) mask |= NeighborMask.NorthWest;
        if (map.Get(layer, x, y - 1) == address) mask |= NeighborMask.North;
        if (map.Get(layer, x + 1, y - 1) == address) mask |= NeighborMask.NorthEast;
        if (map.Get(layer, x - 1, y) == address) mask |= NeighborMask.West;
        if (map.Get(layer, x + 1, y) == address) mask |= NeighborMask.East;
        if (map.Get(layer, x - 1, y + 1) == address) mask |= NeighborMask.SouthWest;
        if (map.Get(layer, x, y + 1) == address) mask |= NeighborMask.South;
        if (map.Get(layer, x + 1, y + 1) == address) mask |= NeighborMask.SouthEast;
        var family = AutotileKindInfo.GetFamily(kind);
        var shape = new AutotileShapeResolver().Resolve(family, mask);
        return new MvAutotileRenderer().RenderShape(source, kind, shape);
    }

    private static bool CanLeave(TestMapDefinition map, TilesetAssemblyDefinition definition, int x, int y,
        DirectionPassageFlags direction)
    {
        foreach (var metadata in EnumerateCellMetadata(map, definition, x, y))
        {
            if (metadata.Passage == PassageMode.Impassable) return false;
            if (metadata.Passage == PassageMode.Directional && metadata.BlockedDirections.HasFlag(direction)) return false;
        }
        return true;
    }

    private static bool CanEnter(TestMapDefinition map, TilesetAssemblyDefinition definition, int x, int y,
        DirectionPassageFlags direction)
    {
        foreach (var metadata in EnumerateCellMetadata(map, definition, x, y))
        {
            if (metadata.Passage == PassageMode.Impassable) return false;
            if (metadata.Passage == PassageMode.Directional && metadata.BlockedDirections.HasFlag(direction)) return false;
        }
        return true;
    }

    private static IEnumerable<TileGameplayMetadata> EnumerateCellMetadata(TestMapDefinition map,
        TilesetAssemblyDefinition definition, int x, int y)
    {
        foreach (var layer in new[] { TestMapLayer.Lower1, TestMapLayer.Lower2, TestMapLayer.Upper1, TestMapLayer.Upper2 })
        {
            var address = map.Get(layer, x, y);
            if (!TileAddressCodec.TryDecode(address, out var sheet, out var index)) continue;
            var metadata = GetMetadata(definition, sheet, index);
            if (metadata is not null) yield return metadata;
        }
    }

    private static TileGameplayMetadata? GetMetadata(TilesetAssemblyDefinition definition, MvSheetSlot sheet, int index)
        => definition.Metadata.FirstOrDefault(x => x.Sheet == sheet && x.CellIndex == index)?.Metadata;

    private static void DrawPlayer(Graphics graphics, int x, int y)
    {
        var bounds = new Rectangle(x * 48 + 10, y * 48 + 7, 28, 38);
        using var shadow = new SolidBrush(Color.FromArgb(110, Color.Black));
        using var body = new SolidBrush(Color.CornflowerBlue);
        using var outline = new Pen(Color.White, 2f);
        graphics.FillEllipse(shadow, bounds.X + 2, bounds.Bottom - 8, bounds.Width - 4, 8);
        graphics.FillEllipse(body, bounds.X + 4, bounds.Y, bounds.Width - 8, 20);
        graphics.FillRectangle(body, bounds.X + 6, bounds.Y + 14, bounds.Width - 12, bounds.Height - 16);
        graphics.DrawEllipse(outline, bounds.X + 4, bounds.Y, bounds.Width - 8, 20);
        graphics.DrawRectangle(outline, bounds.X + 6, bounds.Y + 14, bounds.Width - 12, bounds.Height - 16);
    }

    private static void DrawCollision(Graphics graphics, TestMapDefinition map, TilesetAssemblyDefinition definition)
    {
        using var blocked = new SolidBrush(Color.FromArgb(85, Color.Red));
        using var damage = new SolidBrush(Color.FromArgb(85, Color.Orange));
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var metadata = EnumerateCellMetadata(map, definition, x, y).ToArray();
            if (metadata.Any(m => m.Passage == PassageMode.Impassable))
                graphics.FillRectangle(blocked, x * 48, y * 48, 48, 48);
            if (metadata.Any(m => m.DamageFloor))
                graphics.FillRectangle(damage, x * 48, y * 48, 48, 48);
        }
    }
}

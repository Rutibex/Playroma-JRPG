using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Services;

namespace Playroma.Tile.Tilesets.Rendering;

public sealed class ShowcaseSceneBuilder
{
    public TestMapDefinition Build(TilesetAssemblyDefinition definition, int width = 24, int height = 18)
    {
        var map = TestMapDefinition.CreateDefault(width, height);
        map.Name = "Standardized Tileset Showcase";
        var palette = definition.Assets
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Sheet)
            .ThenBy(x => x.LogicalSlotIndex)
            .ToArray();
        if (palette.Length == 0) return map;
        var lower = palette.Where(x => x.Sheet is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A5).ToArray();
        var walls = palette.Where(x => x.Sheet is MvSheetSlot.A3 or MvSheetSlot.A4).ToArray();
        var objects = palette.Where(x => x.Sheet is MvSheetSlot.B or MvSheetSlot.C or MvSheetSlot.D or MvSheetSlot.E).ToArray();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (lower.Length > 0)
            {
                var item = lower[((y / 3) * 5 + x / 4) % lower.Length];
                map.Set(TestMapLayer.Lower1, x, y, TileAddressCodec.Encode(item.Sheet, item.LogicalSlotIndex));
            }
        }
        if (walls.Length > 0)
        {
            for (var x = 2; x < width - 2; x++)
            {
                var item = walls[(x - 2) % walls.Length];
                map.Set(TestMapLayer.Lower2, x, 3, TileAddressCodec.Encode(item.Sheet, item.LogicalSlotIndex));
                if (x % 3 != 0) map.Set(TestMapLayer.Lower2, x, 4, TileAddressCodec.Encode(item.Sheet, item.LogicalSlotIndex));
            }
        }
        for (var i = 0; i < objects.Length && i < (width - 2) * 2; i++)
        {
            var x = 1 + i % (width - 2);
            var y = height - 3 + i / (width - 2);
            var item = objects[i];
            map.Set(TestMapLayer.Upper1, x, y, TileAddressCodec.Encode(item.Sheet, item.LogicalSlotIndex));
        }
        map.PlayerX = 2;
        map.PlayerY = height / 2;
        return map;
    }

    public ShowcasePackage Export(string directory, TestMapDefinition map, Bitmap beauty,
        TilesetAssemblyDefinition definition)
    {
        Directory.CreateDirectory(directory);
        var beautyPath = Path.Combine(directory, "showcase_beauty.png");
        beauty.Save(beautyPath, ImageFormat.Png);
        var gridPath = Path.Combine(directory, "showcase_grid.png");
        using (var grid = (Bitmap)beauty.Clone())
        using (var graphics = Graphics.FromImage(grid))
        using (var pen = new Pen(Color.FromArgb(130, Color.White)))
        {
            for (var x = 0; x <= map.Width; x++) graphics.DrawLine(pen, x * 48, 0, x * 48, grid.Height);
            for (var y = 0; y <= map.Height; y++) graphics.DrawLine(pen, 0, y * 48, grid.Width, y * 48);
            grid.Save(gridPath, ImageFormat.Png);
        }
        var labelsPath = Path.Combine(directory, "showcase_labels.png");
        using (var labels = new Bitmap(map.Width * 48, map.Height * 48, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(labels))
        using (var font = new Font(FontFamily.GenericSansSerif, 7f, FontStyle.Bold))
        using (var background = new SolidBrush(Color.FromArgb(210, 0, 0, 0)))
        using (var foreground = new SolidBrush(Color.White))
        {
            graphics.Clear(Color.Transparent);
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var address = map.Get(TestMapLayer.Upper1, x, y);
                if (address < 0) address = map.Get(TestMapLayer.Lower2, x, y);
                if (address < 0) address = map.Get(TestMapLayer.Lower1, x, y);
                if (!TileAddressCodec.TryDecode(address, out var sheet, out var index)) continue;
                var name = definition.Assets.FirstOrDefault(a => a.Sheet == sheet && a.LogicalSlotIndex == index)?.Name ?? $"{sheet}{index + 1}";
                var text = name.Length > 10 ? name[..10] : name;
                graphics.FillRectangle(background, x * 48 + 1, y * 48 + 32, 46, 15);
                graphics.DrawString(text, font, foreground, x * 48 + 2, y * 48 + 33);
            }
            labels.Save(labelsPath, ImageFormat.Png);
        }
        var semanticPath = Path.Combine(directory, "showcase_semantic_regions.png");
        using (var semantic = new Bitmap(map.Width * 48, map.Height * 48, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(semantic))
        {
            graphics.Clear(Color.Black);
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var address = map.Get(TestMapLayer.Upper1, x, y);
                if (address < 0) address = map.Get(TestMapLayer.Lower2, x, y);
                if (address < 0) address = map.Get(TestMapLayer.Lower1, x, y);
                var color = AddressColor(address);
                using var brush = new SolidBrush(color);
                graphics.FillRectangle(brush, x * 48, y * 48, 48, 48);
            }
            semantic.Save(semanticPath, ImageFormat.Png);
        }
        var objectIdsPath = Path.Combine(directory, "showcase_object_ids.png");
        File.Copy(semanticPath, objectIdsPath, true);
        var manifestPath = Path.Combine(directory, "showcase_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            version = 1,
            tileSize = 48,
            width = map.Width,
            height = map.Height,
            layers = map.Layers,
            assets = definition.Assets.Select(x => new { x.Sheet, x.LogicalSlotIndex, x.Name, x.Category })
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new ShowcasePackage
        {
            BeautyPath = beautyPath,
            GridPath = gridPath,
            LabelsPath = labelsPath,
            SemanticPath = semanticPath,
            ObjectIdsPath = objectIdsPath,
            ManifestPath = manifestPath
        };
    }

    private static Color AddressColor(int address)
    {
        unchecked
        {
            var hash = address * 1103515245 + 12345;
            return Color.FromArgb(255, 40 + Math.Abs(hash & 0x7F), 40 + Math.Abs((hash >> 8) & 0x7F), 40 + Math.Abs((hash >> 16) & 0x7F));
        }
    }
}

public sealed class ShowcasePackage
{
    public string BeautyPath { get; set; } = string.Empty;
    public string GridPath { get; set; } = string.Empty;
    public string LabelsPath { get; set; } = string.Empty;
    public string SemanticPath { get; set; } = string.Empty;
    public string ObjectIdsPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
}

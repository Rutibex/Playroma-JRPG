using System.Collections.ObjectModel;
using System.Drawing;
using Playroma.Tile.Core.Models;

namespace Playroma.Tile.RpgMakerMV.Formats;

public static class MvLayoutCatalog
{
    private static readonly IReadOnlyDictionary<MvSheetSlot, MvSheetSpecification> Specifications =
        new ReadOnlyDictionary<MvSheetSlot, MvSheetSpecification>(
            new Dictionary<MvSheetSlot, MvSheetSpecification>
            {
                [MvSheetSlot.A1] = new(MvSheetSlot.A1, 768, 576, 16, "Animated water and waterfall autotiles", true),
                [MvSheetSlot.A2] = new(MvSheetSlot.A2, 768, 576, 32, "Ground and floor autotiles", true),
                [MvSheetSlot.A3] = new(MvSheetSlot.A3, 768, 384, 32, "Building exterior and roof autotiles", true),
                [MvSheetSlot.A4] = new(MvSheetSlot.A4, 768, 720, 48, "Wall, cliff and dungeon autotiles", true),
                [MvSheetSlot.A5] = new(MvSheetSlot.A5, 384, 768, 128, "Ordinary lower-layer tiles", false),
                [MvSheetSlot.B] = new(MvSheetSlot.B, 768, 768, 256, "Ordinary upper-layer tiles", false, true),
                [MvSheetSlot.C] = new(MvSheetSlot.C, 768, 768, 256, "Ordinary upper-layer tiles", false),
                [MvSheetSlot.D] = new(MvSheetSlot.D, 768, 768, 256, "Ordinary upper-layer tiles", false),
                [MvSheetSlot.E] = new(MvSheetSlot.E, 768, 768, 256, "Ordinary upper-layer tiles", false)
            });

    public static IEnumerable<MvSheetSpecification> AllSpecifications => Specifications.Values;

    public static MvSheetSpecification GetSpecification(MvSheetSlot slot) => Specifications[slot];

    public static IReadOnlyList<MvRegionDefinition> GetLogicalRegions(MvSheetSlot slot)
    {
        return slot switch
        {
            MvSheetSlot.A1 => CreateA1Regions(),
            MvSheetSlot.A2 => CreateUniformRegions(8, 4, 96, 144, "Ground autotile"),
            MvSheetSlot.A3 => CreateUniformRegions(8, 4, 96, 96, "Building autotile"),
            MvSheetSlot.A4 => CreateA4Regions(),
            MvSheetSlot.A5 => CreateUniformRegions(8, 16, 48, 48, "Lower-layer tile"),
            MvSheetSlot.B => CreateUniformRegions(16, 16, 48, 48, "Upper-layer tile"),
            MvSheetSlot.C => CreateUniformRegions(16, 16, 48, 48, "Upper-layer tile"),
            MvSheetSlot.D => CreateUniformRegions(16, 16, 48, 48, "Upper-layer tile"),
            MvSheetSlot.E => CreateUniformRegions(16, 16, 48, 48, "Upper-layer tile"),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
        };
    }

    public static IReadOnlyList<MvRegionDefinition> GetRawTileCells(MvSheetSlot slot)
    {
        var specification = GetSpecification(slot);
        return CreateUniformRegions(
            specification.Width / MvSheetSpecification.TileSize,
            specification.Height / MvSheetSpecification.TileSize,
            MvSheetSpecification.TileSize,
            MvSheetSpecification.TileSize,
            "48 px source cell");
    }

    private static IReadOnlyList<MvRegionDefinition> CreateA1Regions()
    {
        // RPG Maker MV A1 is not a uniform grid of logical assets. The order below
        // follows the engine's 16 A1 catalogue slots: A, B, two C blocks, then D/E pairs.
        var rectangles = new[]
        {
            new Rectangle(0, 0, 288, 144),
            new Rectangle(0, 144, 288, 144),
            new Rectangle(288, 0, 96, 144),
            new Rectangle(288, 144, 96, 144),
            new Rectangle(384, 0, 288, 144),
            new Rectangle(672, 0, 96, 144),
            new Rectangle(384, 144, 288, 144),
            new Rectangle(672, 144, 96, 144),
            new Rectangle(0, 288, 288, 144),
            new Rectangle(288, 288, 96, 144),
            new Rectangle(384, 288, 288, 144),
            new Rectangle(672, 288, 96, 144),
            new Rectangle(0, 432, 288, 144),
            new Rectangle(288, 432, 96, 144),
            new Rectangle(384, 432, 288, 144),
            new Rectangle(672, 432, 96, 144)
        };

        var kinds = new[]
        {
            "Ocean A (3-frame)",
            "Deep ocean B (3-frame)",
            "Ocean decoration C",
            "Ocean decoration C",
            "Water D (3-frame)",
            "Waterfall E (3-frame)",
            "Water D (3-frame)",
            "Waterfall E (3-frame)",
            "Water D (3-frame)",
            "Waterfall E (3-frame)",
            "Water D (3-frame)",
            "Waterfall E (3-frame)",
            "Water D (3-frame)",
            "Waterfall E (3-frame)",
            "Water D (3-frame)",
            "Waterfall E (3-frame)"
        };

        return rectangles
            .Select((bounds, index) => new MvRegionDefinition(index, bounds, kinds[index], $"A1 {index + 1:00}"))
            .ToArray();
    }

    private static IReadOnlyList<MvRegionDefinition> CreateA4Regions()
    {
        // A4 alternates a six-cell basic autotile row (144 px high) with a
        // group-pattern wall row (96 px high), repeated three times.
        var rowHeights = new[] { 144, 96, 144, 96, 144, 96 };
        var result = new List<MvRegionDefinition>(48);
        var index = 0;
        var y = 0;

        for (var row = 0; row < rowHeights.Length; row++)
        {
            var height = rowHeights[row];
            var kind = height == 144 ? "A4 basic autotile" : "A4 group-pattern wall";
            for (var column = 0; column < 8; column++)
            {
                result.Add(new MvRegionDefinition(
                    index,
                    new Rectangle(column * 96, y, 96, height),
                    kind,
                    $"A4 {index + 1:00}"));
                index++;
            }

            y += height;
        }

        return result;
    }

    private static IReadOnlyList<MvRegionDefinition> CreateUniformRegions(
        int columns,
        int rows,
        int width,
        int height,
        string kind)
    {
        var result = new List<MvRegionDefinition>(columns * rows);
        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                result.Add(new MvRegionDefinition(
                    index,
                    new Rectangle(column * width, row * height, width, height),
                    kind,
                    $"Tile {index + 1:000}"));
                index++;
            }
        }

        return result;
    }
}

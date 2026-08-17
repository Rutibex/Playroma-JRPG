using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class ConstructionTemplateCatalog
{
    private readonly Dictionary<TilesetTemplateKind, ConstructionTemplate> _templates;

    public ConstructionTemplateCatalog()
    {
        _templates = Enum.GetValues<TilesetTemplateKind>()
            .Where(x => x != TilesetTemplateKind.Custom)
            .ToDictionary(x => x, CreateTemplate);
    }

    public IReadOnlyCollection<ConstructionTemplate> All => _templates.Values;

    public ConstructionTemplate Get(TilesetTemplateKind kind)
        => kind == TilesetTemplateKind.Custom ? CreateCustom() : _templates[kind];

    private static ConstructionTemplate CreateTemplate(TilesetTemplateKind kind)
    {
        var template = new ConstructionTemplate
        {
            Id = kind.ToString().ToLowerInvariant(),
            Name = SplitName(kind.ToString()),
            Kind = kind,
            Description = $"Coverage profile for a complete {SplitName(kind.ToString()).ToLowerInvariant()} RPG Maker MV tileset."
        };
        AddUniversal(template);
        switch (kind)
        {
            case TilesetTemplateKind.Outdoor:
            case TilesetTemplateKind.Village:
            case TilesetTemplateKind.Winter:
            case TilesetTemplateKind.Desert:
            case TilesetTemplateKind.Swamp:
                Add(template, "water", "Water", "terrain", RequirementLevel.Required, MvSheetSlot.A1, "water", "river", "ocean");
                Add(template, "waterfall", "Waterfall", "terrain", RequirementLevel.Recommended, MvSheetSlot.A1, "waterfall");
                Add(template, "ground", kind == TilesetTemplateKind.Desert ? "Sand" : "Primary Ground", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "ground", "grass", "sand", "snow", "swamp");
                Add(template, "path", "Path or Road", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "path", "road", "dirt");
                Add(template, "cliff", "Cliff or Exterior Wall", "structure", RequirementLevel.Required, MvSheetSlot.A4, "cliff", "wall");
                Add(template, "building", "Building Exterior", "structure", RequirementLevel.Required, MvSheetSlot.A3, "building", "roof");
                Add(template, "tree", "Trees", "vegetation", RequirementLevel.Required, MvSheetSlot.B, "tree", "vegetation");
                Add(template, "rocks", "Rocks", "decoration", RequirementLevel.Recommended, MvSheetSlot.B, "rock", "stone");
                Add(template, "fence", "Fence with Corners", "structure", RequirementLevel.Recommended, MvSheetSlot.C, "fence");
                Add(template, "bridge", "Bridge", "structure", RequirementLevel.Recommended, MvSheetSlot.C, "bridge");
                break;
            case TilesetTemplateKind.Interior:
            case TilesetTemplateKind.Castle:
                Add(template, "floor", "Interior Floor", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "floor", "carpet", "wood", "stone");
                Add(template, "wall", "Interior Wall", "structure", RequirementLevel.Required, MvSheetSlot.A4, "wall");
                Add(template, "door", "Door", "structure", RequirementLevel.Required, MvSheetSlot.B, "door");
                Add(template, "window", "Window", "structure", RequirementLevel.Recommended, MvSheetSlot.B, "window");
                Add(template, "table", "Tables and Counters", "furniture", RequirementLevel.Required, MvSheetSlot.C, "table", "counter");
                Add(template, "seating", "Chairs or Seating", "furniture", RequirementLevel.Required, MvSheetSlot.C, "chair", "seat");
                Add(template, "storage", "Shelves and Storage", "furniture", RequirementLevel.Recommended, MvSheetSlot.D, "shelf", "chest", "storage");
                break;
            case TilesetTemplateKind.Dungeon:
            case TilesetTemplateKind.Cave:
            case TilesetTemplateKind.Horror:
                Add(template, "floor", "Dungeon Floor", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "floor", "stone", "cave");
                Add(template, "wall", "Dungeon Wall", "structure", RequirementLevel.Required, MvSheetSlot.A4, "wall", "cliff", "cave");
                Add(template, "pit", "Pit or Chasm", "hazard", RequirementLevel.Recommended, MvSheetSlot.A2, "pit", "chasm");
                Add(template, "door", "Dungeon Door", "structure", RequirementLevel.Required, MvSheetSlot.B, "door", "gate");
                Add(template, "stairs", "Stairs", "structure", RequirementLevel.Required, MvSheetSlot.B, "stairs", "ladder");
                Add(template, "hazard", "Visible Hazard", "hazard", RequirementLevel.Recommended, MvSheetSlot.C, "trap", "damage", "hazard");
                Add(template, "debris", "Debris and Damage", "decoration", RequirementLevel.Recommended, MvSheetSlot.C, "debris", "crack", "broken");
                break;
            case TilesetTemplateKind.ScienceFiction:
            case TilesetTemplateKind.Laboratory:
            case TilesetTemplateKind.Ship:
                Add(template, "floor", "Technology Floor", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "panel", "metal", "floor");
                Add(template, "wall", "Technology Wall", "structure", RequirementLevel.Required, MvSheetSlot.A4, "wall", "panel");
                Add(template, "door", "Powered Door", "structure", RequirementLevel.Required, MvSheetSlot.B, "door", "airlock");
                Add(template, "console", "Console", "machinery", RequirementLevel.Required, MvSheetSlot.C, "console", "terminal");
                Add(template, "machine", "Machinery", "machinery", RequirementLevel.Recommended, MvSheetSlot.C, "machine", "equipment");
                Add(template, "light", "Lighting", "decoration", RequirementLevel.Recommended, MvSheetSlot.D, "light", "lamp");
                break;
            case TilesetTemplateKind.ModernCity:
                Add(template, "road", "Road", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "road", "asphalt");
                Add(template, "sidewalk", "Sidewalk", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "sidewalk", "pavement");
                Add(template, "building", "Building Exterior", "structure", RequirementLevel.Required, MvSheetSlot.A3, "building", "roof");
                Add(template, "door", "Door", "structure", RequirementLevel.Required, MvSheetSlot.B, "door");
                Add(template, "vehicle", "Vehicle", "object", RequirementLevel.Recommended, MvSheetSlot.C, "vehicle", "car");
                Add(template, "street", "Street Furniture", "decoration", RequirementLevel.Recommended, MvSheetSlot.D, "sign", "lamp", "bench");
                break;
            case TilesetTemplateKind.WorldMap:
                Add(template, "ocean", "Ocean", "terrain", RequirementLevel.Required, MvSheetSlot.A1, "ocean", "water");
                Add(template, "land", "Land Biomes", "terrain", RequirementLevel.Required, MvSheetSlot.A2, "land", "grass", "desert", "snow");
                Add(template, "mountain", "Mountains", "terrain", RequirementLevel.Required, MvSheetSlot.B, "mountain");
                Add(template, "forest", "Forests", "terrain", RequirementLevel.Required, MvSheetSlot.B, "forest");
                Add(template, "settlement", "Settlements", "structure", RequirementLevel.Required, MvSheetSlot.C, "town", "city", "castle");
                Add(template, "road", "Roads", "terrain", RequirementLevel.Recommended, MvSheetSlot.A2, "road");
                break;
        }
        return template;
    }

    private static void AddUniversal(ConstructionTemplate template)
    {
        Add(template, "quiet", "Quiet Base Tile", "production", RequirementLevel.Required, MvSheetSlot.A5, "quiet", "base", "plain");
        Add(template, "shadow", "Object Shadow", "production", RequirementLevel.Optional, MvSheetSlot.B, "shadow");
        Add(template, "variation", "Visual Variations", "production", RequirementLevel.Recommended, null, new[] { "variation" }, 3);
    }

    private static void Add(ConstructionTemplate template, string id, string name, string category,
        RequirementLevel level, MvSheetSlot? sheet, params string[] tags)
        => Add(template, id, name, category, level, sheet, tags, 1);

    private static void Add(ConstructionTemplate template, string id, string name, string category,
        RequirementLevel level, MvSheetSlot? sheet, string[] tags, int minimum)
        => template.Requirements.Add(new ConstructionRequirement
        {
            Id = id,
            Name = name,
            Category = category,
            Level = level,
            PreferredSheet = sheet,
            MinimumCount = minimum,
            MatchTags = tags.ToList()
        });

    private static ConstructionTemplate CreateCustom() => new()
    {
        Id = "custom",
        Name = "Custom",
        Kind = TilesetTemplateKind.Custom,
        Description = "A user-defined construction profile."
    };

    private static string SplitName(string text)
        => string.Concat(text.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}

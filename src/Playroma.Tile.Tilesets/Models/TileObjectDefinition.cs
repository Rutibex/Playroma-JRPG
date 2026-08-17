using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Tilesets.Models;

public sealed class TileObjectDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Object";
    public TileObjectCategory Category { get; set; }
    public int GridWidth { get; set; } = 1;
    public int GridHeight { get; set; } = 1;
    public int AnchorCellX { get; set; }
    public int AnchorCellY { get; set; }
    public int OccupiedPixelX { get; set; }
    public int OccupiedPixelY { get; set; }
    public int OccupiedPixelWidth { get; set; } = 48;
    public int OccupiedPixelHeight { get; set; } = 48;
    public int CollisionCellX { get; set; }
    public int CollisionCellY { get; set; }
    public int CollisionCellWidth { get; set; } = 1;
    public int CollisionCellHeight { get; set; } = 1;
    public string RelativeImagePath { get; set; } = string.Empty;
    public string RelativeShadowPath { get; set; } = string.Empty;
    public Guid? SourceMaterialId { get; set; }
    public Guid? StyleProfileId { get; set; }
    public Guid? VariationGroupId { get; set; }
    public List<TileObjectFrame> Frames { get; set; } = new();
    public TileGameplayMetadata Metadata { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
}

public sealed class TileObjectFrame
{
    public string Name { get; set; } = "Default";
    public string RelativeImagePath { get; set; } = string.Empty;
    public int DurationMilliseconds { get; set; } = 250;
}

public sealed class TileVariationGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Variations";
    public VariationSelectionMode SelectionMode { get; set; } = VariationSelectionMode.EqualRandom;
    public List<TileVariationEntry> Entries { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class TileVariationEntry
{
    public Guid ObjectId { get; set; }
    public float Weight { get; set; } = 1f;
    public string Season { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
}

public sealed class TileGameplayMetadata
{
    public PassageMode Passage { get; set; } = PassageMode.Passable;
    public DirectionPassageFlags BlockedDirections { get; set; } = DirectionPassageFlags.None;
    public bool Ladder { get; set; }
    public bool Bush { get; set; }
    public bool Counter { get; set; }
    public bool DamageFloor { get; set; }
    public int TerrainTag { get; set; }
    public string SemanticType { get; set; } = string.Empty;
    public string FootstepType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public TileGameplayMetadata Clone() => new()
    {
        Passage = Passage,
        BlockedDirections = BlockedDirections,
        Ladder = Ladder,
        Bush = Bush,
        Counter = Counter,
        DamageFloor = DamageFloor,
        TerrainTag = TerrainTag,
        SemanticType = SemanticType,
        FootstepType = FootstepType,
        Notes = Notes
    };
}

public sealed class TileCellMetadata
{
    public MvSheetSlot Sheet { get; set; }
    public int CellIndex { get; set; }
    public TileGameplayMetadata Metadata { get; set; } = new();
}

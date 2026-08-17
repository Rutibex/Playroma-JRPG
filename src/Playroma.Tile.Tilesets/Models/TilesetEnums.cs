namespace Playroma.Tile.Tilesets.Models;

public enum TilesetAssetSourceKind
{
    ImportedImage,
    MaterialRecipe,
    AutotileDefinition,
    ObjectDefinition,
    ManualArtwork,
    GeneratedVariation,
    LinkedExternalFile
}

public enum TileObjectCategory
{
    Uncategorized,
    GroundDecoration,
    Vegetation,
    Rock,
    Structure,
    Furniture,
    Container,
    Door,
    Window,
    Fence,
    Bridge,
    Sign,
    Lighting,
    Machinery,
    Effect,
    Shadow,
    WorldMap,
    UserDefined
}

public enum PassageMode
{
    Passable,
    Impassable,
    Directional,
    AboveCharacters
}

[Flags]
public enum DirectionPassageFlags
{
    None = 0,
    Down = 1 << 0,
    Left = 1 << 1,
    Right = 1 << 2,
    Up = 1 << 3,
    All = Down | Left | Right | Up
}

public enum VariationSelectionMode
{
    EqualRandom,
    Weighted,
    Seasonal,
    ConditionBased,
    Manual,
    AdjacencyAware,
    BiomeDependent
}

public enum PackingMode
{
    Compact,
    CategoryGroups,
    VariationFamilies,
    PreserveExistingLayout,
    CommercialPresentation,
    ManualOnly
}

public enum TilesetTemplateKind
{
    Outdoor,
    Interior,
    Dungeon,
    WorldMap,
    ScienceFiction,
    ModernCity,
    Horror,
    Winter,
    Desert,
    Swamp,
    Ship,
    Castle,
    Village,
    Cave,
    Laboratory,
    Custom
}

public enum RequirementLevel
{
    Required,
    Recommended,
    Optional
}

public enum TilesetAssetStatus
{
    Current,
    SourceChanged,
    MissingSource,
    CompileFailed,
    ExportOutdated,
    ExternallyModified,
    Locked,
    Detached
}

public enum TilesetIssueSeverity
{
    Information,
    Warning,
    Error
}

public enum TilesetExportKind
{
    PngSheetsOnly,
    RpgMakerAssetPack,
    PlayromaEditablePackage,
    MarketplacePackage,
    AiScenePackage
}

public enum MetadataBrushKind
{
    Passage,
    DirectionalPassage,
    AboveCharacters,
    Ladder,
    Bush,
    Counter,
    DamageFloor,
    TerrainTag,
    SemanticType,
    FootstepType
}

public enum TestMapLayer
{
    Lower1,
    Lower2,
    Upper1,
    Upper2,
    Shadow,
    Region
}

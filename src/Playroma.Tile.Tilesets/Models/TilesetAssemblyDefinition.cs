using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Tilesets.Models;

public sealed class TilesetAssemblyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TileSetId { get; set; }
    public string Name { get; set; } = "Tileset Construction";
    public string Theme { get; set; } = "Custom";
    public TilesetTemplateKind TemplateKind { get; set; } = TilesetTemplateKind.Custom;
    public Guid? StyleProfileId { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TilesetAssetBinding> Assets { get; set; } = new();
    public List<TileObjectDefinition> Objects { get; set; } = new();
    public List<TileVariationGroup> VariationGroups { get; set; } = new();
    public List<TileCellMetadata> Metadata { get; set; } = new();
    public TilesetConstructionSettings Construction { get; set; } = new();
    public TilesetExportSettings Export { get; set; } = new();
    public List<TilesetSnapshotRecord> Snapshots { get; set; } = new();
    public TestMapDefinition TestMap { get; set; } = TestMapDefinition.CreateDefault();
}

public sealed class TilesetAssetBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MvSheetSlot Sheet { get; set; }
    public int LogicalSlotIndex { get; set; }
    public TilesetAssetSourceKind SourceKind { get; set; }
    public Guid? SourceAssetId { get; set; }
    public int SourceRevision { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string CompiledHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsOptional { get; set; }
    public bool IsDetached { get; set; }
    public DateTimeOffset? CompiledUtc { get; set; }
    public DateTimeOffset? ExportedUtc { get; set; }
}

public sealed class TilesetConstructionSettings
{
    public PackingMode PackingMode { get; set; } = PackingMode.CategoryGroups;
    public bool KeepVariationFamiliesTogether { get; set; } = true;
    public bool ReserveFutureVariationSpace { get; set; } = true;
    public bool ProtectLockedSlots { get; set; } = true;
    public bool AutoApplyMetadataRecommendations { get; set; }
    public bool RequireValidationBeforeExport { get; set; } = true;
    public int ReservedCellsPerFamily { get; set; } = 2;
}

public sealed class TilesetExportSettings
{
    public TilesetExportKind Kind { get; set; } = TilesetExportKind.RpgMakerAssetPack;
    public bool IncludeCatalogues { get; set; } = true;
    public bool IncludeHtmlCatalogue { get; set; } = true;
    public bool IncludeContactSheet { get; set; } = true;
    public bool IncludeShowcaseScene { get; set; } = true;
    public bool IncludeEditableSources { get; set; }
    public bool BlockOnErrors { get; set; } = true;
    public string Author { get; set; } = "Michael Robinson";
    public string LicenseText { get; set; } = string.Empty;
    public string CreditsText { get; set; } = string.Empty;
}

public sealed class TilesetSnapshotRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Snapshot";
    public string RelativeDirectory { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = string.Empty;
}

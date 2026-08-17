using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Autotiles.Models;

public sealed class AutotileDefinition
{
    public int FormatVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Autotile";
    public string Description { get; set; } = string.Empty;
    public AutotileKind Kind { get; set; } = AutotileKind.A2Floor;
    public string SourceImageRelativePath { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Seed { get; set; }
    public bool AllowRotation { get; set; }
    public bool AllowMirroring { get; set; }
    public A2UsageMode A2UsageMode { get; set; } = A2UsageMode.Field;
    public bool IsCounter { get; set; }
    public bool IsOverlay { get; set; }
    public List<string> Tags { get; set; } = new();
    public AutotileTargetBinding? Target { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public DateTimeOffset? LastCompiledUtc { get; set; }
    public string LastCompiledOutputSha256 { get; set; } = string.Empty;
    public MaterialSourceBinding? SourceMaterial { get; set; }
}

public sealed class AutotileTargetBinding
{
    public Guid TileSetId { get; set; }
    public Guid SheetId { get; set; }
    public MvSheetSlot Slot { get; set; }
    public int RegionIndex { get; set; }
}

public sealed class MaterialSourceBinding
{
    public Guid MaterialId { get; set; }
    public int MaterialRevision { get; set; }
    public string OutputChannel { get; set; } = "BaseColor";
    public int Seed { get; set; }
    public string AdapterPreset { get; set; } = string.Empty;
    public string MaterialResultSha256 { get; set; } = string.Empty;
}

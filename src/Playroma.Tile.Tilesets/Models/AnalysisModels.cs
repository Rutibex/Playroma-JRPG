using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Tilesets.Models;

public sealed class TilesetIssue
{
    public TilesetIssueSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public MvSheetSlot? Sheet { get; set; }
    public int? CellIndex { get; set; }
    public Guid? AssetId { get; set; }
}

public sealed class TileDuplicateMatch
{
    public MvSheetSlot FirstSheet { get; set; }
    public int FirstCell { get; set; }
    public MvSheetSlot SecondSheet { get; set; }
    public int SecondCell { get; set; }
    public double Similarity { get; set; }
    public bool IsExact { get; set; }
    public bool IsMirror { get; set; }
    public bool IsRotation { get; set; }
}

public sealed class TilesetStyleReport
{
    public double PaletteConsistency { get; set; }
    public double AverageSaturation { get; set; }
    public double AverageLuminance { get; set; }
    public double AverageContrast { get; set; }
    public int TransparentFringeCount { get; set; }
    public int DetailOutlierCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class PackingPlacement
{
    public Guid ObjectId { get; set; }
    public MvSheetSlot Sheet { get; set; }
    public int CellX { get; set; }
    public int CellY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class PackingPlan
{
    public List<PackingPlacement> Placements { get; set; } = new();
    public List<Guid> UnplacedObjectIds { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int OccupiedCells { get; set; }
}

public sealed class TilesetExportResult
{
    public List<string> ExportedFiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string DestinationDirectory { get; set; } = string.Empty;
}

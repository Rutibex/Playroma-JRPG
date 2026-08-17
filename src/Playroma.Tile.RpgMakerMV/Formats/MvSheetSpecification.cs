using Playroma.Tile.Core.Models;

namespace Playroma.Tile.RpgMakerMV.Formats;

public sealed record MvSheetSpecification(
    MvSheetSlot Slot,
    int Width,
    int Height,
    int LogicalRegionCount,
    string Purpose,
    bool IsAutotile,
    bool RequiresTransparentFirstCell = false)
{
    public const int TileSize = 48;
}

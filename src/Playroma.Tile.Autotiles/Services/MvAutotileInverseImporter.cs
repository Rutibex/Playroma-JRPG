using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;

namespace Playroma.Tile.Autotiles.Services;

public sealed class MvAutotileInverseImporter
{
    public (AutotileDefinition Definition, Bitmap Source) Extract(
        Bitmap sheet,
        MvSheetSlot slot,
        int regionIndex,
        string name)
    {
        if (slot is not (MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4))
            throw new InvalidOperationException("Only A1 through A4 regions can be converted into autotile definitions.");
        var regions = MvLayoutCatalog.GetLogicalRegions(slot);
        if (regionIndex < 0 || regionIndex >= regions.Count) throw new ArgumentOutOfRangeException(nameof(regionIndex));
        var source = AutotileBitmap.Crop(sheet, regions[regionIndex].Bounds);
        var definition = new AutotileDefinition
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{slot} {regionIndex + 1:00}" : name.Trim(),
            Kind = AutotileKindInfo.InferKind(slot, regionIndex),
            SourceSha256 = AutotileBitmap.ComputeSha256(source)
        };
        return (definition, source);
    }
}

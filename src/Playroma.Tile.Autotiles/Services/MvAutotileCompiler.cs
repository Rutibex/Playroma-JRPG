using System.Drawing;
using System.Drawing.Drawing2D;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Formats;

namespace Playroma.Tile.Autotiles.Services;

public sealed class MvAutotileCompiler
{
    public AutotileCompileResult CompileIntoSheet(
        Bitmap sheet,
        TileRegionMetadata regionMetadata,
        MvSheetSlot slot,
        int regionIndex,
        AutotileDefinition definition,
        Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(regionMetadata);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(source);
        MvAutotileRenderer.ValidateSource(source, definition.Kind);

        if (!AutotileKindInfo.IsCompatible(definition.Kind, slot, regionIndex))
            throw new InvalidOperationException($"{definition.Kind} cannot be compiled into {slot} region {regionIndex + 1}.");

        var regions = MvLayoutCatalog.GetLogicalRegions(slot);
        if (regionIndex < 0 || regionIndex >= regions.Count) throw new ArgumentOutOfRangeException(nameof(regionIndex));
        var bounds = regions[regionIndex].Bounds;
        if (source.Width != bounds.Width || source.Height != bounds.Height)
            throw new InvalidDataException($"The source block is {source.Width} × {source.Height}, but the selected MV region is {bounds.Width} × {bounds.Height}.");

        using (var graphics = Graphics.FromImage(sheet))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(source, bounds, new Rectangle(Point.Empty, source.Size), GraphicsUnit.Pixel);
        }

        var sourceHash = AutotileBitmap.ComputeSha256(source);
        using var compiledCrop = AutotileBitmap.Crop(sheet, bounds);
        var outputHash = AutotileBitmap.ComputeSha256(compiledCrop);
        var now = DateTimeOffset.UtcNow;

        regionMetadata.SourceAutotileId = definition.Id;
        regionMetadata.SourceAutotileRevision = definition.Revision;
        regionMetadata.CompiledUtc = now;
        regionMetadata.CompiledSourceSha256 = sourceHash;
        regionMetadata.CompiledOutputSha256 = outputHash;
        if (string.IsNullOrWhiteSpace(regionMetadata.Name)) regionMetadata.Name = definition.Name;

        definition.Target ??= new AutotileTargetBinding();
        definition.Target.Slot = slot;
        definition.Target.RegionIndex = regionIndex;
        definition.SourceSha256 = sourceHash;
        definition.LastCompiledUtc = now;
        definition.LastCompiledOutputSha256 = outputHash;

        return new AutotileCompileResult(bounds, sourceHash, outputHash, now);
    }
}

public sealed record AutotileCompileResult(
    Rectangle Bounds,
    string SourceSha256,
    string OutputSha256,
    DateTimeOffset CompiledUtc);

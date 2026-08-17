namespace Playroma.JRPG.Rendering;

public sealed record MapMinimapResult(
    RgbaImage Image,
    PixelSize MapPixelSize,
    double ScaleX,
    double ScaleY,
    int RenderedChunkCount);

/// <summary>
/// Builds a bounded map overview without allocating a full native-resolution image for large maps.
/// Native chunks are rendered one at a time and sampled into the final minimap.
/// </summary>
public sealed class MapMinimapBuilder
{
    private const int ChunkTiles = 16;
    private readonly MapPixelRenderer _renderer = new();

    public MapMinimapResult Build(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        MapRenderAssetSet assets,
        int maximumWidth = 320,
        int maximumHeight = 320,
        int animationFrame = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileset);
        ArgumentNullException.ThrowIfNull(assets);
        if (maximumWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (maximumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHeight));

        PixelSize mapSize = map.PixelSize;
        double scale = Math.Min(1d, Math.Min(maximumWidth / (double)mapSize.Width, maximumHeight / (double)mapSize.Height));
        int outputWidth = Math.Max(1, checked((int)Math.Round(mapSize.Width * scale)));
        int outputHeight = Math.Max(1, checked((int)Math.Round(mapSize.Height * scale)));
        RgbaImage output = new(outputWidth, outputHeight);

        int chunkPixels = checked(ChunkTiles * MvTileRules.TileWidth);
        int columns = DivideRoundUp(mapSize.Width, chunkPixels);
        int rows = DivideRoundUp(mapSize.Height, chunkPixels);
        int rendered = 0;

        for (int row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int column = 0; column < columns; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PixelRect region = new(
                    column * chunkPixels,
                    row * chunkPixels,
                    Math.Min(chunkPixels, mapSize.Width - column * chunkPixels),
                    Math.Min(chunkPixels, mapSize.Height - row * chunkPixels));
                MapRenderResult chunk = _renderer.RenderRegion(map, tileset, assets, region, animationFrame, cancellationToken);
                SampleChunk(chunk.Image, region, output, mapSize);
                rendered++;
            }
        }

        return new MapMinimapResult(
            output,
            mapSize,
            outputWidth / (double)mapSize.Width,
            outputHeight / (double)mapSize.Height,
            rendered);
    }

    private static void SampleChunk(RgbaImage chunk, PixelRect region, RgbaImage output, PixelSize mapSize)
    {
        int firstOutputX = Math.Max(0, ScaleFloor(region.Left, output.Width, mapSize.Width));
        int firstOutputY = Math.Max(0, ScaleFloor(region.Top, output.Height, mapSize.Height));
        int lastOutputX = Math.Min(output.Width, ScaleCeiling(region.Right, output.Width, mapSize.Width));
        int lastOutputY = Math.Min(output.Height, ScaleCeiling(region.Bottom, output.Height, mapSize.Height));

        for (int outputY = firstOutputY; outputY < lastOutputY; outputY++)
        {
            int sourceMapY = SampleCoordinate(outputY, output.Height, mapSize.Height);
            if (sourceMapY < region.Top || sourceMapY >= region.Bottom) continue;
            int sourceY = sourceMapY - region.Y;
            for (int outputX = firstOutputX; outputX < lastOutputX; outputX++)
            {
                int sourceMapX = SampleCoordinate(outputX, output.Width, mapSize.Width);
                if (sourceMapX < region.Left || sourceMapX >= region.Right) continue;
                output.SetPixel(outputX, outputY, chunk.GetPixel(sourceMapX - region.X, sourceY));
            }
        }
    }

    private static int SampleCoordinate(int outputCoordinate, int outputLength, int sourceLength)
    {
        long numerator = checked(((long)outputCoordinate * 2 + 1) * sourceLength);
        return Math.Min(sourceLength - 1, checked((int)(numerator / (2L * outputLength))));
    }

    private static int ScaleFloor(int value, int numerator, int denominator) =>
        checked((int)((long)value * numerator / denominator));

    private static int ScaleCeiling(int value, int numerator, int denominator) =>
        checked((int)(((long)value * numerator + denominator - 1) / denominator));

    private static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);
}

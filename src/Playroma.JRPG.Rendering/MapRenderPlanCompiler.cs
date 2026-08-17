using System.Globalization;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

/// <summary>Compiles MV's flat six-layer map representation into ordered render instructions.</summary>
public sealed class MapRenderPlanCompiler
{
    private static readonly Rgba32 ShadowColour = new(0, 0, 0, 128);
    private readonly MvTileInstructionDecoder _decoder = new();

    public MapRenderPlan Compile(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        PixelRect region,
        int animationFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileset);
        PixelRect mapBounds = new(0, 0, map.PixelSize.Width, map.PixelSize.Height);
        PixelRect clippedRegion = PixelRect.Intersect(mapBounds, region);
        if (clippedRegion.IsEmpty) return new MapRenderPlan(region, [], [], [], []);

        List<TileRenderInstruction> blits = [];
        List<FillRenderInstruction> fills = [];
        List<CharacterRenderInstruction> characters = [];
        List<ProjectDiagnostic> diagnostics = [];
        diagnostics.AddRange(map.Diagnostics);
        diagnostics.AddRange(tileset.Diagnostics);
        int sequence = 0;

        int startX = Math.Max(0, clippedRegion.Left / MvTileRules.TileWidth);
        int startY = Math.Max(0, clippedRegion.Top / MvTileRules.TileHeight);
        int endX = Math.Min(map.Width, DivideRoundUp(clippedRegion.Right, MvTileRules.TileWidth));
        int endY = Math.Min(map.Height, DivideRoundUp(clippedRegion.Bottom, MvTileRules.TileHeight));

        for (int y = startY; y < endY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                int destinationX = checked(x * MvTileRules.TileWidth);
                int destinationY = checked(y * MvTileRules.TileHeight);
                int tile0 = map.Read(x, y, 0);
                int tile1 = map.Read(x, y, 1);
                int tile2 = map.Read(x, y, 2);
                int tile3 = map.Read(x, y, 3);
                int shadowRaw = map.ReadShadow(x, y);
                int upperTile1 = map.Read(x, y - 1, 1);
                ProjectLocation location = new(map.RelativePath, $"/data/{((y * map.Width) + x).ToString(CultureInfo.InvariantCulture)}", map.MapId);

                AppendLower(tile0, TileLayer.Lower0, destinationX, destinationY, tileset, animationFrame, blits, ref sequence, diagnostics, location);
                AppendLower(tile1, TileLayer.Lower1, destinationX, destinationY, tileset, animationFrame, blits, ref sequence, diagnostics, location);

                int shadowBits = shadowRaw & 0x0f;
                for (int quarter = 0; quarter < 4; quarter++)
                {
                    if ((shadowBits & (1 << quarter)) == 0) continue;
                    fills.Add(new FillRenderInstruction(
                        RenderPass.LowerTiles,
                        sequence++,
                        new PixelRect(
                            destinationX + quarter % 2 * MvTileRules.QuarterWidth,
                            destinationY + quarter / 2 * MvTileRules.QuarterHeight,
                            MvTileRules.QuarterWidth,
                            MvTileRules.QuarterHeight),
                        ShadowColour,
                        TileLayer.Shadow));
                }

                if (IsTableTile(upperTile1, tileset) && !IsTableTile(tile1, tileset) && !MvTileRules.IsShadowingTile(tile0))
                {
                    _decoder.AppendTableEdge(
                        upperTile1,
                        destinationX,
                        destinationY,
                        RenderPass.LowerTiles,
                        TileLayer.Lower1,
                        blits,
                        ref sequence,
                        diagnostics,
                        location);
                }

                AppendClassified(tile2, TileLayer.Upper0, destinationX, destinationY, tileset, animationFrame, blits, ref sequence, diagnostics, location);
                AppendClassified(tile3, TileLayer.Upper1, destinationX, destinationY, tileset, animationFrame, blits, ref sequence, diagnostics, location);
            }
        }

        foreach (MapEventRenderSnapshot mapEvent in map.Events
                     .Where(candidate => candidate.X >= startX - 1 && candidate.X <= endX && candidate.Y >= startY - 1 && candidate.Y <= endY + 1)
                     .OrderBy(candidate => candidate.Pass)
                     .ThenBy(candidate => candidate.Y)
                     .ThenBy(candidate => candidate.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int destinationX = checked(mapEvent.X * MvTileRules.TileWidth);
            int destinationY = checked(mapEvent.Y * MvTileRules.TileHeight);
            ProjectLocation eventLocation = new(
                map.RelativePath,
                $"/events/{mapEvent.Id.ToString(CultureInfo.InvariantCulture)}/pages/{mapEvent.PageIndex.ToString(CultureInfo.InvariantCulture)}/image",
                map.MapId,
                EventId: mapEvent.Id,
                PageIndex: mapEvent.PageIndex);
            if (mapEvent.Graphic.IsTile)
            {
                int tileId = mapEvent.Graphic.TileId;
                _decoder.AppendTile(
                    tileId,
                    destinationX,
                    destinationY,
                    mapEvent.Pass,
                    PriorityLayer(mapEvent.PriorityType),
                    animationFrame,
                    IsTableTile(tileId, tileset),
                    blits,
                    ref sequence,
                    diagnostics,
                    eventLocation);
            }
            else if (mapEvent.Graphic.IsCharacter)
            {
                characters.Add(new CharacterRenderInstruction(
                    mapEvent.Pass,
                    sequence++,
                    mapEvent.Graphic.CharacterName,
                    mapEvent.Graphic.CharacterIndex,
                    mapEvent.Graphic.Direction,
                    mapEvent.Graphic.Pattern,
                    mapEvent.X,
                    mapEvent.Y,
                    mapEvent.Id));
            }
        }

        return new MapRenderPlan(clippedRegion, blits, fills, characters, diagnostics);
    }

    private void AppendLower(
        int tileId,
        TileLayer layer,
        int destinationX,
        int destinationY,
        TilesetRenderSnapshot tileset,
        int animationFrame,
        List<TileRenderInstruction> blits,
        ref int sequence,
        List<ProjectDiagnostic> diagnostics,
        ProjectLocation location)
    {
        if (!MvTileRules.IsVisible(tileId)) return;
        _decoder.AppendTile(
            tileId,
            destinationX,
            destinationY,
            RenderPass.LowerTiles,
            layer,
            animationFrame,
            IsTableTile(tileId, tileset),
            blits,
            ref sequence,
            diagnostics,
            location);
    }

    private void AppendClassified(
        int tileId,
        TileLayer layer,
        int destinationX,
        int destinationY,
        TilesetRenderSnapshot tileset,
        int animationFrame,
        List<TileRenderInstruction> blits,
        ref int sequence,
        List<ProjectDiagnostic> diagnostics,
        ProjectLocation location)
    {
        if (!MvTileRules.IsVisible(tileId)) return;
        RenderPass pass = tileset.GetFlags(tileId).IsHigher ? RenderPass.UpperTiles : RenderPass.LowerTiles;
        _decoder.AppendTile(
            tileId,
            destinationX,
            destinationY,
            pass,
            layer,
            animationFrame,
            IsTableTile(tileId, tileset),
            blits,
            ref sequence,
            diagnostics,
            location);
    }

    private static bool IsTableTile(int tileId, TilesetRenderSnapshot tileset) =>
        MvTileRules.IsTileA2(tileId) && tileset.GetFlags(tileId).IsCounter;

    private static TileLayer PriorityLayer(int priority) => priority switch
    {
        <= 0 => TileLayer.EventBelow,
        1 => TileLayer.EventSame,
        _ => TileLayer.EventAbove
    };

    private static int DivideRoundUp(int value, int divisor) => checked((value + divisor - 1) / divisor);
}

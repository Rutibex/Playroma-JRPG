using System.Globalization;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

public sealed class MvTileInstructionDecoder
{
    private static readonly int[] WaterSurfaceFrames = [0, 1, 2, 1];

    public void AppendTile(
        int tileId,
        int destinationX,
        int destinationY,
        RenderPass pass,
        TileLayer layer,
        int animationFrame,
        bool isTable,
        List<TileRenderInstruction> output,
        ref int sequence,
        List<ProjectDiagnostic>? diagnostics = null,
        ProjectLocation? location = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!MvTileRules.IsVisible(tileId)) return;
        if (MvTileRules.IsAutotile(tileId))
        {
            AppendAutotile(tileId, destinationX, destinationY, pass, layer, animationFrame, isTable, output, ref sequence, diagnostics, location);
        }
        else
        {
            AppendNormalTile(tileId, destinationX, destinationY, pass, layer, output, ref sequence);
        }
    }

    public void AppendTableEdge(
        int tileId,
        int destinationX,
        int destinationY,
        RenderPass pass,
        TileLayer layer,
        List<TileRenderInstruction> output,
        ref int sequence,
        List<ProjectDiagnostic>? diagnostics = null,
        ProjectLocation? location = null)
    {
        if (!MvTileRules.IsTileA2(tileId)) return;
        int kind = MvTileRules.GetAutotileKind(tileId);
        int shape = MvTileRules.GetAutotileShape(tileId);
        if (shape < 0 || shape >= MvTileRules.FloorAutotileTable.Length)
        {
            AddInvalidShapeDiagnostic(tileId, shape, "floor", diagnostics, location);
            return;
        }
        int tx = kind % 8;
        int ty = kind / 8;
        int bx = tx * 2;
        int by = (ty - 2) * 3;
        QuarterCoordinate[] table = MvTileRules.FloorAutotileTable[shape];
        for (int index = 0; index < 2; index++)
        {
            QuarterCoordinate quarter = table[2 + index];
            PixelRect source = new(
                (bx * 2 + quarter.X) * MvTileRules.QuarterWidth,
                (by * 2 + quarter.Y) * MvTileRules.QuarterHeight + MvTileRules.QuarterHeight / 2,
                MvTileRules.QuarterWidth,
                MvTileRules.QuarterHeight / 2);
            PixelRect destination = new(
                destinationX + index * MvTileRules.QuarterWidth,
                destinationY,
                MvTileRules.QuarterWidth,
                MvTileRules.QuarterHeight / 2);
            output.Add(new TileRenderInstruction(pass, sequence++, RenderImageKey.Tileset(TileSheetKind.A2), source, destination, 255, tileId, layer));
        }
    }

    private static void AppendNormalTile(
        int tileId,
        int destinationX,
        int destinationY,
        RenderPass pass,
        TileLayer layer,
        List<TileRenderInstruction> output,
        ref int sequence)
    {
        TileSheetKind sheet = MvTileRules.IsTileA5(tileId)
            ? TileSheetKind.A5
            : (TileSheetKind)(5 + tileId / 256);
        int sourceX = ((tileId / 128 % 2) * 8 + tileId % 8) * MvTileRules.TileWidth;
        int sourceY = (tileId % 256 / 8 % 16) * MvTileRules.TileHeight;
        output.Add(new TileRenderInstruction(
            pass,
            sequence++,
            RenderImageKey.Tileset(sheet),
            new PixelRect(sourceX, sourceY, MvTileRules.TileWidth, MvTileRules.TileHeight),
            new PixelRect(destinationX, destinationY, MvTileRules.TileWidth, MvTileRules.TileHeight),
            255,
            tileId,
            layer));
    }

    private static void AppendAutotile(
        int tileId,
        int destinationX,
        int destinationY,
        RenderPass pass,
        TileLayer layer,
        int animationFrame,
        bool isTable,
        List<TileRenderInstruction> output,
        ref int sequence,
        List<ProjectDiagnostic>? diagnostics,
        ProjectLocation? location)
    {
        QuarterCoordinate[][] table = MvTileRules.FloorAutotileTable;
        int kind = MvTileRules.GetAutotileKind(tileId);
        int shape = MvTileRules.GetAutotileShape(tileId);
        int tx = kind % 8;
        int ty = kind / 8;
        int bx = 0;
        int by = 0;
        TileSheetKind sheet = TileSheetKind.A1;
        string tableName = "floor";

        if (MvTileRules.IsTileA1(tileId))
        {
            int normalizedFrame = Mod(animationFrame, 12);
            int waterSurfaceIndex = WaterSurfaceFrames[normalizedFrame % 4];
            sheet = TileSheetKind.A1;
            if (kind == 0)
            {
                bx = waterSurfaceIndex * 2;
                by = 0;
            }
            else if (kind == 1)
            {
                bx = waterSurfaceIndex * 2;
                by = 3;
            }
            else if (kind == 2)
            {
                bx = 6;
                by = 0;
            }
            else if (kind == 3)
            {
                bx = 6;
                by = 3;
            }
            else
            {
                bx = tx / 4 * 8;
                by = ty * 6 + tx / 2 % 2 * 3;
                if (kind % 2 == 0)
                {
                    bx += waterSurfaceIndex * 2;
                }
                else
                {
                    bx += 6;
                    table = MvTileRules.WaterfallAutotileTable;
                    tableName = "waterfall";
                    by += normalizedFrame % 3;
                }
            }
        }
        else if (MvTileRules.IsTileA2(tileId))
        {
            sheet = TileSheetKind.A2;
            bx = tx * 2;
            by = (ty - 2) * 3;
        }
        else if (MvTileRules.IsTileA3(tileId))
        {
            sheet = TileSheetKind.A3;
            bx = tx * 2;
            by = (ty - 6) * 2;
            table = MvTileRules.WallAutotileTable;
            tableName = "wall";
        }
        else if (MvTileRules.IsTileA4(tileId))
        {
            sheet = TileSheetKind.A4;
            bx = tx * 2;
            by = (int)Math.Floor((ty - 10) * 2.5 + (ty % 2 == 1 ? 0.5 : 0));
            if (ty % 2 == 1)
            {
                table = MvTileRules.WallAutotileTable;
                tableName = "wall";
            }
        }

        if (shape < 0 || shape >= table.Length)
        {
            AddInvalidShapeDiagnostic(tileId, shape, tableName, diagnostics, location);
            return;
        }

        QuarterCoordinate[] quarters = table[shape];
        for (int index = 0; index < 4; index++)
        {
            QuarterCoordinate quarter = quarters[index];
            int destinationQuarterX = destinationX + index % 2 * MvTileRules.QuarterWidth;
            int destinationQuarterY = destinationY + index / 2 * MvTileRules.QuarterHeight;
            if (isTable && (quarter.Y == 1 || quarter.Y == 5))
            {
                int replacementX = quarter.X;
                if (quarter.Y == 1) replacementX = new[] { 0, 3, 2, 1 }[quarter.X];
                PixelRect replacement = new(
                    (bx * 2 + replacementX) * MvTileRules.QuarterWidth,
                    (by * 2 + 3) * MvTileRules.QuarterHeight,
                    MvTileRules.QuarterWidth,
                    MvTileRules.QuarterHeight);
                output.Add(new TileRenderInstruction(
                    pass,
                    sequence++,
                    RenderImageKey.Tileset(sheet),
                    replacement,
                    new PixelRect(destinationQuarterX, destinationQuarterY, MvTileRules.QuarterWidth, MvTileRules.QuarterHeight),
                    255,
                    tileId,
                    layer));
                PixelRect originalTopHalf = new(
                    (bx * 2 + quarter.X) * MvTileRules.QuarterWidth,
                    (by * 2 + quarter.Y) * MvTileRules.QuarterHeight,
                    MvTileRules.QuarterWidth,
                    MvTileRules.QuarterHeight / 2);
                output.Add(new TileRenderInstruction(
                    pass,
                    sequence++,
                    RenderImageKey.Tileset(sheet),
                    originalTopHalf,
                    new PixelRect(destinationQuarterX, destinationQuarterY + MvTileRules.QuarterHeight / 2, MvTileRules.QuarterWidth, MvTileRules.QuarterHeight / 2),
                    255,
                    tileId,
                    layer));
            }
            else
            {
                PixelRect source = new(
                    (bx * 2 + quarter.X) * MvTileRules.QuarterWidth,
                    (by * 2 + quarter.Y) * MvTileRules.QuarterHeight,
                    MvTileRules.QuarterWidth,
                    MvTileRules.QuarterHeight);
                output.Add(new TileRenderInstruction(
                    pass,
                    sequence++,
                    RenderImageKey.Tileset(sheet),
                    source,
                    new PixelRect(destinationQuarterX, destinationQuarterY, MvTileRules.QuarterWidth, MvTileRules.QuarterHeight),
                    255,
                    tileId,
                    layer));
            }
        }
    }

    private static void AddInvalidShapeDiagnostic(
        int tileId,
        int shape,
        string family,
        List<ProjectDiagnostic>? diagnostics,
        ProjectLocation? location)
    {
        diagnostics?.Add(ProjectDiagnostic.Warning(
            DiagnosticCodes.Phase6InvalidAutotileShape,
            $"Tile {tileId.ToString(CultureInfo.InvariantCulture)} uses autotile shape {shape.ToString(CultureInfo.InvariantCulture)}, which is invalid for the {family} autotile family.",
            location));
    }

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

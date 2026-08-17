using System.Diagnostics;
using System.Globalization;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Rendering;

/// <summary>Deterministic reference renderer for MV map regions.</summary>
public sealed class MapPixelRenderer
{
    private static readonly Rgba32 MissingA = new(255, 0, 255, 255);
    private static readonly Rgba32 MissingB = new(24, 24, 24, 255);
    private readonly MapRenderPlanCompiler _compiler = new();

    public MapRenderResult RenderRegion(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        MapRenderAssetSet assets,
        PixelRect region,
        int animationFrame = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileset);
        ArgumentNullException.ThrowIfNull(assets);
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        Stopwatch stopwatch = Stopwatch.StartNew();
        RgbaImage output = new(region.Width, region.Height);
        DrawParallax(output, map, assets, region, cancellationToken);
        MapRenderPlan plan = _compiler.Compile(map, tileset, region, animationFrame, cancellationToken);
        List<ProjectDiagnostic> diagnostics = [];
        diagnostics.AddRange(assets.Diagnostics);
        diagnostics.AddRange(plan.Diagnostics);

        List<RenderOperation> operations = new(plan.Blits.Count + plan.Fills.Count + plan.Characters.Count);
        operations.AddRange(plan.Blits.Select(blit => new RenderOperation(blit.Pass, blit.Sequence, blit, null, null)));
        operations.AddRange(plan.Fills.Select(fill => new RenderOperation(fill.Pass, fill.Sequence, null, fill, null)));
        operations.AddRange(plan.Characters.Select(character => new RenderOperation(character.Pass, character.Sequence, null, null, character)));
        operations.Sort(static (left, right) =>
        {
            int pass = left.Pass.CompareTo(right.Pass);
            return pass != 0 ? pass : left.Sequence.CompareTo(right.Sequence);
        });

        HashSet<string> reportedMissing = new(StringComparer.OrdinalIgnoreCase);
        foreach (RenderOperation operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Blit is not null)
            {
                DrawBlit(output, operation.Blit, assets, region, diagnostics, reportedMissing);
            }
            else if (operation.Fill is not null)
            {
                output.Fill(operation.Fill.Destination.Offset(-region.X, -region.Y), operation.Fill.Colour);
            }
            else if (operation.Character is not null)
            {
                DrawCharacter(output, operation.Character, assets, region, diagnostics, reportedMissing);
            }
        }

        stopwatch.Stop();
        return new MapRenderResult(
            output,
            diagnostics,
            new MapRenderStatistics(plan.Blits.Count, plan.Fills.Count, plan.Characters.Count, stopwatch.Elapsed));
    }

    public MapRenderResult RenderWholeMap(
        MapRenderSnapshot map,
        TilesetRenderSnapshot tileset,
        MapRenderAssetSet assets,
        int animationFrame = 0,
        CancellationToken cancellationToken = default)
    {
        PixelSize size = map.PixelSize;
        return RenderRegion(map, tileset, assets, new PixelRect(0, 0, size.Width, size.Height), animationFrame, cancellationToken);
    }

    private static void DrawBlit(
        RgbaImage output,
        TileRenderInstruction instruction,
        MapRenderAssetSet assets,
        PixelRect region,
        List<ProjectDiagnostic> diagnostics,
        HashSet<string> reportedMissing)
    {
        PixelRect destination = instruction.Destination.Offset(-region.X, -region.Y);
        if (!assets.TryGetImage(instruction.Image, out RgbaImage? source) || source is null)
        {
            DrawMissing(output, destination);
            string identity = instruction.Image.ToString();
            if (reportedMissing.Add(identity))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6RenderAssetMissing,
                    $"The renderer could not resolve {Describe(instruction.Image)}.",
                    technicalDetail: identity));
            }
            return;
        }
        if (!source.Bounds.Intersects(instruction.Source) || instruction.Source.Right > source.Width || instruction.Source.Bottom > source.Height)
        {
            string identity = instruction.Image + ":" + instruction.Source;
            if (reportedMissing.Add(identity))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6TilesetDimensions,
                    $"A render source rectangle for {Describe(instruction.Image)} falls outside the decoded image.",
                    technicalDetail: $"Image={source.Width}x{source.Height}; source={instruction.Source}"));
            }
        }
        output.Blit(source, instruction.Source, destination.Location, instruction.Opacity);
    }

    private static void DrawCharacter(
        RgbaImage output,
        CharacterRenderInstruction instruction,
        MapRenderAssetSet assets,
        PixelRect region,
        List<ProjectDiagnostic> diagnostics,
        HashSet<string> reportedMissing)
    {
        RenderImageKey key = RenderImageKey.Character(instruction.CharacterName);
        if (!assets.TryGetImage(key, out RgbaImage? source) || source is null)
        {
            PixelRect missing = new(
                instruction.MapX * MvTileRules.TileWidth - region.X,
                instruction.MapY * MvTileRules.TileHeight - region.Y,
                MvTileRules.TileWidth,
                MvTileRules.TileHeight);
            DrawMissing(output, missing);
            if (reportedMissing.Add(key.ToString()))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6RenderAssetMissing,
                    $"Character sheet '{instruction.CharacterName}' could not be loaded.",
                    new ProjectLocation(EventId: instruction.EventId)));
            }
            return;
        }

        string filename = Path.GetFileName(instruction.CharacterName);
        bool single = filename.StartsWith('$') || filename.StartsWith("!$", StringComparison.Ordinal);
        bool objectCharacter = filename.StartsWith('!');
        int columns = single ? 3 : 12;
        int rows = single ? 4 : 8;
        if (source.Width % columns != 0 || source.Height % rows != 0)
        {
            if (reportedMissing.Add(key.ToString() + ":grid"))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.Phase6CharacterGrid,
                    $"Character sheet '{instruction.CharacterName}' does not match MV's required {columns.ToString(CultureInfo.InvariantCulture)}×{rows.ToString(CultureInfo.InvariantCulture)} frame grid.",
                    new ProjectLocation(EventId: instruction.EventId),
                    $"Image dimensions: {source.Width}x{source.Height}"));
            }
            return;
        }

        int frameWidth = source.Width / columns;
        int frameHeight = source.Height / rows;
        int blockX = single ? 0 : instruction.CharacterIndex % 4 * 3;
        int blockY = single ? 0 : instruction.CharacterIndex / 4 * 4;
        int directionRow = (instruction.Direction - 2) / 2;
        int sourceX = checked((blockX + Math.Clamp(instruction.Pattern, 0, 2)) * frameWidth);
        int sourceY = checked((blockY + Math.Clamp(directionRow, 0, 3)) * frameHeight);
        int destinationX = checked(instruction.MapX * MvTileRules.TileWidth + (MvTileRules.TileWidth - frameWidth) / 2 - region.X);
        int shiftY = objectCharacter ? 0 : 6;
        int destinationY = checked(instruction.MapY * MvTileRules.TileHeight + MvTileRules.TileHeight - frameHeight - shiftY - region.Y);
        output.Blit(source, new PixelRect(sourceX, sourceY, frameWidth, frameHeight), new PixelPoint(destinationX, destinationY));
    }

    private static void DrawParallax(
        RgbaImage output,
        MapRenderSnapshot map,
        MapRenderAssetSet assets,
        PixelRect region,
        CancellationToken cancellationToken)
    {
        ParallaxRenderSnapshot parallax = map.Parallax;
        if (!parallax.HasImage || !parallax.ShowInEditor) return;
        if (!assets.TryGetImage(RenderImageKey.Parallax(parallax.Name), out RgbaImage? image) || image is null) return;

        bool repeatX = parallax.LoopX;
        bool repeatY = parallax.LoopY;
        int startX = repeatX ? FloorMultiple(region.Left, image.Width) : 0;
        int startY = repeatY ? FloorMultiple(region.Top, image.Height) : 0;
        int endX = repeatX ? region.Right : image.Width;
        int endY = repeatY ? region.Bottom : image.Height;
        int stepX = repeatX ? image.Width : Math.Max(image.Width, endX - startX + 1);
        int stepY = repeatY ? image.Height : Math.Max(image.Height, endY - startY + 1);
        for (int y = startY; y < endY; y += stepY)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x += stepX)
            {
                output.Blit(image, image.Bounds, new PixelPoint(x - region.X, y - region.Y));
                if (!repeatX) break;
            }
            if (!repeatY) break;
        }
    }

    private static void DrawMissing(RgbaImage output, PixelRect destination)
    {
        PixelRect clipped = PixelRect.Intersect(output.Bounds, destination);
        if (clipped.IsEmpty) return;
        output.DrawCheckerboard(clipped, 8, MissingA, MissingB);
    }

    private static int FloorMultiple(int value, int divisor)
    {
        int quotient = value / divisor;
        if (value < 0 && value % divisor != 0) quotient--;
        return quotient * divisor;
    }

    private static string Describe(RenderImageKey key) => key.Kind switch
    {
        RenderImageKind.Tileset => $"tileset sheet {key.Sheet}",
        RenderImageKind.Character => $"character sheet '{key.Name}'",
        RenderImageKind.Parallax => $"parallax '{key.Name}'",
        _ => key.Name
    };

    private sealed record RenderOperation(
        RenderPass Pass,
        int Sequence,
        TileRenderInstruction? Blit,
        FillRenderInstruction? Fill,
        CharacterRenderInstruction? Character);
}

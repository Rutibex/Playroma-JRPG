using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;

namespace Playroma.Tile.Autotiles.Validation;

public sealed class AutotileValidator
{
    public IReadOnlyList<AutotileIssue> Validate(AutotileDefinition definition, Bitmap source)
    {
        var issues = new List<AutotileIssue>();
        var expected = AutotileKindInfo.GetSourceSize(definition.Kind);
        if (source.Width != expected.Width || source.Height != expected.Height)
        {
            issues.Add(new(AutotileIssueSeverity.Error, "AT001",
                $"Expected {expected.Width} × {expected.Height}; found {source.Width} × {source.Height}."));
            return issues;
        }
        if (string.IsNullOrWhiteSpace(definition.Name))
            issues.Add(new(AutotileIssueSeverity.Warning, "AT002", "The autotile has no name."));

        var family = AutotileKindInfo.GetFamily(definition.Kind);
        var renderer = new MvAutotileRenderer();
        var shapeCount = family switch
        {
            AutotileFamily.Floor => 48,
            AutotileFamily.Wall => 16,
            _ => 4
        };
        for (var frame = 0; frame < AutotileKindInfo.GetFrameCount(definition.Kind); frame++)
        for (var shape = 0; shape < shapeCount; shape++)
        {
            using var rendered = renderer.RenderShape(source, definition.Kind, shape, frame);
            if (IsFullyTransparent(rendered))
            {
                issues.Add(new(AutotileIssueSeverity.Warning, "AT010",
                    $"Frame {frame + 1}, shape {shape} is fully transparent."));
                if (issues.Count(x => x.Code == "AT010") >= 8) break;
            }
        }

        CheckSourceSeams(definition, source, issues);
        if (definition.SourceSha256.Length > 0)
        {
            var actual = AutotileBitmap.ComputeSha256(source);
            if (!string.Equals(actual, definition.SourceSha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(AutotileIssueSeverity.Information, "AT020", "The source image has changed since the definition was last saved."));
        }
        return issues;
    }

    private static void CheckSourceSeams(AutotileDefinition definition, Bitmap source, List<AutotileIssue> issues)
    {
        if (AutotileKindInfo.GetFamily(definition.Kind) != AutotileFamily.Floor) return;
        for (var frame = 0; frame < AutotileKindInfo.GetFrameCount(definition.Kind); frame++)
        {
            var origin = MvAutotileRenderer.GetFrameOrigin(definition.Kind, frame);
            var mismatchHorizontal = AverageDifference(source, origin.X + 48, origin.Y + 96, origin.X + 47, origin.Y + 96, 24, vertical: true);
            var mismatchVertical = AverageDifference(source, origin.X + 48, origin.Y + 96, origin.X + 48, origin.Y + 95, 24, vertical: false);
            if (mismatchHorizontal > 55)
                issues.Add(new(AutotileIssueSeverity.Warning, "AT030", $"Frame {frame + 1} has a strong horizontal interior seam ({mismatchHorizontal:0})."));
            if (mismatchVertical > 55)
                issues.Add(new(AutotileIssueSeverity.Warning, "AT031", $"Frame {frame + 1} has a strong vertical interior seam ({mismatchVertical:0})."));
        }
    }

    private static double AverageDifference(Bitmap bitmap, int x1, int y1, int x2, int y2, int count, bool vertical)
    {
        double total = 0;
        for (var i = 0; i < count; i++)
        {
            var a = bitmap.GetPixel(x1 + (vertical ? 0 : i), y1 + (vertical ? i : 0));
            var b = bitmap.GetPixel(x2 + (vertical ? 0 : i), y2 + (vertical ? i : 0));
            total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A);
        }
        return total / count;
    }

    private static bool IsFullyTransparent(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y += 3)
        for (var x = 0; x < bitmap.Width; x += 3)
            if (bitmap.GetPixel(x, y).A != 0) return false;
        return true;
    }
}

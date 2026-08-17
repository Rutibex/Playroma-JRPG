using System.Drawing;
using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Integration;

public sealed class MaterialAutotileSettings
{
    public MaterialAdapterMode Mode { get; set; } = MaterialAdapterMode.UniformFloor;
    public AutotileKind? KindOverride { get; set; }
    public int BoundaryDarkening { get; set; } = 28;
    public int BoundaryWidth { get; set; } = 2;
    public bool PreserveAlpha { get; set; }
    public bool IsCounter { get; set; }
    public bool IsOverlay { get; set; }
    public A2UsageMode A2UsageMode { get; set; } = A2UsageMode.Field;
}

public sealed class MaterialAutotileAdapter
{
    private readonly AutotileMaterialComposer _composer = new();

    public (AutotileDefinition Definition, Bitmap Source) CreateAutotile(
        MaterialRecipe recipe,
        MaterialResult result,
        MaterialAutotileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        var baseColor = result.BaseColor ?? throw new InvalidOperationException("The material has no BaseColor output.");
        var kind = settings.KindOverride ?? InferKind(settings.Mode);
        var definition = new AutotileDefinition
        {
            Id = Guid.NewGuid(),
            Name = recipe.Name,
            Description = $"Generated from material recipe {recipe.Name}.",
            Kind = kind,
            Seed = result.Seed,
            IsCounter = settings.IsCounter,
            IsOverlay = settings.IsOverlay || settings.Mode == MaterialAdapterMode.TransparentDecoration,
            A2UsageMode = settings.A2UsageMode,
            Tags = recipe.Tags.Concat(new[] { "Procedural", "Phase3" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceMaterial = new MaterialSourceBinding
            {
                MaterialId = recipe.Id,
                MaterialRevision = recipe.Revision,
                OutputChannel = MaterialChannel.BaseColor.ToString(),
                Seed = result.Seed,
                AdapterPreset = settings.Mode.ToString(),
                MaterialResultSha256 = MaterialGraphEvaluator.ComputeBufferHash(baseColor)
            }
        };

        using var materialBitmap = baseColor.ToBitmap();
        var source = _composer.Compose(kind, materialBitmap, settings.BoundaryDarkening, settings.BoundaryWidth, settings.PreserveAlpha);
        if (result.Frames.Count >= 3 && AutotileKindInfo.GetFrameCount(kind) == 3)
        {
            ApplyFrames(source, kind, result.Frames, settings);
        }
        return (definition, source);
    }

    private void ApplyFrames(Bitmap destination, AutotileKind kind, IReadOnlyList<ColorBuffer> frames, MaterialAutotileSettings settings)
    {
        for (var frame = 0; frame < 3; frame++)
        {
            using var bitmap = frames[frame].ToBitmap();
            using var compiled = _composer.Compose(kind, bitmap, settings.BoundaryDarkening, settings.BoundaryWidth, settings.PreserveAlpha);
            var sourceRect = kind == AutotileKind.A1Waterfall
                ? new Rectangle(0, frame * 48, 96, 48)
                : new Rectangle(frame * 96, 0, 96, 144);
            using var g = Graphics.FromImage(destination);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(compiled, sourceRect, sourceRect, GraphicsUnit.Pixel);
        }
    }

    private static AutotileKind InferKind(MaterialAdapterMode mode) => mode switch
    {
        MaterialAdapterMode.Roof => AutotileKind.A3Building,
        MaterialAdapterMode.Wall or MaterialAdapterMode.Cliff => AutotileKind.A4Wall,
        MaterialAdapterMode.AnimatedWater or MaterialAdapterMode.Shoreline => AutotileKind.A1Water,
        MaterialAdapterMode.Waterfall => AutotileKind.A1Waterfall,
        _ => AutotileKind.A2Floor
    };
}

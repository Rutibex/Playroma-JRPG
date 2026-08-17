using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Nodes;

public sealed class PoissonPointsNode : MaterialNodeDefinitionBase
{
    public override string TypeId => "Source.PoissonPoints";
    public override string DisplayName => "Poisson Disc Points";
    public override MaterialNodeCategory Category => MaterialNodeCategory.Sources;
    public override TileabilitySupport Tileability => TileabilitySupport.NativePeriodic;
    public override IReadOnlyList<MaterialPortDefinition> Outputs => new[] { new MaterialPortDefinition("Value", MaterialDataType.Mask) };
    public override IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; } = NodeUtilities.Defaults(
        ("MinimumDistance", MaterialParameterValue.From(8f)), ("Radius", MaterialParameterValue.From(1.5f)), ("SeedOffset", MaterialParameterValue.From(0)));

    public override MaterialNodeResult Evaluate(MaterialNodeContext context)
    {
        var width = context.Evaluation.Width; var height = context.Evaluation.Height;
        var minimum = Math.Clamp(context.Float("MinimumDistance", 8f), 1f, Math.Min(width, height));
        var radius = Math.Clamp(context.Float("Radius", 1.5f), .5f, minimum * .5f);
        var random = new Random(context.Evaluation.Seed + context.Int("SeedOffset"));
        var points = new List<(float X, float Y)>();
        var attempts = Math.Max(64, width * height / Math.Max(1, (int)(minimum * minimum)) * 40);
        for (var i = 0; i < attempts; i++)
        {
            var candidate = ((float)random.NextDouble() * width, (float)random.NextDouble() * height);
            var valid = points.All(p => ToroidalDistance(p, candidate, width, height) >= minimum);
            if (valid) points.Add(candidate);
        }
        var field = new ScalarField(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            field[x, y] = points.Any(p => ToroidalDistance(p, (x, y), width, height) <= radius) ? 1f : 0f;
        return new MaterialNodeResult().Add("Value", MaterialValue.From(field, true));
    }

    private static float ToroidalDistance((float X,float Y) a, (float X,float Y) b, int width, int height)
    {
        var dx = MathF.Abs(a.X - b.X); var dy = MathF.Abs(a.Y - b.Y);
        dx = Math.Min(dx, width - dx); dy = Math.Min(dy, height - dy);
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class GrowthNode : MaterialNodeDefinitionBase
{
    public override string TypeId => "Simulation.Growth";
    public override string DisplayName => "Organic Growth";
    public override MaterialNodeCategory Category => MaterialNodeCategory.Simulation;
    public override TileabilitySupport Tileability => TileabilitySupport.NativePeriodic;
    public override IReadOnlyList<MaterialPortDefinition> Inputs => new[] { new MaterialPortDefinition("SeedMask", MaterialDataType.Mask) };
    public override IReadOnlyList<MaterialPortDefinition> Outputs => new[] { new MaterialPortDefinition("Value", MaterialDataType.Scalar) };
    public override IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; } = NodeUtilities.Defaults(
        ("Kind", MaterialParameterValue.FromEnum(GrowthKind.Branching)), ("Iterations", MaterialParameterValue.From(180)), ("Density", MaterialParameterValue.From(.12f)), ("SeedOffset", MaterialParameterValue.From(0)));

    public override MaterialNodeResult Evaluate(MaterialNodeContext context)
    {
        var width = context.Evaluation.Width; var height = context.Evaluation.Height;
        var field = context.Input("SeedMask") is null ? new ScalarField(width, height) : NodeUtilities.ToScalar(context.Input("SeedMask"), width, height).Clone();
        var random = new Random(context.Evaluation.Seed + context.Int("SeedOffset"));
        if (field.Values.ToArray().All(x => x < .5f))
            for (var i = 0; i < Math.Max(1, width * height / 500); i++) field[random.Next(width), random.Next(height)] = 1f;
        var kind = context.Enum("Kind", GrowthKind.Branching); var iterations = context.Evaluation.Quality == MaterialQuality.Draft ? Math.Min(60, context.Int("Iterations", 180)) : context.Int("Iterations", 180);
        var density = Math.Clamp(context.Float("Density", .12f), .001f, .95f);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if ((iteration & 15) == 0) context.ThrowIfCancelled();
            var next = field.Clone();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (field[x, y] >= .5f) continue;
                var neighbours = 0; for (var oy=-1; oy<=1; oy++) for (var ox=-1; ox<=1; ox++) if ((ox!=0||oy!=0) && field.Sample(x+ox,y+oy)>=.5f) neighbours++;
                var probability = kind switch
                {
                    GrowthKind.CellularAutomata => neighbours >= 3 ? density * 2f : 0f,
                    GrowthKind.DiffusionLimitedAggregation => neighbours > 0 ? density * .55f : 0f,
                    GrowthKind.Crystal => neighbours is 1 or 2 ? density * 1.6f : 0f,
                    GrowthKind.Fungal => neighbours > 0 ? density * (1f + neighbours * .12f) : density * .01f,
                    GrowthKind.Roots => neighbours == 1 ? density * 1.8f : 0f,
                    _ => neighbours == 1 ? density : neighbours == 2 ? density * .25f : 0f
                };
                if (random.NextDouble() < probability) next[x, y] = 1f;
            }
            field = next;
        }
        var blurred = MaterialOperations.Blur(field, kind == GrowthKind.Crystal ? 0 : 1);
        blurred.Normalize(); return new MaterialNodeResult().Add("Value", MaterialValue.From(blurred));
    }
}

public sealed class FlowFieldNode : MaterialNodeDefinitionBase
{
    public override string TypeId => "Simulation.FlowField";
    public override string DisplayName => "Flow Field";
    public override MaterialNodeCategory Category => MaterialNodeCategory.Simulation;
    public override TileabilitySupport Tileability => TileabilitySupport.NativePeriodic;
    public override IReadOnlyList<MaterialPortDefinition> Inputs => new[] { new MaterialPortDefinition("Height", MaterialDataType.Scalar) };
    public override IReadOnlyList<MaterialPortDefinition> Outputs => new[] { new MaterialPortDefinition("FlowX", MaterialDataType.Scalar), new MaterialPortDefinition("FlowY", MaterialDataType.Scalar), new MaterialPortDefinition("Curl", MaterialDataType.Scalar) };
    public override IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; } = NodeUtilities.Defaults(("Frequency", MaterialParameterValue.From(4f)), ("Strength", MaterialParameterValue.From(1f)));

    public override MaterialNodeResult Evaluate(MaterialNodeContext context)
    {
        var height = context.Input("Height") is null ? null : NodeUtilities.ToScalar(context.Input("Height"), context.Evaluation.Width, context.Evaluation.Height);
        var fx = new ScalarField(context.Evaluation.Width, context.Evaluation.Height); var fy = new ScalarField(fx.Width, fx.Height); var curl = new ScalarField(fx.Width, fx.Height);
        var frequency = context.Float("Frequency", 4f); var strength = context.Float("Strength", 1f);
        for (var y=0; y<fx.Height; y++) for (var x=0; x<fx.Width; x++)
        {
            float H(int px,int py) => height?.Sample(px,py) ?? NoiseFunctions.Perlin(px/(float)fx.Width*frequency, py/(float)fx.Height*frequency, context.Evaluation.Seed, Math.Max(1,(int)frequency), Math.Max(1,(int)frequency));
            var dx = (H(x+1,y)-H(x-1,y))*strength; var dy=(H(x,y+1)-H(x,y-1))*strength;
            fx[x,y]=Math.Clamp(.5f-dy*.5f,0f,1f); fy[x,y]=Math.Clamp(.5f+dx*.5f,0f,1f); curl[x,y]=Math.Clamp(.5f+(dx-dy)*.5f,0f,1f);
        }
        return new MaterialNodeResult().Add("FlowX", MaterialValue.From(fx)).Add("FlowY", MaterialValue.From(fy)).Add("Curl", MaterialValue.From(curl));
    }
}

public sealed class PatchMatchNode : MaterialNodeDefinitionBase
{
    public override string TypeId => "Synthesis.PatchMatchInpaint";
    public override string DisplayName => "PatchMatch Inpaint";
    public override MaterialNodeCategory Category => MaterialNodeCategory.Synthesis;
    public override TileabilitySupport Tileability => TileabilitySupport.RequiresRepair;
    public override IReadOnlyList<MaterialPortDefinition> Inputs => new[] { new MaterialPortDefinition("Image", MaterialDataType.Color, true), new MaterialPortDefinition("Mask", MaterialDataType.Mask, true) };
    public override IReadOnlyDictionary<string, MaterialParameterValue> DefaultParameters { get; } = NodeUtilities.Defaults(("Iterations", MaterialParameterValue.From(5)), ("SeedOffset", MaterialParameterValue.From(0)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext context)
    {
        var image=NodeUtilities.ToColor(context.Input("Image"),context.Evaluation.Width,context.Evaluation.Height);var mask=NodeUtilities.ToScalar(context.Input("Mask"),context.Evaluation.Width,context.Evaluation.Height);
        return new MaterialNodeResult().Add("Value",MaterialValue.From(TextureSynthesisAlgorithms.InpaintPatchMatch(image,mask,context.Int("Iterations",5),context.Evaluation.Seed+context.Int("SeedOffset"),context.Evaluation.CancellationToken)));
    }
}

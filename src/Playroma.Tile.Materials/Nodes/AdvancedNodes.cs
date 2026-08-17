using System.Drawing;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Nodes;

public sealed class ReactionDiffusionNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Simulation.ReactionDiffusion";public override string DisplayName=>"Reaction Diffusion";public override MaterialNodeCategory Category=>MaterialNodeCategory.Simulation;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Iterations",MaterialParameterValue.From(900)),("Feed",MaterialParameterValue.From(.055f)),("Kill",MaterialParameterValue.From(.062f)),("DiffusionA",MaterialParameterValue.From(1f)),("DiffusionB",MaterialParameterValue.From(.5f)),("TimeStep",MaterialParameterValue.From(1f)),("SeedOffset",MaterialParameterValue.From(0)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var iterations=c.Evaluation.Quality switch{MaterialQuality.Draft=>Math.Min(160,c.Int("Iterations",900)),MaterialQuality.Interactive=>Math.Min(450,c.Int("Iterations",900)),_=>c.Int("Iterations",900)};var field=SimulationAlgorithms.ReactionDiffusion(c.Evaluation.Width,c.Evaluation.Height,c.Evaluation.Seed+c.Int("SeedOffset"),iterations,c.Float("Feed",.055f),c.Float("Kill",.062f),c.Float("DiffusionA",1f),c.Float("DiffusionB",.5f),c.Float("TimeStep",1f),c.Evaluation.CancellationToken,c.Evaluation.Progress);return new MaterialNodeResult().Add("Value",MaterialValue.From(field));}
}

public sealed class ErosionNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Simulation.Erosion";public override string DisplayName=>"Erosion / Weathering";public override MaterialNodeCategory Category=>MaterialNodeCategory.Simulation;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Height",MaterialDataType.Scalar,true)};public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Height",MaterialDataType.Scalar),new MaterialPortDefinition("Wear",MaterialDataType.Scalar),new MaterialPortDefinition("Moisture",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Iterations",MaterialParameterValue.From(40)),("Talus",MaterialParameterValue.From(.025f)),("Strength",MaterialParameterValue.From(.35f)),("CavityDirt",MaterialParameterValue.From(.45f)),("EdgeWear",MaterialParameterValue.From(.25f)),("Moisture",MaterialParameterValue.From(.15f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToScalar(c.Input("Height"),c.Evaluation.Width,c.Evaluation.Height);var iterations=c.Evaluation.Quality==MaterialQuality.Draft?Math.Min(12,c.Int("Iterations",40)):c.Int("Iterations",40);var eroded=SimulationAlgorithms.ThermalErosion(input,iterations,c.Float("Talus",.025f),c.Float("Strength",.35f),c.Evaluation.CancellationToken);var weather=SimulationAlgorithms.Weather(eroded,c.Float("CavityDirt",.45f),c.Float("EdgeWear",.25f),c.Float("Moisture",.15f),c.Evaluation.Seed);var wear=new ScalarField(input.Width,input.Height);var moisture=new ScalarField(input.Width,input.Height);for(var i=0;i<wear.Values.Length;i++){wear.Values[i]=Math.Clamp(MathF.Abs(input.Values[i]-eroded.Values[i])*4f,0f,1f);moisture.Values[i]=Math.Clamp(weather.Values[i]-eroded.Values[i]+.5f,0f,1f);}return new MaterialNodeResult().Add("Height",MaterialValue.From(eroded)).Add("Wear",MaterialValue.From(wear)).Add("Moisture",MaterialValue.From(moisture));}
}

public sealed class SeamRepairNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Synthesis.SeamRepair";public override string DisplayName=>"Seam Repair";public override MaterialNodeCategory Category=>MaterialNodeCategory.Synthesis;public override TileabilitySupport Tileability=>TileabilitySupport.RequiresRepair;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Color,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("BlendWidth",MaterialParameterValue.From(8)),("MirrorEdges",MaterialParameterValue.From(false)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToColor(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);return new MaterialNodeResult().Add("Value",MaterialValue.From(TextureSynthesisAlgorithms.MakeSeamless(input,c.Int("BlendWidth",8),c.Bool("MirrorEdges"))));}
}

public sealed class ImageQuiltNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Synthesis.ImageQuilt";public override string DisplayName=>"Image Quilting";public override MaterialNodeCategory Category=>MaterialNodeCategory.Synthesis;public override TileabilitySupport Tileability=>TileabilitySupport.RequiresRepair;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Exemplar",MaterialDataType.Color,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("PatchSize",MaterialParameterValue.From(24)),("Overlap",MaterialParameterValue.From(6)),("SeedOffset",MaterialParameterValue.From(0)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToColor(c.Input("Exemplar"),c.Evaluation.Width,c.Evaluation.Height);var result=TextureSynthesisAlgorithms.Quilt(input,c.Evaluation.Width,c.Evaluation.Height,c.Int("PatchSize",24),c.Int("Overlap",6),c.Evaluation.Seed+c.Int("SeedOffset"),c.Evaluation.CancellationToken);return new MaterialNodeResult().Add("Value",MaterialValue.From(result));}
}

public sealed class PixelArtNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"PixelArt.Finish";public override string DisplayName=>"Pixel Art Finish";public override MaterialNodeCategory Category=>MaterialNodeCategory.PixelArt;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Color,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Width",MaterialParameterValue.From(48)),("Height",MaterialParameterValue.From(48)),("Colors",MaterialParameterValue.From(24)),("Dither",MaterialParameterValue.FromEnum(DitherMode.None)),("RemoveIslands",MaterialParameterValue.From(true)),("SnapAlpha",MaterialParameterValue.From(true)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToColor(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var result=PixelArtProcessor.Process(input,Math.Clamp(c.Int("Width",48),1,2048),Math.Clamp(c.Int("Height",48),1,2048),c.Int("Colors",24),c.Enum("Dither",DitherMode.None),c.Bool("RemoveIslands",true),c.Bool("SnapAlpha",true));if(result.Width!=c.Evaluation.Width||result.Height!=c.Evaluation.Height)result=result.Resize(c.Evaluation.Width,c.Evaluation.Height,true);return new MaterialNodeResult().Add("Value",MaterialValue.From(result));}
}

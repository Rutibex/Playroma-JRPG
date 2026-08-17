using System.Drawing;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Nodes;

public sealed class ConstantColorNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Source.Color";public override string DisplayName=>"Constant Color";public override string Description=>"Fills the material with one RGBA color.";public override MaterialNodeCategory Category=>MaterialNodeCategory.Sources;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Color",MaterialParameterValue.From(Color.ForestGreen)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var color=Rgba32.FromColor(c.Color("Color",Color.ForestGreen));return new MaterialNodeResult().Add("Value",MaterialValue.From(new ColorBuffer(c.Evaluation.Width,c.Evaluation.Height,color)));}
}

public sealed class ConstantScalarNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Source.Scalar";public override string DisplayName=>"Constant Scalar";public override MaterialNodeCategory Category=>MaterialNodeCategory.Sources;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Value",MaterialParameterValue.From(.5f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c)=>new MaterialNodeResult().Add("Value",MaterialValue.From(new ScalarField(c.Evaluation.Width,c.Evaluation.Height,c.Float("Value",.5f))));
}

public sealed class CoordinateNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Source.Coordinates";public override string DisplayName=>"Coordinates";public override MaterialNodeCategory Category=>MaterialNodeCategory.Sources;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;
    public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("X",MaterialDataType.Scalar),new MaterialPortDefinition("Y",MaterialDataType.Scalar),new MaterialPortDefinition("Radial",MaterialDataType.Scalar),new MaterialPortDefinition("Angle",MaterialDataType.Scalar)};
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var xField=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);var yField=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);var radial=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);var angle=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);for(var y=0;y<c.Evaluation.Height;y++)for(var x=0;x<c.Evaluation.Width;x++){var u=x/(float)Math.Max(1,c.Evaluation.Width-1);var v=y/(float)Math.Max(1,c.Evaluation.Height-1);xField[x,y]=u;yField[x,y]=v;var dx=u-.5f;var dy=v-.5f;radial[x,y]=Math.Clamp(MathF.Sqrt(dx*dx+dy*dy)*2f,0f,1f);angle[x,y]=(MathF.Atan2(dy,dx)+MathF.PI)/MathF.Tau;}return new MaterialNodeResult().Add("X",MaterialValue.From(xField)).Add("Y",MaterialValue.From(yField)).Add("Radial",MaterialValue.From(radial)).Add("Angle",MaterialValue.From(angle));}
}

public sealed class WhiteNoiseNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Source.WhiteNoise";public override string DisplayName=>"White Noise";public override MaterialNodeCategory Category=>MaterialNodeCategory.Sources;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("SeedOffset",MaterialParameterValue.From(0)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var field=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);var seed=c.Evaluation.Seed+c.Int("SeedOffset");for(var y=0;y<field.Height;y++)for(var x=0;x<field.Width;x++)field[x,y]=NoiseFunctions.Hash01(x,y,seed);return new MaterialNodeResult().Add("Value",MaterialValue.From(field));}
}

public sealed class ImageInputNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Source.Image";public override string DisplayName=>"Image Input";public override MaterialNodeCategory Category=>MaterialNodeCategory.Sources;public override TileabilitySupport Tileability=>TileabilitySupport.None;
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Path",new MaterialParameterValue{Kind=MaterialParameterKind.FilePath,Value=string.Empty}),("Nearest",MaterialParameterValue.From(false)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var path=c.String("Path");if(c.Evaluation.AssetResolver is not null)path=c.Evaluation.AssetResolver(path);if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new FileNotFoundException("The material image input could not be found.",path);using var image=Image.FromFile(path);var buffer=ColorBuffer.FromBitmap(image);if(buffer.Width!=c.Evaluation.Width||buffer.Height!=c.Evaluation.Height)buffer=buffer.Resize(c.Evaluation.Width,c.Evaluation.Height,c.Bool("Nearest"));return new MaterialNodeResult().Add("Value",MaterialValue.From(buffer));}
}

public sealed class GradientMapNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Color.GradientMap";public override string DisplayName=>"Gradient Map";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Palette",MaterialParameterValue.From("#142014,#50783c,#a8cd64")));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var field=NodeUtilities.ToScalar(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);return new MaterialNodeResult().Add("Value",MaterialValue.From(MaterialOperations.GradientMap(field,NodeUtilities.ParsePalette(c.String("Palette")))));}
}

public sealed class BlendNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Color.Blend";public override string DisplayName=>"Blend";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Background",MaterialDataType.Color,true),new MaterialPortDefinition("Foreground",MaterialDataType.Color,true),new MaterialPortDefinition("Mask",MaterialDataType.Mask)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Mode",MaterialParameterValue.FromEnum(BlendMode.Normal)),("Opacity",MaterialParameterValue.From(1f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var a=NodeUtilities.ToColor(c.Input("Background"),c.Evaluation.Width,c.Evaluation.Height);var b=NodeUtilities.ToColor(c.Input("Foreground"),c.Evaluation.Width,c.Evaluation.Height);var mask=c.Input("Mask") is null?null:NodeUtilities.ToScalar(c.Input("Mask"),c.Evaluation.Width,c.Evaluation.Height);return new MaterialNodeResult().Add("Value",MaterialValue.From(MaterialOperations.Blend(a,b,mask,c.Enum("Mode",BlendMode.Normal),c.Float("Opacity",1f))));}
}

public sealed class MaterialOutputNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Material.Output";public override string DisplayName=>"Material Output";public override string Description=>"Collects the final material channels.";public override MaterialNodeCategory Category=>MaterialNodeCategory.Output;
    public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("BaseColor",MaterialDataType.Color,true),new MaterialPortDefinition("Alpha",MaterialDataType.Mask),new MaterialPortDefinition("Height",MaterialDataType.Scalar),new MaterialPortDefinition("Roughness",MaterialDataType.Scalar),new MaterialPortDefinition("Emission",MaterialDataType.Color),new MaterialPortDefinition("DecorationProbability",MaterialDataType.Scalar),new MaterialPortDefinition("Wear",MaterialDataType.Scalar),new MaterialPortDefinition("Moisture",MaterialDataType.Scalar),new MaterialPortDefinition("FlowX",MaterialDataType.Scalar),new MaterialPortDefinition("FlowY",MaterialDataType.Scalar)};
    public override IReadOnlyList<MaterialPortDefinition> Outputs=>Enum.GetNames<MaterialChannel>().Select(n=>new MaterialPortDefinition(n,n is "BaseColor" or "Emission"?MaterialDataType.Color:MaterialDataType.Scalar)).ToArray();
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var r=new MaterialNodeResult();foreach(var input in Inputs){var value=c.Input(input.Name);if(value is not null)r.Add(input.Name,value);}return r;}
}

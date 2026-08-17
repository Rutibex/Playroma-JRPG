using System.Drawing;
using System.Numerics;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Nodes;

public sealed class NoiseNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Noise.Procedural";public override string DisplayName=>"Procedural Noise";public override MaterialNodeCategory Category=>MaterialNodeCategory.Noise;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("WarpX",MaterialDataType.Scalar),new MaterialPortDefinition("WarpY",MaterialDataType.Scalar)};public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar),new MaterialPortDefinition("CellEdge",MaterialDataType.Scalar),new MaterialPortDefinition("CellId",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Kind",MaterialParameterValue.FromEnum(NoiseKind.Fbm)),("Frequency",MaterialParameterValue.From(5f)),("Amplitude",MaterialParameterValue.From(1f)),("Octaves",MaterialParameterValue.From(5)),("Lacunarity",MaterialParameterValue.From(2f)),("Persistence",MaterialParameterValue.From(.5f)),("SeedOffset",MaterialParameterValue.From(0)),("WarpAmount",MaterialParameterValue.From(0f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c)
    {
        var value=new ScalarField(c.Evaluation.Width,c.Evaluation.Height);var edge=new ScalarField(value.Width,value.Height);var ids=new ScalarField(value.Width,value.Height);var kind=c.Enum("Kind",NoiseKind.Fbm);var frequency=Math.Max(.001f,c.Float("Frequency",5f));var amplitude=c.Float("Amplitude",1f);var octaves=Math.Clamp(c.Int("Octaves",5),1,12);var lacunarity=Math.Max(1.01f,c.Float("Lacunarity",2f));var persistence=Math.Clamp(c.Float("Persistence",.5f),.01f,.99f);var seed=c.Evaluation.Seed+c.Int("SeedOffset");var wx=c.Input("WarpX") is null?null:NodeUtilities.ToScalar(c.Input("WarpX"),value.Width,value.Height);var wy=c.Input("WarpY") is null?null:NodeUtilities.ToScalar(c.Input("WarpY"),value.Width,value.Height);var warpAmount=c.Float("WarpAmount",0f);var period=Math.Max(1,(int)MathF.Round(frequency));
        for(var y=0;y<value.Height;y++)for(var x=0;x<value.Width;x++)
        {
            if((x&63)==0)c.ThrowIfCancelled();var u=x/(float)value.Width*frequency;var v=y/(float)value.Height*frequency;if(wx is not null)u+=(wx[x,y]-.5f)*warpAmount;if(wy is not null)v+=(wy[x,y]-.5f)*warpAmount;float n;
            switch(kind)
            {
                case NoiseKind.Value:n=NoiseFunctions.Value(u,v,seed,period,period);break;
                case NoiseKind.Perlin:n=NoiseFunctions.Perlin(u,v,seed,period,period);break;
                case NoiseKind.Cellular:var cell=NoiseFunctions.Cellular(u,v,seed,period,period);n=Math.Clamp(cell.F1,0f,1f);edge[x,y]=Math.Clamp((cell.F2-cell.F1)*3f,0f,1f);ids[x,y]=(cell.CellId&0xFFFF)/65535f;break;
                case NoiseKind.Turbulence:n=NoiseFunctions.Turbulence(u,v,seed,octaves,lacunarity,persistence);break;
                case NoiseKind.Billow:n=NoiseFunctions.Billow(u,v,seed,octaves,lacunarity,persistence);break;
                case NoiseKind.Ridged:n=NoiseFunctions.Ridged(u,v,seed,octaves,lacunarity,persistence);break;
                case NoiseKind.DomainWarped:var dx=NoiseFunctions.Perlin(u*.7f,v*.7f,seed+17,period,period)-.5f;var dy=NoiseFunctions.Perlin(u*.7f,v*.7f,seed+31,period,period)-.5f;n=NoiseFunctions.Fractal(u+dx*2f,v+dy*2f,seed,octaves,lacunarity,persistence,(a,b,s)=>NoiseFunctions.Perlin(a,b,s,period,period));break;
                default:n=NoiseFunctions.Fractal(u,v,seed,octaves,lacunarity,persistence,(a,b,s)=>NoiseFunctions.Perlin(a,b,s,period,period));break;
            }
            value[x,y]=Math.Clamp(n*amplitude,0f,1f);
        }
        return new MaterialNodeResult().Add("Value",MaterialValue.From(value)).Add("CellEdge",MaterialValue.From(edge)).Add("CellId",MaterialValue.From(ids));
    }
}

public sealed class PatternNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Pattern.Procedural";public override string DisplayName=>"Pattern Generator";public override MaterialNodeCategory Category=>MaterialNodeCategory.Patterns;public override TileabilitySupport Tileability=>TileabilitySupport.NativePeriodic;public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar),new MaterialPortDefinition("Edge",MaterialDataType.Mask)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Kind",MaterialParameterValue.FromEnum(PatternKind.Bricks)),("Scale",MaterialParameterValue.From(16f)),("Gap",MaterialParameterValue.From(1.5f)),("Irregularity",MaterialParameterValue.From(.1f)),("SeedOffset",MaterialParameterValue.From(0)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var f=PatternGenerator.Generate(c.Enum("Kind",PatternKind.Bricks),c.Evaluation.Width,c.Evaluation.Height,c.Float("Scale",16f),c.Float("Gap",1.5f),c.Float("Irregularity",.1f),c.Evaluation.Seed+c.Int("SeedOffset"));var blur=MaterialOperations.Blur(f,1);var edge=new ScalarField(f.Width,f.Height);for(var y=0;y<f.Height;y++)for(var x=0;x<f.Width;x++)edge[x,y]=Math.Clamp(MathF.Abs(f[x,y]-blur[x,y])*4f,0f,1f);return new MaterialNodeResult().Add("Value",MaterialValue.From(f)).Add("Edge",MaterialValue.From(edge,true));}
}

public sealed class TransformNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Transform.Domain";public override string DisplayName=>"Transform / Warp";public override MaterialNodeCategory Category=>MaterialNodeCategory.Transform;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Color,true),new MaterialPortDefinition("DisplaceX",MaterialDataType.Scalar),new MaterialPortDefinition("DisplaceY",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("OffsetX",MaterialParameterValue.From(0f)),("OffsetY",MaterialParameterValue.From(0f)),("ScaleX",MaterialParameterValue.From(1f)),("ScaleY",MaterialParameterValue.From(1f)),("Rotation",MaterialParameterValue.From(0f)),("WarpAmount",MaterialParameterValue.From(0f)),("MirrorX",MaterialParameterValue.From(false)),("MirrorY",MaterialParameterValue.From(false)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c)
    {
        var source=NodeUtilities.ToColor(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var result=new ColorBuffer(c.Evaluation.Width,c.Evaluation.Height);var dx=c.Input("DisplaceX") is null?null:NodeUtilities.ToScalar(c.Input("DisplaceX"),result.Width,result.Height);var dy=c.Input("DisplaceY") is null?null:NodeUtilities.ToScalar(c.Input("DisplaceY"),result.Width,result.Height);var ox=c.Float("OffsetX")*source.Width;var oy=c.Float("OffsetY")*source.Height;var sx=Math.Max(.01f,c.Float("ScaleX",1f));var sy=Math.Max(.01f,c.Float("ScaleY",1f));var angle=-c.Float("Rotation")*MathF.PI/180f;var cos=MathF.Cos(angle);var sin=MathF.Sin(angle);var warp=c.Float("WarpAmount");var mirrorX=c.Bool("MirrorX");var mirrorY=c.Bool("MirrorY");
        for(var y=0;y<result.Height;y++)for(var x=0;x<result.Width;x++){var px=x-result.Width*.5f;var py=y-result.Height*.5f;var rx=(px*cos-py*sin)/sx+source.Width*.5f+ox;var ry=(px*sin+py*cos)/sy+source.Height*.5f+oy;if(mirrorX)rx=source.Width-rx;if(mirrorY)ry=source.Height-ry;if(dx is not null)rx+=(dx[x,y]-.5f)*warp;if(dy is not null)ry+=(dy[x,y]-.5f)*warp;result[x,y]=source.SampleBilinear(rx,ry,c.Evaluation.WrapX,c.Evaluation.WrapY);}return new MaterialNodeResult().Add("Value",MaterialValue.From(result));
    }
}

public sealed class FilterNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Filter.General";public override string DisplayName=>"Image Filter";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Color,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Kind",MaterialParameterValue.FromEnum(FilterKind.GaussianBlur)),("Radius",MaterialParameterValue.From(2)),("Amount",MaterialParameterValue.From(1f)),("Angle",MaterialParameterValue.From(0f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c)
    {
        var input=NodeUtilities.ToColor(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var kind=c.Enum("Kind",FilterKind.GaussianBlur);var radius=Math.Clamp(c.Int("Radius",2),1,32);var amount=c.Float("Amount",1f);ColorBuffer output;
        if(kind is FilterKind.BoxBlur or FilterKind.GaussianBlur or FilterKind.LowPass)output=MaterialOperations.Blur(input,radius);
        else if(kind==FilterKind.Median){output=input.Clone();for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var list=new List<Rgba32>();for(var oy=-radius;oy<=radius;oy++)for(var ox=-radius;ox<=radius;ox++)list.Add(input.Sample(x+ox,y+oy));output[x,y]=list.OrderBy(p=>p.R+p.G+p.B).ElementAt(list.Count/2);}}
        else if(kind==FilterKind.DirectionalBlur){output=new ColorBuffer(input.Width,input.Height);var angle=c.Float("Angle")*MathF.PI/180f;for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var acc=new Vector4();var count=0;for(var k=-radius;k<=radius;k++){var p=input.SampleBilinear(x+MathF.Cos(angle)*k,y+MathF.Sin(angle)*k);acc+=new Vector4(p.R,p.G,p.B,p.A);count++;}acc/=count;output[x,y]=new Rgba32((byte)acc.X,(byte)acc.Y,(byte)acc.Z,(byte)acc.W);}}
        else{var blurred=MaterialOperations.Blur(input,radius);output=new ColorBuffer(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var a=input[x,y];var b=blurred[x,y];float factor=kind switch{FilterKind.Sharpen or FilterKind.HighPass or FilterKind.LocalContrast=>amount,FilterKind.Emboss=>amount,_=>1f};var r=Math.Clamp(a.R+(a.R-b.R)*factor,0,255);var g=Math.Clamp(a.G+(a.G-b.G)*factor,0,255);var bl=Math.Clamp(a.B+(a.B-b.B)*factor,0,255);if(kind==FilterKind.Sobel){var lum=MaterialOperations.Luminance(input);var gx=lum.Sample(x+1,y)-lum.Sample(x-1,y);var gy=lum.Sample(x,y+1)-lum.Sample(x,y-1);var v=(byte)Math.Clamp((int)(MathF.Sqrt(gx*gx+gy*gy)*255f*amount),0,255);output[x,y]=new Rgba32(v,v,v,a.A);}else output[x,y]=new Rgba32((byte)r,(byte)g,(byte)bl,a.A);}}
        return new MaterialNodeResult().Add("Value",MaterialValue.From(output));
    }
}

public sealed class MorphologyNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Filter.Morphology";public override string DisplayName=>"Morphology";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar,true)};public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Kind",MaterialParameterValue.FromEnum(MorphologyKind.Dilate)),("Radius",MaterialParameterValue.From(1)),("Strength",MaterialParameterValue.From(1f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToScalar(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var radius=c.Int("Radius",1);var kind=c.Enum("Kind",MorphologyKind.Dilate);ScalarField result=kind switch{MorphologyKind.Dilate=>MaterialOperations.Morphology(input,radius,true),MorphologyKind.Erode=>MaterialOperations.Morphology(input,radius,false),MorphologyKind.Open=>MaterialOperations.Morphology(MaterialOperations.Morphology(input,radius,false),radius,true),MorphologyKind.Close=>MaterialOperations.Morphology(MaterialOperations.Morphology(input,radius,true),radius,false),MorphologyKind.Outline=>Outline(input,radius),MorphologyKind.Bevel=>Bevel(input,c.Float("Strength",1f)),MorphologyKind.Slope=>Slope(input,c.Float("Strength",1f)),MorphologyKind.Curvature=>Curvature(input,c.Float("Strength",1f)),MorphologyKind.AmbientOcclusion=>Ambient(input,radius,c.Float("Strength",1f)),_=>input.Clone()};return new MaterialNodeResult().Add("Value",MaterialValue.From(result));}
    private static ScalarField Outline(ScalarField input,int radius){var d=MaterialOperations.Morphology(input,radius,true);var e=MaterialOperations.Morphology(input,radius,false);var r=new ScalarField(input.Width,input.Height);for(var i=0;i<r.Values.Length;i++)r.Values[i]=Math.Clamp(d.Values[i]-e.Values[i],0f,1f);return r;}
    private static ScalarField Bevel(ScalarField input,float strength){var r=new ScalarField(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)r[x,y]=Math.Clamp(.5f+(input.Sample(x-1,y-1)-input.Sample(x+1,y+1))*strength,0f,1f);return r;}
    private static ScalarField Slope(ScalarField input,float strength){var r=new ScalarField(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var dx=input.Sample(x+1,y)-input.Sample(x-1,y);var dy=input.Sample(x,y+1)-input.Sample(x,y-1);r[x,y]=Math.Clamp(MathF.Sqrt(dx*dx+dy*dy)*strength,0f,1f);}return r;}
    private static ScalarField Curvature(ScalarField input,float strength){var r=new ScalarField(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var lap=input.Sample(x-1,y)+input.Sample(x+1,y)+input.Sample(x,y-1)+input.Sample(x,y+1)-4f*input[x,y];r[x,y]=Math.Clamp(.5f+lap*strength,0f,1f);}return r;}
    private static ScalarField Ambient(ScalarField input,int radius,float strength){var blur=MaterialOperations.Blur(input,radius);var r=new ScalarField(input.Width,input.Height);for(var i=0;i<r.Values.Length;i++)r.Values[i]=Math.Clamp(.5f+(input.Values[i]-blur.Values[i])*strength,0f,1f);return r;}
}

public sealed class ThresholdNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Filter.Threshold";public override string DisplayName=>"Threshold / Levels";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar,true)};public override IReadOnlyList<MaterialPortDefinition> Outputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Scalar)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Low",MaterialParameterValue.From(.25f)),("High",MaterialParameterValue.From(.75f)),("Smooth",MaterialParameterValue.From(true)),("Invert",MaterialParameterValue.From(false)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToScalar(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var output=new ScalarField(input.Width,input.Height);var low=c.Float("Low",.25f);var high=Math.Max(low+.0001f,c.Float("High",.75f));var smooth=c.Bool("Smooth",true);var invert=c.Bool("Invert");for(var i=0;i<output.Values.Length;i++){var v=smooth?SmoothStep(low,high,input.Values[i]):input.Values[i]>=high?1f:0f;output.Values[i]=invert?1f-v:v;}return new MaterialNodeResult().Add("Value",MaterialValue.From(output));}
    private static float SmoothStep(float a,float b,float v){var t=Math.Clamp((v-a)/(b-a),0f,1f);return t*t*(3f-2f*t);}
}

public sealed class ColorAdjustNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Color.Adjust";public override string DisplayName=>"Color Adjustment";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Value",MaterialDataType.Color,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("Brightness",MaterialParameterValue.From(0f)),("Contrast",MaterialParameterValue.From(1f)),("Saturation",MaterialParameterValue.From(1f)),("Hue",MaterialParameterValue.From(0f)),("Gamma",MaterialParameterValue.From(1f)),("Posterize",MaterialParameterValue.From(256)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var input=NodeUtilities.ToColor(c.Input("Value"),c.Evaluation.Width,c.Evaluation.Height);var output=new ColorBuffer(input.Width,input.Height);var brightness=c.Float("Brightness");var contrast=c.Float("Contrast",1f);var saturation=c.Float("Saturation",1f);var hue=c.Float("Hue")/360f;var gamma=Math.Max(.01f,c.Float("Gamma",1f));var levels=Math.Clamp(c.Int("Posterize",256),2,256);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var p=input[x,y];var (h,s,v)=RgbToHsv(p);h=(h+hue)%1f;if(h<0)h+=1f;s=Math.Clamp(s*saturation,0f,1f);v=MathF.Pow(Math.Clamp((v-.5f)*contrast+.5f+brightness,0f,1f),1f/gamma);var q=HsvToRgb(h,s,v,p.A);if(levels<256)q=new Rgba32(Q(q.R,levels),Q(q.G,levels),Q(q.B,levels),q.A);output[x,y]=q;}return new MaterialNodeResult().Add("Value",MaterialValue.From(output));}
    private static (float H,float S,float V) RgbToHsv(Rgba32 p){var r=p.R/255f;var g=p.G/255f;var b=p.B/255f;var max=Math.Max(r,Math.Max(g,b));var min=Math.Min(r,Math.Min(g,b));var d=max-min;var h=d==0?0:max==r?((g-b)/d)%6:max==g?(b-r)/d+2:(r-g)/d+4;h/=6;if(h<0)h+=1;return(h,max==0?0:d/max,max);}
    private static Rgba32 HsvToRgb(float h,float s,float v,byte a){var i=(int)MathF.Floor(h*6);var f=h*6-i;var p=v*(1-s);var q=v*(1-f*s);var t=v*(1-(1-f)*s);var tuple=(i%6) switch{0=>(v,t,p),1=>(q,v,p),2=>(p,v,t),3=>(p,q,v),4=>(t,p,v),_=>(v,p,q)};return new Rgba32((byte)(tuple.Item1*255),(byte)(tuple.Item2*255),(byte)(tuple.Item3*255),a);}
    private static byte Q(byte v,int levels)=> (byte)Math.Clamp((int)MathF.Round(MathF.Round(v/255f*(levels-1))/(levels-1)*255f),0,255);
}

public sealed class HeightShadeNode:MaterialNodeDefinitionBase
{
    public override string TypeId=>"Color.HeightShade";public override string DisplayName=>"Shade From Height";public override MaterialNodeCategory Category=>MaterialNodeCategory.Filter;public override IReadOnlyList<MaterialPortDefinition> Inputs=>new[]{new MaterialPortDefinition("Color",MaterialDataType.Color,true),new MaterialPortDefinition("Height",MaterialDataType.Scalar,true)};
    public override IReadOnlyDictionary<string,MaterialParameterValue> DefaultParameters{get;}=NodeUtilities.Defaults(("LightAngle",MaterialParameterValue.From(315f)),("Strength",MaterialParameterValue.From(.7f)));
    public override MaterialNodeResult Evaluate(MaterialNodeContext c){var color=NodeUtilities.ToColor(c.Input("Color"),c.Evaluation.Width,c.Evaluation.Height);var height=NodeUtilities.ToScalar(c.Input("Height"),c.Evaluation.Width,c.Evaluation.Height);return new MaterialNodeResult().Add("Value",MaterialValue.From(MaterialOperations.Shade(color,height,c.Float("LightAngle",315f),c.Float("Strength",.7f))));}
}

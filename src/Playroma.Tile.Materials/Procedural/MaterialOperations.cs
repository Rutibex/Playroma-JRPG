using System.Drawing;
using System.Numerics;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Procedural;

public static class MaterialOperations
{
    public static ScalarField Luminance(ColorBuffer input)
    {
        var result = new ScalarField(input.Width, input.Height);
        for (var y = 0; y < input.Height; y++)
        for (var x = 0; x < input.Width; x++)
        {
            var p = input[x,y]; result[x,y] = (0.2126f * p.R + 0.7152f * p.G + 0.0722f * p.B) / 255f;
        }
        return result;
    }

    public static ColorBuffer Gray(ScalarField input, byte alpha = 255)
    {
        var result = new ColorBuffer(input.Width, input.Height);
        for (var y=0;y<input.Height;y++) for (var x=0;x<input.Width;x++) { var v=(byte)Math.Clamp((int)MathF.Round(input[x,y]*255f),0,255); result[x,y]=new Rgba32(v,v,v,alpha); }
        return result;
    }

    public static ColorBuffer GradientMap(ScalarField field, IReadOnlyList<Rgba32> colors)
    {
        if (colors.Count == 0) colors = new[] { new Rgba32(0,0,0), new Rgba32(255,255,255) };
        var result = new ColorBuffer(field.Width, field.Height);
        for (var y=0;y<field.Height;y++) for (var x=0;x<field.Width;x++)
        {
            var v=Math.Clamp(field[x,y],0f,1f)*(colors.Count-1); var i=Math.Min(colors.Count-2,(int)MathF.Floor(v)); var t=v-i;
            result[x,y]=colors.Count==1?colors[0]:Rgba32.Lerp(colors[i],colors[i+1],t);
        }
        return result;
    }

    public static ColorBuffer Blend(ColorBuffer a, ColorBuffer b, ScalarField? mask, BlendMode mode, float opacity)
    {
        var width=Math.Max(a.Width,b.Width); var height=Math.Max(a.Height,b.Height); var result=new ColorBuffer(width,height);
        for(var y=0;y<height;y++) for(var x=0;x<width;x++)
        {
            var pa=a.Sample(x,y); var pb=b.Sample(x,y); var m=Math.Clamp((mask?.Sample(x,y)??1f)*opacity,0f,1f);
            result[x,y]=Rgba32.Lerp(pa,ApplyBlend(pa,pb,mode),m);
        }
        return result;
    }

    public static Rgba32 ApplyBlend(Rgba32 a, Rgba32 b, BlendMode mode)
    {
        byte C(byte x, byte y)
        {
            var xf=x/255f; var yf=y/255f; float v=mode switch
            {
                BlendMode.Multiply=>xf*yf, BlendMode.Screen=>1f-(1f-xf)*(1f-yf),
                BlendMode.Overlay=>xf<.5f?2f*xf*yf:1f-2f*(1f-xf)*(1f-yf),
                BlendMode.SoftLight=>(1f-2f*yf)*xf*xf+2f*yf*xf,
                BlendMode.HardLight=>yf<.5f?2f*xf*yf:1f-2f*(1f-xf)*(1f-yf),
                BlendMode.Add=>Math.Min(1f,xf+yf), BlendMode.Subtract=>Math.Max(0f,xf-yf),
                BlendMode.Difference=>MathF.Abs(xf-yf), BlendMode.Darken=>Math.Min(xf,yf),
                BlendMode.Lighten=>Math.Max(xf,yf), BlendMode.Minimum=>Math.Min(xf,yf), BlendMode.Maximum=>Math.Max(xf,yf),
                _=>yf
            }; return (byte)Math.Clamp((int)MathF.Round(v*255f),0,255);
        }
        return new Rgba32(C(a.R,b.R),C(a.G,b.G),C(a.B,b.B),(byte)Math.Max(a.A,b.A));
    }

    public static ScalarField Blur(ScalarField input,int radius,bool wrap=true)
    {
        radius=Math.Clamp(radius,0,64); if(radius==0)return input.Clone(); var temp=new ScalarField(input.Width,input.Height); var result=new ScalarField(input.Width,input.Height);
        for(var y=0;y<input.Height;y++) for(var x=0;x<input.Width;x++){var sum=0f;for(var k=-radius;k<=radius;k++)sum+=input.Sample(x+k,y,wrap,wrap);temp[x,y]=sum/(radius*2+1);}
        for(var y=0;y<input.Height;y++) for(var x=0;x<input.Width;x++){var sum=0f;for(var k=-radius;k<=radius;k++)sum+=temp.Sample(x,y+k,wrap,wrap);result[x,y]=sum/(radius*2+1);}
        return result;
    }

    public static ColorBuffer Blur(ColorBuffer input,int radius,bool wrap=true)
    {
        var r=new ScalarField(input.Width,input.Height);var g=new ScalarField(input.Width,input.Height);var b=new ScalarField(input.Width,input.Height);var a=new ScalarField(input.Width,input.Height);
        for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var p=input[x,y];r[x,y]=p.R/255f;g[x,y]=p.G/255f;b[x,y]=p.B/255f;a[x,y]=p.A/255f;}
        r=Blur(r,radius,wrap);g=Blur(g,radius,wrap);b=Blur(b,radius,wrap);a=Blur(a,radius,wrap);var output=new ColorBuffer(input.Width,input.Height);
        for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)output[x,y]=new Rgba32(ToByte(r[x,y]),ToByte(g[x,y]),ToByte(b[x,y]),ToByte(a[x,y]));
        return output;
    }

    public static ScalarField Morphology(ScalarField input,int radius,bool dilate)
    {
        var result=new ScalarField(input.Width,input.Height);radius=Math.Clamp(radius,1,32);
        for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++){var value=dilate?float.MinValue:float.MaxValue;for(var oy=-radius;oy<=radius;oy++)for(var ox=-radius;ox<=radius;ox++){if(ox*ox+oy*oy>radius*radius)continue;var s=input.Sample(x+ox,y+oy);value=dilate?Math.Max(value,s):Math.Min(value,s);}result[x,y]=value;}
        return result;
    }

    public static VectorField NormalFromHeight(ScalarField height,float strength)
    {
        var result=new VectorField(height.Width,height.Height);
        for(var y=0;y<height.Height;y++)for(var x=0;x<height.Width;x++){var dx=(height.Sample(x+1,y)-height.Sample(x-1,y))*strength;var dy=(height.Sample(x,y+1)-height.Sample(x,y-1))*strength;var n=Vector2.Normalize(new Vector2(-dx,-dy));if(float.IsNaN(n.X))n=Vector2.Zero;result[x,y]=n;}
        return result;
    }

    public static ColorBuffer Shade(ColorBuffer color,ScalarField height,float lightAngleDegrees,float strength)
    {
        var result=new ColorBuffer(color.Width,color.Height);var angle=lightAngleDegrees*MathF.PI/180f;var light=Vector2.Normalize(new Vector2(MathF.Cos(angle),MathF.Sin(angle)));
        for(var y=0;y<color.Height;y++)for(var x=0;x<color.Width;x++){var dx=height.Sample(x+1,y)-height.Sample(x-1,y);var dy=height.Sample(x,y+1)-height.Sample(x,y-1);var dot=(-dx*light.X-dy*light.Y)*strength;var p=color[x,y];result[x,y]=new Rgba32(Shift(p.R,dot),Shift(p.G,dot),Shift(p.B,dot),p.A);}
        return result;
    }

    public static ColorBuffer Warp(ColorBuffer input,ScalarField dx,ScalarField dy,float amount)
    {
        var result=new ColorBuffer(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)result[x,y]=input.SampleBilinear(x+(dx.Sample(x,y)-.5f)*amount,y+(dy.Sample(x,y)-.5f)*amount);return result;
    }

    public static ScalarField Warp(ScalarField input,ScalarField dx,ScalarField dy,float amount)
    {
        var result=new ScalarField(input.Width,input.Height);for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)result[x,y]=input.SampleBilinear(x+(dx.Sample(x,y)-.5f)*amount,y+(dy.Sample(x,y)-.5f)*amount);return result;
    }

    public static byte ToByte(float value)=> (byte)Math.Clamp((int)MathF.Round(value*255f),0,255);
    private static byte Shift(byte value,float amount)=> (byte)Math.Clamp((int)MathF.Round(value+amount*255f),0,255);
}

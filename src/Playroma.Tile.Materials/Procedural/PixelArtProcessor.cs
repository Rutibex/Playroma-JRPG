using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Procedural;

public static class PixelArtProcessor
{
    public static ColorBuffer Process(ColorBuffer input,int width,int height,int colorCount,DitherMode dither,bool removeIslands,bool snapAlpha)
    {
        var resized=Resize(input,width,height);var quantized=Quantize(resized,colorCount,dither);if(removeIslands)quantized=RemoveIsolatedPixels(quantized);if(snapAlpha)SnapAlpha(quantized);return quantized;
    }

    public static ColorBuffer Resize(ColorBuffer input,int width,int height)
    {
        var result=new ColorBuffer(width,height);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var sx0=x*input.Width/width;var sx1=Math.Max(sx0+1,(x+1)*input.Width/width);var sy0=y*input.Height/height;var sy1=Math.Max(sy0+1,(y+1)*input.Height/height);long r=0,g=0,b=0,a=0,count=0;
            for(var sy=sy0;sy<Math.Min(input.Height,sy1);sy++)for(var sx=sx0;sx<Math.Min(input.Width,sx1);sx++){var p=input[sx,sy];r+=p.R;g+=p.G;b+=p.B;a+=p.A;count++;}
            if(count==0)result[x,y]=input.Sample(sx0,sy0);else result[x,y]=new Rgba32((byte)(r/count),(byte)(g/count),(byte)(b/count),(byte)(a/count));
        }
        return result;
    }

    public static ColorBuffer Quantize(ColorBuffer input,int colorCount,DitherMode dither)
    {
        colorCount=Math.Clamp(colorCount,2,256);var palette=BuildKMeansPalette(input,colorCount,8);var result=input.Clone();var errors=new (float R,float G,float B)[input.Width*input.Height];
        for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)
        {
            var p=input[x,y];var e=errors[y*input.Width+x];var rr=Math.Clamp(p.R+e.R,0,255);var gg=Math.Clamp(p.G+e.G,0,255);var bb=Math.Clamp(p.B+e.B,0,255);var threshold=OrderedThreshold(x,y,dither);rr=Math.Clamp(rr+threshold,0,255);gg=Math.Clamp(gg+threshold,0,255);bb=Math.Clamp(bb+threshold,0,255);var q=Nearest(palette,rr,gg,bb,p.A);result[x,y]=q;
            if(dither is DitherMode.FloydSteinberg or DitherMode.Atkinson or DitherMode.SierraLite)Distribute(errors,input.Width,input.Height,x,y,rr-q.R,gg-q.G,bb-q.B,dither);
        }
        return result;
    }

    public static ColorBuffer RemoveIsolatedPixels(ColorBuffer input)
    {
        var result=input.Clone();for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)
        {
            var p=input[x,y];var neighbors=new[]{input.Sample(x-1,y),input.Sample(x+1,y),input.Sample(x,y-1),input.Sample(x,y+1)};if(neighbors.All(n=>ColorDistance(p,n)>900))result[x,y]=MostCommon(neighbors);
        }return result;
    }

    public static void SnapAlpha(ColorBuffer input){for(var i=0;i<input.Pixels.Length;i++){var p=input.Pixels[i];var a=p.A<64?(byte)0:p.A<192?(byte)128:(byte)255;input.Pixels[i]=p with{A=a};}}

    private static List<Rgba32> BuildKMeansPalette(ColorBuffer input,int count,int iterations)
    {
        var pixels=input.ReadOnlyPixels.ToArray();var palette=new List<(float R,float G,float B,float A)>();for(var i=0;i<count;i++){var p=pixels[i*pixels.Length/count];palette.Add((p.R,p.G,p.B,p.A));}
        var assignments=new int[pixels.Length];for(var iteration=0;iteration<iterations;iteration++)
        {
            for(var i=0;i<pixels.Length;i++){var p=pixels[i];var best=0;var bestD=float.MaxValue;for(var k=0;k<palette.Count;k++){var c=palette[k];var d=Sq(p.R-c.R)+Sq(p.G-c.G)+Sq(p.B-c.B)+Sq((p.A-c.A)*.5f);if(d<bestD){bestD=d;best=k;}}assignments[i]=best;}
            var sums=new (double R,double G,double B,double A,int N)[count];for(var i=0;i<pixels.Length;i++){var p=pixels[i];var k=assignments[i];sums[k].R+=p.R;sums[k].G+=p.G;sums[k].B+=p.B;sums[k].A+=p.A;sums[k].N++;}
            for(var k=0;k<count;k++)if(sums[k].N>0)palette[k]=((float)(sums[k].R/sums[k].N),(float)(sums[k].G/sums[k].N),(float)(sums[k].B/sums[k].N),(float)(sums[k].A/sums[k].N));
        }
        return palette.Select(c=>new Rgba32((byte)c.R,(byte)c.G,(byte)c.B,(byte)c.A)).Distinct().ToList();
    }

    private static Rgba32 Nearest(List<Rgba32> palette,float r,float g,float b,byte a){var best=palette[0];var bestD=float.MaxValue;foreach(var c in palette){var d=Sq(r-c.R)+Sq(g-c.G)+Sq(b-c.B)+Sq((a-c.A)*.5f);if(d<bestD){bestD=d;best=c;}}return best;}
    private static void Distribute((float R,float G,float B)[] errors,int width,int height,int x,int y,float er,float eg,float eb,DitherMode mode)
    {
        void Add(int ox,int oy,float weight){var xx=x+ox;var yy=y+oy;if(xx<0||yy<0||xx>=width||yy>=height)return;ref var e=ref errors[yy*width+xx];e=(e.R+er*weight,e.G+eg*weight,e.B+eb*weight);}
        if(mode==DitherMode.FloydSteinberg){Add(1,0,7/16f);Add(-1,1,3/16f);Add(0,1,5/16f);Add(1,1,1/16f);}else if(mode==DitherMode.Atkinson){foreach(var p in new[]{(1,0),(2,0),(-1,1),(0,1),(1,1),(0,2)})Add(p.Item1,p.Item2,1/8f);}else{Add(1,0,.5f);Add(-1,1,.25f);Add(0,1,.25f);}
    }
    private static float OrderedThreshold(int x,int y,DitherMode mode){int[,] m=mode switch{DitherMode.Bayer2=>new[,]{{0,2},{3,1}},DitherMode.Bayer4=>new[,]{{0,8,2,10},{12,4,14,6},{3,11,1,9},{15,7,13,5}},DitherMode.Bayer8=>Bayer8,_=>new[,]{{0}}};if(m.Length==1)return 0f;var n=m.GetLength(0);return ((m[y%n,x%n]+.5f)/(n*n)-.5f)*24f;}
    private static readonly int[,] Bayer8={{0,32,8,40,2,34,10,42},{48,16,56,24,50,18,58,26},{12,44,4,36,14,46,6,38},{60,28,52,20,62,30,54,22},{3,35,11,43,1,33,9,41},{51,19,59,27,49,17,57,25},{15,47,7,39,13,45,5,37},{63,31,55,23,61,29,53,21}};
    private static float ColorDistance(Rgba32 a,Rgba32 b)=>Sq(a.R-b.R)+Sq(a.G-b.G)+Sq(a.B-b.B);private static Rgba32 MostCommon(IEnumerable<Rgba32> values)=>values.GroupBy(x=>x).OrderByDescending(x=>x.Count()).First().Key;private static float Sq(float v)=>v*v;
}

using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Procedural;

public static class SimulationAlgorithms
{
    public static ScalarField ReactionDiffusion(int width,int height,int seed,int iterations,float feed,float kill,float diffA,float diffB,float dt,CancellationToken token,IProgress<MaterialProgress>? progress=null)
    {
        var a=new ScalarField(width,height,1f);var b=new ScalarField(width,height);var nextA=new ScalarField(width,height);var nextB=new ScalarField(width,height);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)if(NoiseFunctions.Hash01(x,y,seed)>.985f)b[x,y]=1f;
        iterations=Math.Max(1,iterations);
        for(var iteration=0;iteration<iterations;iteration++)
        {
            if((iteration&15)==0){token.ThrowIfCancellationRequested();progress?.Report(new MaterialProgress("Reaction diffusion",iteration/(float)iterations));}
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)
            {
                var av=a[x,y];var bv=b[x,y];var lapA=Laplacian(a,x,y);var lapB=Laplacian(b,x,y);var reaction=av*bv*bv;
                nextA[x,y]=Math.Clamp(av+(diffA*lapA-reaction+feed*(1f-av))*dt,0f,1f);
                nextB[x,y]=Math.Clamp(bv+(diffB*lapB+reaction-(kill+feed)*bv)*dt,0f,1f);
            }
            (a,nextA)=(nextA,a);(b,nextB)=(nextB,b);
        }
        b.Normalize();return b;
    }

    public static ScalarField ThermalErosion(ScalarField source,int iterations,float talus,float strength,CancellationToken token)
    {
        var field=source.Clone();var delta=new ScalarField(source.Width,source.Height);iterations=Math.Max(1,iterations);
        for(var iteration=0;iteration<iterations;iteration++)
        {
            if((iteration&7)==0)token.ThrowIfCancellationRequested();delta.Values.Clear();
            for(var y=0;y<field.Height;y++)for(var x=0;x<field.Width;x++)
            {
                var current=field[x,y];var bestX=x;var bestY=y;var bestDrop=0f;
                for(var oy=-1;oy<=1;oy++)for(var ox=-1;ox<=1;ox++){if(ox==0&&oy==0)continue;var sample=field.Sample(x+ox,y+oy);var drop=current-sample;if(drop>bestDrop){bestDrop=drop;bestX=NoiseFunctions.Mod(x+ox,field.Width);bestY=NoiseFunctions.Mod(y+oy,field.Height);}}
                if(bestDrop>talus){var amount=(bestDrop-talus)*strength*.5f;delta[x,y]-=amount;delta[bestX,bestY]+=amount;}
            }
            for(var i=0;i<field.Values.Length;i++)field.Values[i]+=delta.Values[i];
        }
        field.Normalize();return field;
    }

    public static ScalarField Weather(ScalarField source,float cavity,float edgeWear,float moisture,int seed)
    {
        var blur=MaterialOperations.Blur(source,2);var result=new ScalarField(source.Width,source.Height);
        for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++)
        {
            var local=source[x,y];var concavity=Math.Clamp(blur[x,y]-local,0f,1f);var edge=Math.Clamp(MathF.Abs(source.Sample(x+1,y)-source.Sample(x-1,y))+MathF.Abs(source.Sample(x,y+1)-source.Sample(x,y-1)),0f,1f);var noise=NoiseFunctions.Value(x*.08f,y*.08f,seed);
            result[x,y]=Math.Clamp(local+concavity*cavity-edge*edgeWear+noise*moisture*.15f,0f,1f);
        }
        return result;
    }

    private static float Laplacian(ScalarField f,int x,int y)=>f.Sample(x-1,y)*.2f+f.Sample(x+1,y)*.2f+f.Sample(x,y-1)*.2f+f.Sample(x,y+1)*.2f+f.Sample(x-1,y-1)*.05f+f.Sample(x+1,y-1)*.05f+f.Sample(x-1,y+1)*.05f+f.Sample(x+1,y+1)*.05f-f[x,y];
}

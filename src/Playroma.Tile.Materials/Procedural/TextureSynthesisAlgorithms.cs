using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Procedural;

public static class TextureSynthesisAlgorithms
{
    public static ColorBuffer MakeSeamless(ColorBuffer input, int blendWidth, bool mirrorEdges)
    {
        blendWidth=Math.Clamp(blendWidth,1,Math.Max(1,Math.Min(input.Width,input.Height)/2));var result=input.Clone();
        for(var y=0;y<input.Height;y++)for(var x=0;x<blendWidth;x++)
        {
            var t=(x+1f)/(blendWidth+1f);var left=input[x,y];var right=input[input.Width-blendWidth+x,y];
            if(mirrorEdges)right=input[input.Width-1-x,y];var mixed=Rgba32.Lerp(right,left,t);result[x,y]=mixed;result[input.Width-blendWidth+x,y]=mixed;
        }
        for(var x=0;x<input.Width;x++)for(var y=0;y<blendWidth;y++)
        {
            var t=(y+1f)/(blendWidth+1f);var top=result[x,y];var bottom=result[x,input.Height-blendWidth+y];
            if(mirrorEdges)bottom=result[x,input.Height-1-y];var mixed=Rgba32.Lerp(bottom,top,t);result[x,y]=mixed;result[x,input.Height-blendWidth+y]=mixed;
        }
        return result;
    }

    public static ColorBuffer Quilt(ColorBuffer exemplar,int width,int height,int patchSize,int overlap,int seed,CancellationToken token)
    {
        patchSize=Math.Clamp(patchSize,4,Math.Min(exemplar.Width,exemplar.Height));overlap=Math.Clamp(overlap,1,patchSize-1);var step=patchSize-overlap;var result=new ColorBuffer(width,height);var rng=new Random(seed);
        for(var oy=0;oy<height;oy+=step)for(var ox=0;ox<width;ox+=step)
        {
            token.ThrowIfCancellationRequested();var candidates=Math.Min(24,Math.Max(4,(exemplar.Width-patchSize+1)*(exemplar.Height-patchSize+1)));var bestX=0;var bestY=0;var bestCost=float.MaxValue;
            for(var c=0;c<candidates;c++)
            {
                var sx=rng.Next(0,Math.Max(1,exemplar.Width-patchSize+1));var sy=rng.Next(0,Math.Max(1,exemplar.Height-patchSize+1));var cost=PatchCost(result,exemplar,ox,oy,sx,sy,patchSize,overlap);
                if(cost<bestCost){bestCost=cost;bestX=sx;bestY=sy;}
            }
            PastePatch(result,exemplar,ox,oy,bestX,bestY,patchSize,overlap);
        }
        return MakeSeamless(result,Math.Max(1,overlap),false);
    }

    public static ColorBuffer InpaintPatchMatch(ColorBuffer input,ScalarField mask,int iterations,int seed,CancellationToken token)
    {
        var result=input.Clone();var rng=new Random(seed);var known=new List<(int X,int Y)>();
        for(var y=0;y<input.Height;y++)for(var x=0;x<input.Width;x++)if(mask[x,y]<.5f)known.Add((x,y));
        if(known.Count==0)return result;
        for(var iteration=0;iteration<Math.Max(1,iterations);iteration++)
        {
            token.ThrowIfCancellationRequested();var forward=iteration%2==0;var ys=forward?Enumerable.Range(0,input.Height):Enumerable.Range(0,input.Height).Reverse();var xs=forward?Enumerable.Range(0,input.Width):Enumerable.Range(0,input.Width).Reverse();
            foreach(var y in ys)foreach(var x in xs)if(mask[x,y]>=.5f)
            {
                var best=known[rng.Next(known.Count)];var bestCost=NeighborhoodCost(result,mask,x,y,best.X,best.Y,2);
                for(var attempt=0;attempt<8;attempt++){var candidate=known[rng.Next(known.Count)];var cost=NeighborhoodCost(result,mask,x,y,candidate.X,candidate.Y,2);if(cost<bestCost){best=candidate;bestCost=cost;}}
                result[x,y]=input[best.X,best.Y];
            }
        }
        return result;
    }

    private static float PatchCost(ColorBuffer target,ColorBuffer source,int tx,int ty,int sx,int sy,int size,int overlap)
    {
        var cost=0f;var count=0;
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {
            var dx=tx+x;var dy=ty+y;if(dx>=target.Width||dy>=target.Height)continue;var compare=(x<overlap&&tx>0)||(y<overlap&&ty>0);if(!compare)continue;var a=target[dx,dy];var b=source[sx+x,sy+y];var dr=a.R-b.R;var dg=a.G-b.G;var db=a.B-b.B;cost+=dr*dr+dg*dg+db*db;count++;
        }
        return count==0?0f:cost/count;
    }

    private static void PastePatch(ColorBuffer target,ColorBuffer source,int tx,int ty,int sx,int sy,int size,int overlap)
    {
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {
            var dx=tx+x;var dy=ty+y;if(dx>=target.Width||dy>=target.Height)continue;var p=source[sx+x,sy+y];var weight=1f;if(tx>0&&x<overlap)weight=Math.Min(weight,x/(float)overlap);if(ty>0&&y<overlap)weight=Math.Min(weight,y/(float)overlap);target[dx,dy]=weight<1f?Rgba32.Lerp(target[dx,dy],p,weight):p;
        }
    }

    private static float NeighborhoodCost(ColorBuffer image,ScalarField mask,int x,int y,int sx,int sy,int radius)
    {
        var cost=0f;var count=0;for(var oy=-radius;oy<=radius;oy++)for(var ox=-radius;ox<=radius;ox++)
        {
            var tx=NoiseFunctions.Mod(x+ox,image.Width);var ty=NoiseFunctions.Mod(y+oy,image.Height);if(mask[tx,ty]>=.5f)continue;var px=image[tx,ty];var qx=NoiseFunctions.Mod(sx+ox,image.Width);var qy=NoiseFunctions.Mod(sy+oy,image.Height);var q=image[qx,qy];var dr=px.R-q.R;var dg=px.G-q.G;var db=px.B-q.B;cost+=dr*dr+dg*dg+db*db;count++;
        }
        return count==0?float.MaxValue:cost/count;
    }
}

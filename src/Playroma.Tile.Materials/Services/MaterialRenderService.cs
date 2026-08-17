using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Services;

public sealed class MaterialRenderService
{
    private readonly MaterialGraphEvaluator _evaluator;
    public MaterialRenderService(MaterialNodeRegistry? registry=null)=>_evaluator=new MaterialGraphEvaluator(registry);
    public MaterialResult Render(MaterialRecipe recipe,int width,int height,int seed,MaterialQuality quality=MaterialQuality.Interactive,CancellationToken cancellationToken=default,IProgress<MaterialProgress>? progress=null,Func<string,string>? assetResolver=null,int frameCount=1)
    {
        var result=_evaluator.Evaluate(recipe,new MaterialEvaluationContext{Width=width,Height=height,Seed=seed,Quality=quality,CancellationToken=cancellationToken,Progress=progress,AssetResolver=assetResolver,FrameCount=frameCount});
        if(result.BaseColor is not null&&frameCount>1)
        {
            for(var frame=0;frame<frameCount;frame++){var source=result.BaseColor;var shifted=new ColorBuffer(source.Width,source.Height);var ox=(int)MathF.Round(frame*source.Width/(float)frameCount);var oy=(int)MathF.Round(frame*source.Height/(float)(frameCount*2));for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++)shifted[x,y]=source.Sample(x+ox,y+oy);result.Frames.Add(shifted);}
        }
        return result;
    }
}

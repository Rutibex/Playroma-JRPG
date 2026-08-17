using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Validation;

public sealed class MaterialValidator
{
    public IReadOnlyList<MaterialIssue> Validate(MaterialResult result)
    {
        var issues=new List<MaterialIssue>(result.Issues);if(result.BaseColor is null){issues.Add(new(MaterialIssueSeverity.Error,"NO_COLOR","The material does not produce a BaseColor channel."));return issues;}var color=result.BaseColor;if(color.ReadOnlyPixels.ToArray().All(x=>x.A==0))issues.Add(new(MaterialIssueSeverity.Error,"EMPTY","The material is completely transparent."));var seam=AnalyzeSeams(color);if(seam.Horizontal>8f)issues.Add(new(MaterialIssueSeverity.Warning,"SEAM_X",$"Horizontal seam error is {seam.Horizontal:0.0}."));if(seam.Vertical>8f)issues.Add(new(MaterialIssueSeverity.Warning,"SEAM_Y",$"Vertical seam error is {seam.Vertical:0.0}."));var colors=color.ReadOnlyPixels.ToArray().Distinct().Count();if(colors>128)issues.Add(new(MaterialIssueSeverity.Information,"COLOR_COUNT",$"The material uses {colors} unique colors; pixel-art reduction may improve readability."));if(result.Frames.Count>1){for(var i=0;i<result.Frames.Count;i++){var next=result.Frames[(i+1)%result.Frames.Count];var delta=FrameDifference(result.Frames[i],next);if(delta>80f)issues.Add(new(MaterialIssueSeverity.Warning,"ANIMATION_JUMP",$"Animation frames {i+1} and {(i+1)%result.Frames.Count+1} differ sharply ({delta:0.0})."));}}return issues;
    }
    public (float Horizontal,float Vertical,float Corner) AnalyzeSeams(ColorBuffer buffer)
    {
        double h=0,v=0;for(var y=0;y<buffer.Height;y++)h+=Distance(buffer[0,y],buffer[buffer.Width-1,y]);for(var x=0;x<buffer.Width;x++)v+=Distance(buffer[x,0],buffer[x,buffer.Height-1]);var corner=(float)((Distance(buffer[0,0],buffer[buffer.Width-1,buffer.Height-1])+Distance(buffer[buffer.Width-1,0],buffer[0,buffer.Height-1]))*.5);return((float)(h/buffer.Height),(float)(v/buffer.Width),corner);
    }
    private static float FrameDifference(ColorBuffer a,ColorBuffer b){double total=0;var count=Math.Min(a.ReadOnlyPixels.Length,b.ReadOnlyPixels.Length);for(var i=0;i<count;i++)total+=Distance(a.ReadOnlyPixels[i],b.ReadOnlyPixels[i]);return count==0?0:(float)(total/count);}
    private static double Distance(Rgba32 a,Rgba32 b)=>Math.Sqrt((a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B)+(a.A-b.A)*(a.A-b.A)*.25);
}

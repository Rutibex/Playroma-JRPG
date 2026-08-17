using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Procedural;

public static class PatternGenerator
{
    public static ScalarField Generate(PatternKind kind,int width,int height,float scale,float gap,float irregularity,int seed)
    {
        scale=Math.Max(2f,scale);gap=Math.Clamp(gap,0f,scale*.45f);var result=new ScalarField(width,height);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var nx=x/scale;var ny=y/scale;var value=kind switch
            {
                PatternKind.Checker=>((int)MathF.Floor(nx)+(int)MathF.Floor(ny))%2==0?1f:0f,
                PatternKind.Stripes=>Fract(nx)>=gap/scale?1f:0f,
                PatternKind.Bricks or PatternKind.RunningBond=>Brick(nx,ny,gap/scale,kind==PatternKind.RunningBond),
                PatternKind.Herringbone=>Herringbone(nx,ny,gap/scale),
                PatternKind.Planks=>Planks(nx,ny,gap/scale,seed),
                PatternKind.FloorTiles=>Tile(nx,ny,gap/scale),
                PatternKind.Shingles=>Shingle(nx,ny,gap/scale),
                PatternKind.Cobblestone or PatternKind.Flagstone=>Stone(nx,ny,seed,kind==PatternKind.Flagstone?0.55f:0.72f),
                PatternKind.Hexagons=>Hex(nx,ny,gap/scale),
                PatternKind.Grate=>Grate(nx,ny,gap/scale),
                PatternKind.RivetedPanels=>Panels(nx,ny,gap/scale),
                PatternKind.Circuit=>Circuit(nx,ny,seed),
                PatternKind.Cloth=>Cloth(nx,ny),
                PatternKind.Dots=>Dots(nx,ny),
                PatternKind.Waves=>.5f+.5f*MathF.Sin(nx*MathF.Tau+MathF.Sin(ny*MathF.Tau*.5f)),
                PatternKind.Dunes=>.5f+.5f*MathF.Sin((nx+NoiseFunctions.Perlin(nx*.2f,ny*.2f,seed))*MathF.Tau),
                PatternKind.Sediment=>.5f+.5f*MathF.Sin((ny+NoiseFunctions.Perlin(nx*.15f,ny*.15f,seed)*.4f)*MathF.Tau),
                PatternKind.Cracks=>Cracks(nx,ny,seed),
                PatternKind.Veins=>Veins(nx,ny,seed),
                _=>0f
            };
            if(irregularity>0f)value=Math.Clamp(value+(NoiseFunctions.Value(nx*3f,ny*3f,seed)-.5f)*irregularity,0f,1f);result[x,y]=value;
        }
        return result;
    }

    private static float Brick(float x,float y,float gap,bool offset){var row=(int)MathF.Floor(y);var u=Fract(x+(offset&&row%2!=0?.5f:0f));var v=Fract(y);return u>gap&&v>gap?1f:0f;}
    private static float Herringbone(float x,float y,float gap){var cellX=(int)MathF.Floor(x);var cellY=(int)MathF.Floor(y);var u=Fract(x);var v=Fract(y);return ((cellX+cellY)&1)==0?(MathF.Abs(u-v)>.15f+gap?1f:0f):(MathF.Abs((1-u)-v)>.15f+gap?1f:0f);}
    private static float Planks(float x,float y,float gap,int seed){var row=(int)MathF.Floor(y);var offset=NoiseFunctions.Hash01(row,0,seed)*2f;var u=Fract(x*.45f+offset);var v=Fract(y);return u>gap*.5f&&v>gap?Math.Clamp(.65f+NoiseFunctions.Value(x*2,y*2,seed)*.35f,0f,1f):0f;}
    private static float Tile(float x,float y,float gap){var u=Fract(x);var v=Fract(y);return u>gap&&v>gap?1f:0f;}
    private static float Shingle(float x,float y,float gap){var row=(int)MathF.Floor(y);var u=Fract(x+(row%2==0?0f:.5f));var v=Fract(y);var arc=MathF.Abs(u-.5f)*2f;return v>gap&&v<1f-arc*.35f?1f:0f;}
    private static float Stone(float x,float y,int seed,float threshold){var c=NoiseFunctions.Cellular(x,y,seed);var edge=Math.Clamp((c.F2-c.F1)*3f,0f,1f);return edge>threshold?1f:edge/threshold;}
    private static float Hex(float x,float y,float gap){var q=x*2f/3f;var r=(-x/3f+0.57735f*y);var rq=MathF.Round(q);var rr=MathF.Round(r);var cx=1.5f*rq;var cy=1.73205f*(rr+rq*.5f);var d=MathF.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy));return d<.86f-gap?1f:0f;}
    private static float Grate(float x,float y,float gap){var u=Fract(x);var v=Fract(y);return (u<.2f+gap||v<.2f+gap)?1f:0f;}
    private static float Panels(float x,float y,float gap){var u=Fract(x);var v=Fract(y);var edge=u<gap||v<gap;var rivet=(u<.16f||u>.84f)&&(v<.16f||v>.84f);return rivet?1f:edge?.25f:.65f;}
    private static float Circuit(float x,float y,int seed){var gx=(int)MathF.Floor(x);var gy=(int)MathF.Floor(y);var u=Fract(x);var v=Fract(y);var h=NoiseFunctions.Hash01(gx,gy,seed);var line=h<.5f?MathF.Abs(u-.5f)<.08f:MathF.Abs(v-.5f)<.08f;var pad=(u-.5f)*(u-.5f)+(v-.5f)*(v-.5f)<.04f;return line||pad?1f:0f;}
    private static float Cloth(float x,float y){var u=MathF.Abs(MathF.Sin(x*MathF.PI));var v=MathF.Abs(MathF.Sin(y*MathF.PI));return Math.Clamp((u+v)*.5f,0f,1f);}
    private static float Dots(float x,float y){var u=Fract(x)-.5f;var v=Fract(y)-.5f;return u*u+v*v<.12f?1f:0f;}
    private static float Cracks(float x,float y,int seed){var c=NoiseFunctions.Cellular(x,y,seed);return Math.Clamp(1f-(c.F2-c.F1)*18f,0f,1f);}
    private static float Veins(float x,float y,int seed){var n=NoiseFunctions.Perlin(x,y,seed);return Math.Clamp(1f-MathF.Abs(n-.5f)*8f,0f,1f);}
    private static float Fract(float v)=>v-MathF.Floor(v);
}

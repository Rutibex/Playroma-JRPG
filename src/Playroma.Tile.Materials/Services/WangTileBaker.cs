using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Services;

public sealed record WangTile(int North, int East, int South, int West, ColorBuffer Image);

public sealed class WangTileSet
{
    public int TileSize { get; init; }
    public int EdgeClassCount { get; init; }
    public List<WangTile> Tiles { get; } = new();
}

public sealed class WangTileBaker
{
    public WangTileSet Bake(ColorBuffer source, int tileSize = 48, int edgeClasses = 2, int variantsPerCombination = 1, int seed = 0)
    {
        tileSize = Math.Clamp(tileSize, 4, Math.Min(source.Width, source.Height));
        edgeClasses = Math.Clamp(edgeClasses, 2, 4); variantsPerCombination = Math.Clamp(variantsPerCombination, 1, 4);
        var result = new WangTileSet { TileSize = tileSize, EdgeClassCount = edgeClasses };
        var random = new Random(seed);
        for (var n=0; n<edgeClasses; n++) for (var e=0; e<edgeClasses; e++) for (var s=0; s<edgeClasses; s++) for (var w=0; w<edgeClasses; w++)
        for (var variant=0; variant<variantsPerCombination; variant++)
        {
            var ox = random.Next(source.Width); var oy = random.Next(source.Height); var tile = new ColorBuffer(tileSize, tileSize);
            for (var y=0; y<tileSize; y++) for (var x=0; x<tileSize; x++) tile[x,y]=source.Sample(ox+x,oy+y);
            ApplyEdgeClass(tile, n, e, s, w, edgeClasses); result.Tiles.Add(new WangTile(n,e,s,w,tile));
        }
        return result;
    }

    public ColorBuffer RenderPreview(WangTileSet set, int columns, int rows, int seed = 0)
    {
        columns=Math.Max(1,columns);rows=Math.Max(1,rows);var result=new ColorBuffer(columns*set.TileSize,rows*set.TileSize);var random=new Random(seed);var placed=new WangTile?[columns,rows];
        for(var y=0;y<rows;y++)for(var x=0;x<columns;x++)
        {
            var north=y>0?placed[x,y-1]!.South:(int?)null;var west=x>0?placed[x-1,y]!.East:(int?)null;var candidates=set.Tiles.Where(t=>(north is null||t.North==north)&&(west is null||t.West==west)).ToArray();var tile=candidates.Length==0?set.Tiles[random.Next(set.Tiles.Count)]:candidates[random.Next(candidates.Length)];placed[x,y]=tile;
            for(var py=0;py<set.TileSize;py++)for(var px=0;px<set.TileSize;px++)result[x*set.TileSize+px,y*set.TileSize+py]=tile.Image[px,py];
        }
        return result;
    }

    private static void ApplyEdgeClass(ColorBuffer tile,int north,int east,int south,int west,int count)
    {
        var band=Math.Max(1,tile.Width/12);for(var y=0;y<tile.Height;y++)for(var x=0;x<tile.Width;x++)
        {
            float shift=0;if(y<band)shift+=(north-(count-1)*.5f)*4f;if(y>=tile.Height-band)shift+=(south-(count-1)*.5f)*4f;if(x<band)shift+=(west-(count-1)*.5f)*4f;if(x>=tile.Width-band)shift+=(east-(count-1)*.5f)*4f;if(Math.Abs(shift)<.01f)continue;var p=tile[x,y];tile[x,y]=new Rgba32(Shift(p.R,shift),Shift(p.G,shift),Shift(p.B,shift),p.A);
        }
    }
    private static byte Shift(byte value,float amount)=>(byte)Math.Clamp((int)MathF.Round(value+amount),0,255);
}

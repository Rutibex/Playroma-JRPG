using System.Drawing;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;

namespace Playroma.Tile.Materials.Nodes;

internal static class NodeUtilities
{
    public static ScalarField ToScalar(MaterialValue? value,int width,int height,float fallback=0f)
    {
        if(value?.Scalar is { } scalar)return scalar;
        if(value?.Color is { } color)return MaterialOperations.Luminance(color);
        return new ScalarField(width,height,fallback);
    }

    public static ColorBuffer ToColor(MaterialValue? value,int width,int height,Rgba32 fallback=default)
    {
        if(value?.Color is { } color)return color;
        if(value?.Scalar is { } scalar)return MaterialOperations.Gray(scalar);
        return new ColorBuffer(width,height,fallback==default?new Rgba32(128,128,128):fallback);
    }

    public static IReadOnlyList<Rgba32> ParsePalette(string text)
    {
        var result=new List<Rgba32>();
        foreach(var part in (text??string.Empty).Split(new[]{',',';',' '},StringSplitOptions.RemoveEmptyEntries))
        {
            var value=MaterialParameterValue.From(part).AsColor(Color.Empty);if(value!=Color.Empty)result.Add(Rgba32.FromColor(value));
        }
        if(result.Count<2){result.Clear();result.Add(new Rgba32(20,30,20));result.Add(new Rgba32(80,140,60));result.Add(new Rgba32(170,205,100));}
        return result;
    }

    public static Dictionary<string,MaterialParameterValue> Defaults(params (string Name,MaterialParameterValue Value)[] values)=>values.ToDictionary(x=>x.Name,x=>x.Value,StringComparer.OrdinalIgnoreCase);
}

using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Serialization;

namespace Playroma.Tile.Materials.Services;

public sealed class MaterialCandidate:IDisposable
{
    public required MaterialRecipe Recipe{get;init;}public required MaterialResult Result{get;init;}public int Seed{get;init;}public bool IsKept{get;set;}public int Rating{get;set;}
    public void Dispose()=>Result.Dispose();
}

public sealed class MaterialCandidateGenerator
{
    private readonly MaterialRenderService _renderer=new();
    public async Task<IReadOnlyList<MaterialCandidate>> GenerateAsync(MaterialRecipe recipe,int count,int width,int height,int seed,float mutationStrength,CancellationToken token,IProgress<MaterialProgress>? progress=null,IReadOnlySet<string>? lockedParameters=null)
    {
        count=Math.Clamp(count,1,32);var tasks=new List<Task<MaterialCandidate>>();for(var i=0;i<count;i++){var index=i;tasks.Add(Task.Run(()=>{token.ThrowIfCancellationRequested();var clone=MaterialSerializer.DeepClone(recipe);var candidateSeed=unchecked(seed+index*7919);Mutate(clone,candidateSeed,mutationStrength,lockedParameters);var result=_renderer.Render(clone,width,height,candidateSeed,MaterialQuality.Interactive,token,null,null,clone.Category==MaterialCategory.Fluid?3:1);progress?.Report(new MaterialProgress("Generating candidates",(index+1f)/count));return new MaterialCandidate{Recipe=clone,Result=result,Seed=candidateSeed};},token));}return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static MaterialRecipe Breed(MaterialRecipe first,MaterialRecipe second,int seed)
    {
        var child=MaterialSerializer.DeepClone(first);var other=second.ExposedParameters.ToDictionary(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase);
        foreach(var exposed in child.ExposedParameters)
        {
            if(!other.TryGetValue(exposed.DisplayName,out var otherExposed))continue;var node=child.Graph.Find(exposed.NodeId);var otherNode=second.Graph.Find(otherExposed.NodeId);if(node is null||otherNode is null)continue;if(!node.Parameters.TryGetValue(exposed.ParameterName,out var a)||!otherNode.Parameters.TryGetValue(otherExposed.ParameterName,out var b))continue;var average=(a.AsFloat()+b.AsFloat())*.5f;a.Value=average.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        }
        child.Id=Guid.NewGuid();child.Name=$"{first.Name} Hybrid";child.Seed=seed;child.Revision=1;return child;
    }
    public static void Mutate(MaterialRecipe recipe,int seed,float strength,IReadOnlySet<string>? lockedParameters=null)
    {
        var random=new Random(seed);strength=Math.Clamp(strength,0f,1f);foreach(var exposed in recipe.ExposedParameters){if(lockedParameters?.Contains(exposed.DisplayName)==true)continue;var node=recipe.Graph.Find(exposed.NodeId);if(node is null||!node.Parameters.TryGetValue(exposed.ParameterName,out var value))continue;var current=value.AsFloat();var span=exposed.Maximum-exposed.Minimum;var delta=(float)(random.NextDouble()*2-1)*span*strength;value.Value=Math.Clamp(current+delta,exposed.Minimum,exposed.Maximum).ToString("R",System.Globalization.CultureInfo.InvariantCulture);}recipe.Seed=seed;recipe.Revision++;
    }
}

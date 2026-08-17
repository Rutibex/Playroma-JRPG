using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Materials.Serialization;

public static class MaterialSerializer
{
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
    public static MaterialRecipe Load(TileProject project,MaterialAssetReference reference)
    {
        var path=ProjectPath.Resolve(project,reference.RelativeRecipePath);var recipe=JsonSerializer.Deserialize<MaterialRecipe>(File.ReadAllText(path),Options)??throw new InvalidDataException($"Invalid material recipe: {path}");Normalize(recipe);return recipe;
    }
    public static MaterialAssetReference SaveNew(TileProject project,MaterialRecipe recipe,ColorBuffer? preview=null)
    {
        if(recipe.Id==Guid.Empty)recipe.Id=Guid.NewGuid();var folder=Path.Combine("Materials",recipe.Id.ToString("N"));var recipePath=Path.Combine(folder,"recipe.material.json");var thumbPath=Path.Combine(folder,"thumbnail.png");var reference=new MaterialAssetReference{Id=recipe.Id,Name=recipe.Name,RelativeRecipePath=recipePath,RelativeThumbnailPath=thumbPath,Revision=recipe.Revision,ModifiedUtc=recipe.ModifiedUtc};project.Materials.Add(reference);Save(project,recipe,preview,reference);return reference;
    }
    public static void Save(TileProject project,MaterialRecipe recipe,ColorBuffer? preview,MaterialAssetReference? reference=null)
    {
        Normalize(recipe);recipe.ModifiedUtc=DateTimeOffset.UtcNow;reference??=project.Materials.FirstOrDefault(x=>x.Id==recipe.Id);if(reference is null){SaveNew(project,recipe,preview);return;}CopyInputAssets(project,recipe,reference);reference.Name=recipe.Name;reference.Revision=recipe.Revision;reference.ModifiedUtc=recipe.ModifiedUtc;var path=ProjectPath.Resolve(project,reference.RelativeRecipePath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(recipe,Options));File.Move(temp,path,true);if(preview is not null){using var bmp=preview.ToBitmap();var thumbPath=ProjectPath.Resolve(project,reference.RelativeThumbnailPath);Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);bmp.Save(thumbPath,ImageFormat.Png);}
    }
    public static MaterialRecipe DeepClone(MaterialRecipe recipe){var json=JsonSerializer.Serialize(recipe,Options);return JsonSerializer.Deserialize<MaterialRecipe>(json,Options)!;}
    public static void DeleteFiles(TileProject project,MaterialAssetReference reference){var path=ProjectPath.Resolve(project,reference.RelativeRecipePath);var dir=Path.GetDirectoryName(path);if(!string.IsNullOrWhiteSpace(dir)&&Directory.Exists(dir))Directory.Delete(dir,true);}

    private static void CopyInputAssets(TileProject project,MaterialRecipe recipe,MaterialAssetReference reference)
    {
        var recipePath=ProjectPath.Resolve(project,reference.RelativeRecipePath);var folder=Path.GetDirectoryName(recipePath)!;var inputFolder=Path.Combine(folder,"Inputs");
        foreach(var node in recipe.Graph.Nodes.Where(x=>string.Equals(x.TypeId,"Source.Image",StringComparison.OrdinalIgnoreCase)))
        {
            if(!node.Parameters.TryGetValue("Path",out var parameter)||string.IsNullOrWhiteSpace(parameter.Value))continue;var source=parameter.Value;if(!Path.IsPathRooted(source))continue;if(!File.Exists(source))continue;Directory.CreateDirectory(inputFolder);var safe=Path.GetFileNameWithoutExtension(source);var ext=Path.GetExtension(source);var destination=Path.Combine(inputFolder,$"{safe}_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).Substring(0,8).ToLowerInvariant()}{ext}");File.Copy(source,destination,true);parameter.Value=ProjectPath.MakeRelative(project,destination);
        }
    }
    private static void Normalize(MaterialRecipe recipe){recipe.Name??="Material";recipe.Description??=string.Empty;recipe.Tags??=new();recipe.Graph??=new();recipe.Graph.Nodes??=new();recipe.ExposedParameters??=new();recipe.StyleProfile??=new();recipe.Provenance??=new();recipe.History??=new();if(recipe.Revision<1)recipe.Revision=1;foreach(var node in recipe.Graph.Nodes){node.TypeId??=string.Empty;node.Name??=string.Empty;node.Parameters??=new(StringComparer.OrdinalIgnoreCase);node.Inputs??=new(StringComparer.OrdinalIgnoreCase);}}
}
